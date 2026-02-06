using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Syed.Messaging;

namespace Syed.Messaging.Outbox;

public sealed class OutboxPublisherService : BackgroundService
{
    private readonly IOutboxStore _store;
    private readonly IMessageBus _bus;
    private readonly ISerializer _serializer;
    private readonly IMessageTypeRegistry _registry;
    private readonly ILogger<OutboxPublisherService> _logger;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);
    public int BatchSize { get; set; } = 100;
    
    /// <summary>
    /// When true, publishes raw bytes directly without type registry lookup.
    /// Useful for anonymous payloads or pre-serialized messages.
    /// </summary>
    public bool UseRawMode { get; set; } = false;

    public OutboxPublisherService(
        IOutboxStore store,
        IMessageBus bus,
        ISerializer serializer,
        IMessageTypeRegistry registry,
        ILogger<OutboxPublisherService> logger)
    {
        _store = store;
        _bus = bus;
        _serializer = serializer;
        _registry = registry;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var pending = await _store.GetPendingAsync(BatchSize, stoppingToken);

                foreach (var msg in pending)
                {
                    try
                    {
                        if (UseRawMode)
                        {
                            // Raw mode: publish bytes directly without deserializing
                            var headers = new Dictionary<string, string>
                            {
                                ["message-id"] = msg.Id.ToString(),
                                ["message-type"] = msg.MessageType,
                                ["correlation-id"] = msg.CorrelationId ?? string.Empty,
                                ["tenant-id"] = msg.TenantId?.ToString() ?? string.Empty
                            };
                            
                            await _bus.PublishRawAsync(msg.Destination, msg.Payload, msg.MessageType, headers, stoppingToken);
                            await _store.MarkAsProcessedAsync(msg.Id, stoppingToken);
                            
                            _logger.LogDebug("Published outbox message {MessageId} to {Destination} (raw mode)", 
                                msg.Id, msg.Destination);
                        }
                        else
                        {
                            // Typed mode: use registry for type resolution
                            var type = _registry.Resolve(msg.MessageType, msg.Version);
                            if (type is null)
                            {
                                _logger.LogWarning(
                                    "Unable to resolve message type key '{TypeKey}' (version: {Version}) from outbox. " +
                                    "Ensure the type is registered with IMessageTypeRegistry.",
                                    msg.MessageType, msg.Version ?? "null");
                                continue;
                            }

                            var deserialized = _serializer.Deserialize(msg.Payload, type);

                            // use reflection to call generic PublishAsync<T>
                            var method = typeof(IMessageBus).GetMethod(nameof(IMessageBus.PublishAsync))!
                                .MakeGenericMethod(type);

                            await (Task)method.Invoke(_bus, new object?[] { msg.Destination, deserialized, stoppingToken })!;
                            await _store.MarkAsProcessedAsync(msg.Id, stoppingToken);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to publish outbox message {MessageId}", msg.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while processing outbox messages.");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }
}

