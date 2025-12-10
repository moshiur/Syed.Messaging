using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Syed.Messaging;

namespace Syed.Messaging.Sagas;

public sealed class SagaTimeoutRequest
{
    public Guid Id { get; set; }
    public string SagaType { get; set; } = default!;
    public string TimeoutMessageType { get; set; } = default!;
    public string CorrelationKey { get; set; } = default!;
    public byte[] Payload { get; set; } = Array.Empty<byte>();
    public DateTime DueAtUtc { get; set; }
    public bool Cancelled { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public interface ISagaTimeoutStore
{
    Task ScheduleAsync(SagaTimeoutRequest request, CancellationToken ct);
    Task CancelAsync(Type sagaType, string correlationKey, Type timeoutMessageType, CancellationToken ct);
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

    public Task CancelAsync(Type sagaType, string correlationKey, Type timeoutMessageType, CancellationToken ct)
    {
        var sagaTypeName = sagaType.AssemblyQualifiedName ?? sagaType.FullName;
        var timeoutTypeName = timeoutMessageType.AssemblyQualifiedName ?? timeoutMessageType.FullName;

        lock (_lock)
        {
            foreach (var r in _requests)
            {
                if (!r.Cancelled &&
                    r.SagaType == sagaTypeName &&
                    r.TimeoutMessageType == timeoutTypeName &&
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

    public SagaTimeoutScheduler(ISagaTimeoutStore store, ISerializer serializer)
    {
        _store = store;
        _serializer = serializer;
    }

    public Task ScheduleAsync<TTimeout>(Type sagaType, string correlationKey, TimeSpan delay, TTimeout timeout, CancellationToken ct = default)
    {
        var req = new SagaTimeoutRequest
        {
            Id = Guid.NewGuid(),
            SagaType = sagaType.AssemblyQualifiedName ?? sagaType.FullName ?? sagaType.Name,
            TimeoutMessageType = typeof(TTimeout).AssemblyQualifiedName ?? typeof(TTimeout).FullName ?? typeof(TTimeout).Name,
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
        return _store.CancelAsync(
            sagaType,
            correlationKey,
            typeof(TTimeout),
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
    private readonly ISagaRuntime _runtime;
    private readonly ILogger<SagaTimeoutDispatcher> _logger;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(10);
    public int BatchSize { get; set; } = 100;

    public SagaTimeoutDispatcher(
        ISagaTimeoutStore store,
        ISerializer serializer,
        ISagaRuntime runtime,
        ILogger<SagaTimeoutDispatcher> logger)
    {
        _store = store;
        _serializer = serializer;
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
                        var type = Type.GetType(t.TimeoutMessageType);
                        if (type is null)
                        {
                            _logger.LogWarning("Could not resolve timeout type {Type}", t.TimeoutMessageType);
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
