using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Syed.Messaging;

namespace Syed.Messaging.Outbox;

public sealed class OutboxPublisherService : BackgroundService
{
    private readonly IOutboxStore _store;
    private readonly IMessageBus _bus;
    private readonly ISerializer _serializer;
    private readonly ILogger<OutboxPublisherService> _logger;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);
    public int BatchSize { get; set; } = 100;

    public OutboxPublisherService(IOutboxStore store, IMessageBus bus, ISerializer serializer, ILogger<OutboxPublisherService> logger)
    {
        _store = store;
        _bus = bus;
        _serializer = serializer;
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
                        var type = Type.GetType(msg.MessageType);
                        if (type is null)
                        {
                            _logger.LogWarning("Unable to resolve message type '{Type}' from outbox.", msg.MessageType);
                            continue;
                        }

                        var deserialized = _serializer.Deserialize(msg.Payload, type);

                        // use reflection to call generic PublishAsync<T>
                        var method = typeof(IMessageBus).GetMethod(nameof(IMessageBus.PublishAsync))!
                            .MakeGenericMethod(type);

                        await (Task)method.Invoke(_bus, new object?[] { msg.Destination, deserialized, stoppingToken })!;
                        await _store.MarkAsProcessedAsync(msg.Id, stoppingToken);
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
