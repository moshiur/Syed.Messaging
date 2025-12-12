using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Syed.Messaging;

namespace Syed.Messaging.Sagas;

public sealed class SagaTimeoutRequest
{
    public Guid Id { get; set; }
    public string SagaTypeKey { get; set; } = default!;
    public string TimeoutTypeKey { get; set; } = default!;
    public string? TimeoutTypeVersion { get; set; }
    public string CorrelationKey { get; set; } = default!;
    public byte[] Payload { get; set; } = Array.Empty<byte>();
    public DateTime DueAtUtc { get; set; }
    public bool Cancelled { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public interface ISagaTimeoutStore
{
    Task ScheduleAsync(SagaTimeoutRequest request, CancellationToken ct);
    Task CancelAsync(string sagaTypeKey, string correlationKey, string timeoutTypeKey, string? timeoutTypeVersion, CancellationToken ct);
    Task<IReadOnlyList<SagaTimeoutRequest>> GetDueAsync(DateTime nowUtc, int batchSize, CancellationToken ct);
    Task MarkProcessedAsync(Guid id, CancellationToken ct);
}

/// <summary>
/// Simple in-memory timeout store for demos. Not suitable for production.
/// </summary>
public sealed class InMemorySagaTimeoutStore : ISagaTimeoutStore
{
    private readonly List<SagaTimeoutRequest> _requests = new();
    private readonly object _lock = new();

    public Task ScheduleAsync(SagaTimeoutRequest request, CancellationToken ct)
    {
        lock (_lock)
        {
            _requests.Add(request);
        }
        return Task.CompletedTask;
    }

    public Task CancelAsync(string sagaTypeKey, string correlationKey, string timeoutTypeKey, string? timeoutTypeVersion, CancellationToken ct)
    {
        lock (_lock)
        {
            foreach (var r in _requests)
            {
                if (!r.Cancelled &&
                    r.SagaTypeKey == sagaTypeKey &&
                    r.TimeoutTypeKey == timeoutTypeKey &&
                    r.TimeoutTypeVersion == timeoutTypeVersion &&
                    r.CorrelationKey == correlationKey)
                {
                    r.Cancelled = true;
                }
            }
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SagaTimeoutRequest>> GetDueAsync(DateTime nowUtc, int batchSize, CancellationToken ct)
    {
        lock (_lock)
        {
            var due = _requests
                .Where(r => !r.Cancelled && r.DueAtUtc <= nowUtc)
                .OrderBy(r => r.DueAtUtc)
                .Take(batchSize)
                .ToList();

            return Task.FromResult((IReadOnlyList<SagaTimeoutRequest>)due);
        }
    }

    public Task MarkProcessedAsync(Guid id, CancellationToken ct)
    {
        lock (_lock)
        {
            _requests.RemoveAll(r => r.Id == id);
        }
        return Task.CompletedTask;
    }
}

public interface ISagaTimeoutScheduler
{
    Task ScheduleAsync<TTimeout>(Type sagaType, string correlationKey, TimeSpan delay, TTimeout timeout, CancellationToken ct = default);
    Task CancelAsync<TTimeout>(Type sagaType, string correlationKey, CancellationToken ct = default);
}

internal sealed class SagaTimeoutScheduler : ISagaTimeoutScheduler
{
    private readonly ISagaTimeoutStore _store;
    private readonly ISerializer _serializer;
    private readonly IMessageTypeRegistry _registry;

    public SagaTimeoutScheduler(ISagaTimeoutStore store, ISerializer serializer, IMessageTypeRegistry registry)
    {
        _store = store;
        _serializer = serializer;
        _registry = registry;
    }

    public Task ScheduleAsync<TTimeout>(Type sagaType, string correlationKey, TimeSpan delay, TTimeout timeout, CancellationToken ct = default)
    {
        // Get type key from registry, or use full name as fallback
        string timeoutTypeKey;
        string? timeoutTypeVersion = null;

        if (_registry.TryGetTypeKey(typeof(TTimeout), out var key, out var version))
        {
            timeoutTypeKey = key!;
            timeoutTypeVersion = version;
        }
        else
        {
            // Fallback: auto-register and use type name
            _registry.Register<TTimeout>();
            (timeoutTypeKey, timeoutTypeVersion) = _registry.GetTypeKey<TTimeout>();
        }

        var req = new SagaTimeoutRequest
        {
            Id = Guid.NewGuid(),
            SagaTypeKey = sagaType.FullName ?? sagaType.Name,
            TimeoutTypeKey = timeoutTypeKey,
            TimeoutTypeVersion = timeoutTypeVersion,
            CorrelationKey = correlationKey,
            Payload = _serializer.Serialize(timeout),
            DueAtUtc = DateTime.UtcNow + delay,
            CreatedAtUtc = DateTime.UtcNow,
            Cancelled = false
        };

        return _store.ScheduleAsync(req, ct);
    }

    public Task CancelAsync<TTimeout>(Type sagaType, string correlationKey, CancellationToken ct = default)
    {
        string timeoutTypeKey;
        string? timeoutTypeVersion = null;

        if (_registry.TryGetTypeKey(typeof(TTimeout), out var key, out var version))
        {
            timeoutTypeKey = key!;
            timeoutTypeVersion = version;
        }
        else
        {
            timeoutTypeKey = typeof(TTimeout).FullName ?? typeof(TTimeout).Name;
        }

        return _store.CancelAsync(
            sagaType.FullName ?? sagaType.Name,
            correlationKey,
            timeoutTypeKey,
            timeoutTypeVersion,
            ct);
    }
}

/// <summary>
/// Periodically polls the timeout store and dispatches timeout messages into the saga runtime.
/// </summary>
public sealed class SagaTimeoutDispatcher : BackgroundService
{
    private readonly ISagaTimeoutStore _store;
    private readonly ISerializer _serializer;
    private readonly IMessageTypeRegistry _registry;
    private readonly ISagaRuntime _runtime;
    private readonly ILogger<SagaTimeoutDispatcher> _logger;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(10);
    public int BatchSize { get; set; } = 100;

    public SagaTimeoutDispatcher(
        ISagaTimeoutStore store,
        ISerializer serializer,
        IMessageTypeRegistry registry,
        ISagaRuntime runtime,
        ILogger<SagaTimeoutDispatcher> logger)
    {
        _store = store;
        _serializer = serializer;
        _registry = registry;
        _runtime = runtime;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.UtcNow;
                var due = await _store.GetDueAsync(now, BatchSize, stoppingToken);

                foreach (var t in due)
                {
                    if (t.Cancelled)
                    {
                        await _store.MarkProcessedAsync(t.Id, stoppingToken);
                        continue;
                    }

                    try
                    {
                        var type = _registry.Resolve(t.TimeoutTypeKey, t.TimeoutTypeVersion);
                        if (type is null)
                        {
                            _logger.LogWarning(
                                "Could not resolve timeout type key '{TypeKey}' (version: {Version}). " +
                                "Ensure the type is registered with IMessageTypeRegistry.",
                                t.TimeoutTypeKey, t.TimeoutTypeVersion ?? "null");
                            await _store.MarkProcessedAsync(t.Id, stoppingToken);
                            continue;
                        }

                        var timeoutMessage = _serializer.Deserialize(t.Payload, type);

                        var ctx = new MessageContext
                        {
                            MessageId = Guid.NewGuid().ToString(),
                            CorrelationId = t.CorrelationKey,
                            RetryCount = 0,
                            Headers = new Dictionary<string, string>()
                        };

                        await _runtime.HandleAsync(timeoutMessage, ctx, stoppingToken);
                        await _store.MarkProcessedAsync(t.Id, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error dispatching saga timeout {Id}", t.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while processing saga timeouts.");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }
}

