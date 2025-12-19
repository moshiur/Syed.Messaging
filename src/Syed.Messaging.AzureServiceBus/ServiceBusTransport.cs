using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using Syed.Messaging;

namespace Syed.Messaging.AzureServiceBus;

public sealed class ServiceBusTransport : IMessageTransport, IAsyncDisposable
{
    private readonly ServiceBusOptions _options;
    private readonly ILogger<ServiceBusTransport> _logger;
    private readonly ServiceBusClient _client;

    public ServiceBusTransport(ServiceBusOptions options, ILogger<ServiceBusTransport> logger)
    {
        _options = options;
        _logger = logger;
        _client = new ServiceBusClient(options.ConnectionString);
    }

    public async Task PublishAsync(IMessageEnvelope envelope, string destination, CancellationToken ct)
    {
        var sender = _client.CreateSender(destination);
        var message = new ServiceBusMessage(envelope.Body)
        {
            Subject = envelope.MessageType,
            MessageId = envelope.MessageId ?? Guid.NewGuid().ToString(),
            CorrelationId = envelope.CorrelationId
        };

        foreach (var kv in envelope.Headers)
        {
            message.ApplicationProperties[kv.Key] = kv.Value;
        }

        await sender.SendMessageAsync(message, ct);
    }

    public Task SendAsync(IMessageEnvelope envelope, string destination, CancellationToken ct)
        => PublishAsync(envelope, destination, ct);

    public async Task SubscribeAsync(
        string subscriptionName,
        string destination,
        Func<IMessageEnvelope, CancellationToken, Task<TransportAcknowledge>> handler,
        CancellationToken ct)
    {
        // For simplicity, we treat destination as queue name when subscriptionName is empty,
        // or topic+subscription when provided.
        if (string.IsNullOrWhiteSpace(subscriptionName))
        {
            var processor = _client.CreateProcessor(destination, new ServiceBusProcessorOptions());
            processor.ProcessMessageAsync += async args =>
            {
                var envelope = ToEnvelope(args.Message);
                var result = await handler(envelope, ct);

                if (result == TransportAcknowledge.Ack)
                    await args.CompleteMessageAsync(args.Message, ct);
                else if (result == TransportAcknowledge.Retry)
                    await args.AbandonMessageAsync(args.Message, cancellationToken: ct);
                else
                    await args.DeadLetterMessageAsync(args.Message, cancellationToken: ct);
            };

            processor.ProcessErrorAsync += args =>
            {
                _logger.LogError(args.Exception, "Service Bus error.");
                return Task.CompletedTask;
            };

            await processor.StartProcessingAsync(ct);
        }
        else
        {
            var processor = _client.CreateProcessor(destination, subscriptionName, new ServiceBusProcessorOptions());
            processor.ProcessMessageAsync += async args =>
            {
                var envelope = ToEnvelope(args.Message);
                var result = await handler(envelope, ct);

                if (result == TransportAcknowledge.Ack)
                    await args.CompleteMessageAsync(args.Message, ct);
                else if (result == TransportAcknowledge.Retry)
                    await args.AbandonMessageAsync(args.Message, cancellationToken: ct);
                else
                    await args.DeadLetterMessageAsync(args.Message, cancellationToken: ct);
            };

            processor.ProcessErrorAsync += args =>
            {
                _logger.LogError(args.Exception, "Service Bus error.");
                return Task.CompletedTask;
            };

            await processor.StartProcessingAsync(ct);
        }
    }

    private MessageEnvelope ToEnvelope(ServiceBusReceivedMessage message)
    {
        var headers = new Dictionary<string, string>();

        foreach (var kv in message.ApplicationProperties)
        {
            headers[kv.Key] = kv.Value?.ToString() ?? string.Empty;
        }

        return new MessageEnvelope
        {
            MessageType = message.Subject ?? "unknown",
            MessageId = message.MessageId,
            CorrelationId = message.CorrelationId,
            CausationId = null,
            Headers = headers,
            Body = message.Body.ToArray()
        };
    }

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
    }
}
