using StackExchange.Redis;
using Syed.Messaging.Sagas;

namespace Syed.Messaging.Sagas.Redis;

/// <summary>
/// Redis-based saga lock provider for distributed deployments.
/// Uses Redis SET with NX (not exists) and PX (expiry) for atomic lock acquisition.
/// </summary>
public sealed class RedisSagaLockProvider : ISagaLockProvider
{
    private readonly IConnectionMultiplexer _redis;
    private readonly TimeSpan _lockExpiry;
    private readonly string _keyPrefix;

    /// <summary>
    /// Creates a new Redis saga lock provider.
    /// </summary>
    /// <param name="redis">Redis connection multiplexer</param>
    /// <param name="lockExpiry">How long locks are held before auto-expiring (default: 5 minutes)</param>
    /// <param name="keyPrefix">Prefix for Redis lock keys (default: "saga:lock:")</param>
    public RedisSagaLockProvider(
        IConnectionMultiplexer redis,
        TimeSpan? lockExpiry = null,
        string keyPrefix = "saga:lock:")
    {
        _redis = redis;
        _lockExpiry = lockExpiry ?? TimeSpan.FromMinutes(5);
        _keyPrefix = keyPrefix;
    }

    public async Task<ISagaLockHandle?> TryAcquireAsync(
        string sagaType,
        string correlationKey,
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var lockKey = $"{_keyPrefix}{sagaType}:{correlationKey}";
        var lockValue = Guid.NewGuid().ToString("N");
        
        var deadline = DateTime.UtcNow.Add(timeout);
        var retryDelay = TimeSpan.FromMilliseconds(50);

        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            // Try to acquire lock with SET NX PX
            var acquired = await db.StringSetAsync(
                lockKey,
                lockValue,
                _lockExpiry,
                When.NotExists);

            if (acquired)
            {
                return new RedisLockHandle(db, lockKey, lockValue, sagaType, correlationKey);
            }

            // Wait before retrying
            await Task.Delay(retryDelay, ct);
            
            // Exponential backoff up to 500ms
            retryDelay = TimeSpan.FromMilliseconds(Math.Min(retryDelay.TotalMilliseconds * 2, 500));
        }

        return null;
    }

    private sealed class RedisLockHandle : ISagaLockHandle
    {
        private readonly IDatabase _db;
        private readonly string _lockKey;
        private readonly string _lockValue;
        private int _released;

        public RedisLockHandle(IDatabase db, string lockKey, string lockValue, string sagaType, string correlationKey)
        {
            _db = db;
            _lockKey = lockKey;
            _lockValue = lockValue;
            SagaType = sagaType;
            CorrelationKey = correlationKey;
        }

        public string SagaType { get; }
        public string CorrelationKey { get; }
        public bool IsHeld => _released == 0;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.CompareExchange(ref _released, 1, 0) == 0)
            {
                // Use Lua script to atomically check and delete only if we own the lock
                const string script = @"
                    if redis.call('get', KEYS[1]) == ARGV[1] then
                        return redis.call('del', KEYS[1])
                    else
                        return 0
                    end";

                await _db.ScriptEvaluateAsync(script, new RedisKey[] { _lockKey }, new RedisValue[] { _lockValue });
            }
        }
    }
}
