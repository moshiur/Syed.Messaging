using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace Syed.Messaging;

public class GenericMessageConsumer<TMessage> : BackgroundService
{
    private readonly IMessageTransport _transport;
    private readonly IServiceScopeFactory _scopeFactory;

    // Handler is resolved per scope
    private readonly ISerializer _serializer;
    private readonly ConsumerOptions<TMessage> _options;
    private readonly ILogger<GenericMessageConsumer<TMessage>> _logger;
    private readonly SemaphoreSlim _concurrencySemaphore;

    public GenericMessageConsumer(
        IMessageTransport transport,
        IServiceScopeFactory scopeFactory,
        ISerializer serializer,
        ConsumerOptions<TMessage> options,
        ILogger<GenericMessageConsumer<TMessage>> logger)
    {
        _transport = transport;
        _scopeFactory = scopeFactory;
        _serializer = serializer;
        _options = options;
        _logger = logger;
        _concurrencySemaphore = new SemaphoreSlim(Math.Max(1, options.MaxConcurrency));
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return _transport.SubscribeAsync(
            subscriptionName: _options.SubscriptionName,
            destination: _options.Destination,
            handler: HandleEnvelopeAsync,
            ct: stoppingToken);
    }

    private async Task<TransportAcknowledge> HandleEnvelopeAsync(IMessageEnvelope envelope, CancellationToken ct)
    {
        await _concurrencySemaphore.WaitAsync(ct);
        try
        {
            using var activity = MessagingDiagnostics.ActivitySource.StartActivity(
                MessagingDiagnostics.ConsumeActivityName,
                ActivityKind.Consumer);

            if (activity is not null)
            {
                activity.SetTag("messaging.message_type", envelope.MessageType);
                activity.SetTag("messaging.message_id", envelope.MessageId);
                activity.SetTag("messaging.destination", _options.Destination);
            }

            var ctx = BuildContext(envelope);
            TMessage message;

            try
            {
                message = _serializer.Deserialize<TMessage>(envelope.Body);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deserialize message {MessageType}, moving to DLQ", envelope.MessageType);
                return TransportAcknowledge.DeadLetter;
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                
                // --- INBOX LOGIC BEGIN ---
                // We resolve the inbox store from the scope. If available, we check if processed.
                // Ideally this participates in the same unit of work / transaction as the handler.
                var inboxStore = scope.ServiceProvider.GetService<IInboxStore>();
                if (inboxStore != null)
                {
                    // If message ID is missing, we can't dedup reliably, but let's assume it exists or fallback.
                    var msgId = envelope.MessageId; 
                    if (!string.IsNullOrEmpty(msgId) && await inboxStore.HasBeenProcessedAsync(msgId, ct))
                    {
                        _logger.LogInformation("Message {MessageId} ({MessageType}) already processed. Skipping.", msgId, envelope.MessageType);
                        return TransportAcknowledge.Ack;
                    }
                }
                // --- INBOX LOGIC END ---

                var handler = scope.ServiceProvider.GetRequiredService<IMessageHandler<TMessage>>();
                await handler.HandleAsync(message, ctx, ct);

                // --- INBOX MARK PROCESSED ---
                if (inboxStore != null && !string.IsNullOrEmpty(envelope.MessageId))
                {
                    // This call should ideally be part of the handler's transaction commit if possible.
                    // If running against EF Core with same DbContext, it will attach the entity.
                    // However, if the handler already called SaveChanges(), we might need another one here.
                    // EfInboxStore executes SaveChangesAsync() internally in our implementation.
                    try
                    {
                        await inboxStore.MarkProcessedAsync(envelope.MessageId, expiration: null, ct: ct);
                    }
                    catch (Exception ex)
                    {
                         // If marking fails, it might be a race condition (processed by another thread?) or DB error.
                         // If DB error, we might want to fail the whole thing to retry.
                         // But if handler succeeded and this failed, we risk re-processing.
                         // Standard robust pattern: All in one transaction.
                         // For now, allow retry.
                         _logger.LogWarning(ex, "Failed to mark message {MessageId} as processed in Inbox.", envelope.MessageId);
                         throw; 
                    }
                }

                return TransportAcknowledge.Ack;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error handling message {MessageType} in {HandlerType}. RetryCount={RetryCount}, CorrelationId={CorrelationId}",
                    envelope.MessageType,
                    typeof(IMessageHandler<TMessage>).Name,
                    ctx.RetryCount,
                    ctx.CorrelationId);

                if (ctx.RetryCount >= _options.RetryPolicy.MaxRetries)
                {
                    return TransportAcknowledge.DeadLetter;
                }

                return TransportAcknowledge.Retry;
            }
        }
        finally
        {
            _concurrencySemaphore.Release();
        }
    }

    private MessageContext BuildContext(IMessageEnvelope envelope)
    {
        var headers = envelope.Headers;
        headers.TryGetValue("x-retry-count", out var retryStr);

        int retryCount = 0;
        if (!string.IsNullOrWhiteSpace(retryStr) && int.TryParse(retryStr, out var parsed))
        {
            retryCount = parsed;
        }

        var messageId = envelope.MessageId;
        var correlationId = envelope.CorrelationId;

        if (string.IsNullOrWhiteSpace(messageId) && headers.TryGetValue("message-id", out var idHeader))
        {
            messageId = idHeader;
        }

        if (string.IsNullOrWhiteSpace(correlationId) && headers.TryGetValue("correlation-id", out var corrHeader))
        {
            correlationId = corrHeader;
        }

        return new MessageContext
        {
            MessageId = string.IsNullOrWhiteSpace(messageId) ? Guid.NewGuid().ToString() : messageId!,
            CorrelationId = correlationId,
            RetryCount = retryCount,
            Headers = headers
        };
    }
}
