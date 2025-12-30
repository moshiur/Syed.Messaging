using System.Diagnostics;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using Syed.Messaging;
using Syed.Messaging.Core;

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
        using var activity = MessagingDiagnostics.ActivitySource.StartActivity(
            MessagingDiagnostics.PublishActivityName,
            ActivityKind.Producer);

        if (activity is not null)
        {
            activity.SetTag("messaging.system", "azureservicebus");
            activity.SetTag("messaging.destination", destination);
            activity.SetTag("messaging.message_type", envelope.MessageType);
        }

        var sender = _client.CreateSender(destination);
        var message = new ServiceBusMessage(envelope.Body)
        {
            Subject = envelope.MessageType,
            MessageId = envelope.MessageId ?? Guid.NewGuid().ToString(),
            CorrelationId = envelope.CorrelationId
        };

        // Support session ID for saga correlation
        if (envelope.Headers.TryGetValue("session-id", out var sessionId))
        {
            message.SessionId = sessionId;
        }

        foreach (var kv in envelope.Headers)
        {
            message.ApplicationProperties[kv.Key] = kv.Value;
        }

        await sender.SendMessageAsync(message, ct);
        MessagingMetrics.MessagesPublished.Add(1, new KeyValuePair<string, object?>("message_type", envelope.MessageType));
    }

    public Task SendAsync(IMessageEnvelope envelope, string destination, CancellationToken ct)
        => PublishAsync(envelope, destination, ct);

    public Task<IMessageEnvelope> RequestAsync(IMessageEnvelope envelope, string destination, CancellationToken ct)
        => throw new NotSupportedException("RPC is not supported in Azure Service Bus transport");

    public async Task SubscribeAsync(
        string subscriptionName,
        string destination,
        Func<IMessageEnvelope, CancellationToken, Task<TransportAcknowledge>> handler,
        CancellationToken ct)
    {
        var processorOptions = new ServiceBusProcessorOptions
        {
            MaxConcurrentCalls = _options.MaxConcurrentCalls,
            AutoCompleteMessages = false
        };

        ServiceBusProcessor processor;

        if (string.IsNullOrWhiteSpace(subscriptionName))
        {
            processor = _client.CreateProcessor(destination, processorOptions);
        }
        else
        {
            processor = _client.CreateProcessor(destination, subscriptionName, processorOptions);
        }

        processor.ProcessMessageAsync += async args =>
        {
            var envelope = ToEnvelope(args.Message);
            var stopwatch = Stopwatch.StartNew();

            MessagingMetrics.MessagesReceived.Add(1, new KeyValuePair<string, object?>("message_type", envelope.MessageType));

            // Structured logging scope
            using var logScope = _logger.BeginScope(new Dictionary<string, object?>
            {
                ["MessageId"] = envelope.MessageId,
                ["CorrelationId"] = envelope.CorrelationId,
                ["MessageType"] = envelope.MessageType
            });

            try
            {
                var result = await handler(envelope, ct);
                stopwatch.Stop();

                MessagingMetrics.ProcessingDuration.Record(stopwatch.Elapsed.TotalMilliseconds,
                    new KeyValuePair<string, object?>("message_type", envelope.MessageType));

                switch (result)
                {
                    case TransportAcknowledge.Ack:
                        MessagingMetrics.MessagesProcessed.Add(1, new KeyValuePair<string, object?>("message_type", envelope.MessageType));
                        await args.CompleteMessageAsync(args.Message, ct);
                        break;

                    case TransportAcknowledge.Retry:
                        MessagingMetrics.MessagesRetried.Add(1, new KeyValuePair<string, object?>("message_type", envelope.MessageType));
                        await ScheduleRetryAsync(args, envelope, ct);
                        await args.CompleteMessageAsync(args.Message, ct);
                        break;

                    case TransportAcknowledge.DeadLetter:
                        MessagingMetrics.MessagesDeadLettered.Add(1, new KeyValuePair<string, object?>("message_type", envelope.MessageType));
                        await args.DeadLetterMessageAsync(args.Message, "MaxRetriesExceeded", "Message exceeded retry limit", ct);
                        break;
                }
            }
            catch (Exception ex)
            {
                MessagingMetrics.MessagesFailed.Add(1, new KeyValuePair<string, object?>("message_type", envelope.MessageType));
                _logger.LogError(ex, "Error processing message {MessageId}", envelope.MessageId);
                await args.AbandonMessageAsync(args.Message, cancellationToken: ct);
            }
        };

        processor.ProcessErrorAsync += args =>
        {
            _logger.LogError(args.Exception, "Service Bus error: {ErrorSource}", args.ErrorSource);
            return Task.CompletedTask;
        };

        await processor.StartProcessingAsync(ct);

        // Keep running until cancellation
        try
        {
            await Task.Delay(Timeout.Infinite, ct);
        }
        catch (TaskCanceledException)
        {
            await processor.StopProcessingAsync();
        }
    }

    private async Task ScheduleRetryAsync(ProcessMessageEventArgs args, IMessageEnvelope envelope, CancellationToken ct)
    {
        // Get current retry count
        var retryCount = 0;
        if (args.Message.ApplicationProperties.TryGetValue("x-retry-count", out var retryObj))
        {
            int.TryParse(retryObj?.ToString(), out retryCount);
        }

        // Determine delay based on retry count
        var delayIndex = Math.Min(retryCount, _options.RetryDelaysSeconds.Length - 1);
        var delaySeconds = _options.RetryDelaysSeconds[delayIndex];

        // Create new message with scheduled enqueue time
        var sender = _client.CreateSender(args.EntityPath);
        var newMessage = new ServiceBusMessage(args.Message.Body)
        {
            Subject = args.Message.Subject,
            MessageId = Guid.NewGuid().ToString(), // New ID to avoid duplicate detection
            CorrelationId = args.Message.CorrelationId,
            SessionId = args.Message.SessionId,
            ScheduledEnqueueTime = DateTimeOffset.UtcNow.AddSeconds(delaySeconds)
        };

        // Copy and update application properties
        foreach (var kv in args.Message.ApplicationProperties)
        {
            if (kv.Key != "x-retry-count")
                newMessage.ApplicationProperties[kv.Key] = kv.Value;
        }
        newMessage.ApplicationProperties["x-retry-count"] = retryCount + 1;

        await sender.SendMessageAsync(newMessage, ct);

        _logger.LogInformation("Message {MessageId} scheduled for retry in {DelaySeconds}s (attempt {RetryCount})",
            envelope.MessageId, delaySeconds, retryCount + 1);
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
