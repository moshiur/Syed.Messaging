namespace Syed.Messaging.Sagas;

/// <summary>
/// Provides distributed locking for saga instances to prevent concurrent processing
/// of the same saga instance across multiple workers.
/// </summary>
public interface ISagaLockProvider
{
    /// <summary>
    /// Attempts to acquire a lock for a specific saga instance.
    /// </summary>
    /// <param name="sagaType">The saga type name</param>
    /// <param name="correlationKey">The saga correlation key</param>
    /// <param name="timeout">Maximum time to wait for the lock</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A lock handle if acquired, or null if the lock could not be acquired</returns>
    Task<ISagaLockHandle?> TryAcquireAsync(
        string sagaType,
        string correlationKey,
        TimeSpan timeout,
        CancellationToken ct = default);
}

/// <summary>
/// Handle to a saga lock. Disposing releases the lock.
/// </summary>
public interface ISagaLockHandle : IAsyncDisposable
{
    /// <summary>
    /// The saga type this lock is for.
    /// </summary>
    string SagaType { get; }
    
    /// <summary>
    /// The correlation key this lock is for.
    /// </summary>
    string CorrelationKey { get; }
    
    /// <summary>
    /// Whether the lock is still held.
    /// </summary>
    bool IsHeld { get; }
}

/// <summary>
/// In-memory saga lock provider for single-instance deployments.
/// Uses SemaphoreSlim for thread-safe locking within a single process.
/// </summary>
public sealed class InMemorySagaLockProvider : ISagaLockProvider
{
    private readonly Dictionary<string, SemaphoreSlim> _locks = new();
    private readonly object _lockObj = new();

    public async Task<ISagaLockHandle?> TryAcquireAsync(
        string sagaType,
        string correlationKey,
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        var key = $"{sagaType}:{correlationKey}";
        SemaphoreSlim semaphore;

        lock (_lockObj)
        {
            if (!_locks.TryGetValue(key, out semaphore!))
            {
                semaphore = new SemaphoreSlim(1, 1);
                _locks[key] = semaphore;
            }
        }

        var acquired = await semaphore.WaitAsync(timeout, ct);
        if (!acquired)
        {
            return null;
        }

        return new InMemoryLockHandle(sagaType, correlationKey, semaphore);
    }

    private sealed class InMemoryLockHandle : ISagaLockHandle
    {
        private readonly SemaphoreSlim _semaphore;
        private int _released;

        public InMemoryLockHandle(string sagaType, string correlationKey, SemaphoreSlim semaphore)
        {
            SagaType = sagaType;
            CorrelationKey = correlationKey;
            _semaphore = semaphore;
        }

        public string SagaType { get; }
        public string CorrelationKey { get; }
        public bool IsHeld => _released == 0;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.CompareExchange(ref _released, 1, 0) == 0)
            {
                _semaphore.Release();
            }
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>
/// A no-op lock provider that always succeeds. Use when locking is not required.
/// </summary>
public sealed class NoOpSagaLockProvider : ISagaLockProvider
{
    public Task<ISagaLockHandle?> TryAcquireAsync(
        string sagaType,
        string correlationKey,
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        return Task.FromResult<ISagaLockHandle?>(new NoOpLockHandle(sagaType, correlationKey));
    }

    private sealed class NoOpLockHandle : ISagaLockHandle
    {
        public NoOpLockHandle(string sagaType, string correlationKey)
        {
            SagaType = sagaType;
            CorrelationKey = correlationKey;
        }

        public string SagaType { get; }
        public string CorrelationKey { get; }
        public bool IsHeld => true;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
