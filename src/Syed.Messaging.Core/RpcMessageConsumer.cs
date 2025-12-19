using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace Syed.Messaging;

/// <summary>
/// Consumer for RPC-style messages that expects a response.
/// When a message with ReplyTo is received, the handler's response 
/// is serialized and sent back to the reply queue.
/// </summary>
public class RpcMessageConsumer<TRequest, TResponse> : BackgroundService
{
    private readonly IMessageTransport _transport;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISerializer _serializer;
    private readonly ConsumerOptions<TRequest> _options;
    private readonly ILogger<RpcMessageConsumer<TRequest, TResponse>> _logger;
    private readonly SemaphoreSlim _concurrencySemaphore;

    public RpcMessageConsumer(
        IMessageTransport transport,
        IServiceScopeFactory scopeFactory,
        ISerializer serializer,
        ConsumerOptions<TRequest> options,
        ILogger<RpcMessageConsumer<TRequest, TResponse>> logger)
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
                activity.SetTag("messaging.rpc", true);
            }

            var ctx = BuildContext(envelope);
            TRequest request;

            try
            {
                request = _serializer.Deserialize<TRequest>(envelope.Body);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deserialize RPC request {MessageType}, moving to DLQ", envelope.MessageType);
                return TransportAcknowledge.DeadLetter;
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var handler = scope.ServiceProvider.GetRequiredService<IRpcHandler<TRequest, TResponse>>();
                var response = await handler.HandleAsync(request, ctx, ct);

                // Send response back if ReplyTo is specified
                if (!string.IsNullOrEmpty(envelope.ReplyTo))
                {
                    var responseEnvelope = new MessageEnvelope
                    {
                        MessageType = typeof(TResponse).FullName ?? typeof(TResponse).Name,
                        MessageId = Guid.NewGuid().ToString(),
                        CorrelationId = envelope.CorrelationId,
                        Headers = new Dictionary<string, string>
                        {
                            ["correlation-id"] = envelope.CorrelationId ?? "",
                            ["message-id"] = Guid.NewGuid().ToString()
                        },
                        Body = _serializer.Serialize(response)
                    };

                    await _transport.PublishAsync(responseEnvelope, envelope.ReplyTo, ct);
                    _logger.LogDebug("Sent RPC response to {ReplyTo} with CorrelationId {CorrelationId}", 
                        envelope.ReplyTo, envelope.CorrelationId);
                }

                return TransportAcknowledge.Ack;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error handling RPC request {MessageType}. CorrelationId={CorrelationId}",
                    envelope.MessageType,
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
