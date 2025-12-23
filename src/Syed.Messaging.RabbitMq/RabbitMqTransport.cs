using System.Diagnostics;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Polly;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Syed.Messaging;
using Syed.Messaging.Core;

namespace Syed.Messaging.RabbitMq;

public sealed class RabbitMqTransport : IMessageTransport, IDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqTransport> _logger;
    private readonly IConnection _connection;
    private readonly IModel _channel;
    private readonly ResiliencePipeline _resiliencePipeline;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<IMessageEnvelope>> _pendingRequests = new();

    public RabbitMqTransport(
        RabbitMqOptions options, 
        ILogger<RabbitMqTransport> logger,
        ResiliencePipeline? resiliencePipeline = null,
        IConnectionFactory? connectionFactory = null)
    {
        try
        {
            _options = options;
            _logger = logger;
            _resiliencePipeline = resiliencePipeline ?? ResiliencePipeline.Empty;

            var factory = connectionFactory ?? new ConnectionFactory
            {
                Uri = new Uri(options.ConnectionString),
                DispatchConsumersAsync = true
            };

            _connection = factory.CreateConnection();
            _channel = _connection.CreateModel();

            // Enable Publisher Confirms
            _channel.ConfirmSelect();

            // Setup Direct Reply-to Consumer for RPC
            var replyConsumer = new AsyncEventingBasicConsumer(_channel);
            replyConsumer.Received += (_, ea) =>
            {
                var correlationId = ea.BasicProperties.CorrelationId;
                if (!string.IsNullOrEmpty(correlationId) && _pendingRequests.TryRemove(correlationId, out var tcs))
                {
                    var envelope = ToEnvelope(ea);
                    tcs.TrySetResult(envelope);
                }
                return Task.CompletedTask;
            };

            _channel.BasicConsume(
                queue: "amq.rabbitmq.reply-to",
                autoAck: true,
                consumer: replyConsumer);

            var topology = new RabbitTopologyBuilder(_channel, _options);
            topology.Build();

            _channel.BasicQos(0, _options.PrefetchCount, false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Critical] Failed to initialize RabbitMqTransport. ConnectionStr: {ConnectionString}", options.ConnectionString);
            throw;
        }
    }

    public Task PublishAsync(IMessageEnvelope envelope, string destination, CancellationToken ct)
    {
        using var activity = MessagingDiagnostics.ActivitySource.StartActivity(
            MessagingDiagnostics.PublishActivityName,
            ActivityKind.Producer);

        if (activity is not null)
        {
            activity.SetTag("messaging.system", "rabbitmq");
            activity.SetTag("messaging.destination", destination);
            activity.SetTag("messaging.message_type", envelope.MessageType);
        }

        var props = _channel.CreateBasicProperties();
        props.Persistent = true;

        var headers = envelope.Headers.ToDictionary(kv => kv.Key, kv => (object)kv.Value);
        
        // Ensure message-type and message-version are in headers
        headers["message-type"] = envelope.MessageType;
        if (!string.IsNullOrEmpty(envelope.MessageVersion))
        {
            headers["message-version"] = envelope.MessageVersion;
        }
        headers["timestamp"] = envelope.Timestamp.ToUnixTimeMilliseconds().ToString();
        
        props.Headers = headers;
        props.CorrelationId = envelope.CorrelationId;
        props.Type = envelope.MessageType;
        props.MessageId = envelope.MessageId ?? Guid.NewGuid().ToString();
        props.Timestamp = new AmqpTimestamp(envelope.Timestamp.ToUnixTimeSeconds());
        
        // RPC support: set ReplyTo if specified
        if (!string.IsNullOrEmpty(envelope.ReplyTo))
        {
            props.ReplyTo = envelope.ReplyTo;
        }

        _channel.BasicPublish(
            exchange: _options.MainExchangeName,
            routingKey: destination,
            basicProperties: props,
            body: envelope.Body);

        // Wait for broker confirmation
        try
        {
            _channel.WaitForConfirmsOrDie(TimeSpan.FromSeconds(5));
            MessagingMetrics.MessagesPublished.Add(1, new KeyValuePair<string, object?>("message_type", envelope.MessageType));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish message {MessageId} to {Destination}. NACK or Timeout received.", envelope.MessageId, destination);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }

        return Task.CompletedTask;
    }

    public Task SendAsync(IMessageEnvelope envelope, string destination, CancellationToken ct)
        => PublishAsync(envelope, destination, ct);

    public async Task<IMessageEnvelope> RequestAsync(IMessageEnvelope envelope, string destination, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(envelope.CorrelationId))
        {
            throw new ArgumentException("CorrelationId is required for RequestAsync", nameof(envelope));
        }

        // Create envelope with ReplyTo set to Direct Reply-to queue
        var requestEnvelope = new MessageEnvelope
        {
            MessageType = envelope.MessageType,
            MessageVersion = envelope.MessageVersion,
            MessageId = envelope.MessageId,
            CorrelationId = envelope.CorrelationId,
            CausationId = envelope.CausationId,
            Headers = envelope.Headers,
            Body = envelope.Body,
            Timestamp = envelope.Timestamp,
            ReplyTo = "amq.rabbitmq.reply-to"
        };

        var tcs = new TaskCompletionSource<IMessageEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var reg = ct.Register(() =>
            tcs.TrySetCanceled(ct));

        _pendingRequests[envelope.CorrelationId] = tcs;

        try
        {
            await PublishAsync(requestEnvelope, destination, ct);
            return await tcs.Task;
        }
        finally
        {
            _pendingRequests.TryRemove(envelope.CorrelationId, out _);
        }
    }


    public Task SubscribeAsync(
        string subscriptionName,
        string destination,
        Func<IMessageEnvelope, CancellationToken, Task<TransportAcknowledge>> handler,
        CancellationToken ct)
    {
        var consumer = new AsyncEventingBasicConsumer(_channel);

        consumer.Received += async (_, ea) =>
        {
            var envelope = ToEnvelope(ea);
            TransportAcknowledge ackResult;

            try
            {
                // Execute handler within the resilience pipeline (e.g., retries)
                ackResult = await _resiliencePipeline.ExecuteAsync(async cancellationToken => 
                {
                    return await handler(envelope, cancellationToken);
                }, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in message handler (after retries); will NACK/Retry via Broker.");
                ackResult = TransportAcknowledge.Retry;
            }

            HandleAckResult(ea, ackResult);
        };

        _channel.BasicConsume(
            queue: _options.MainQueueName,
            autoAck: false,
            consumer: consumer);

        return Task.CompletedTask;
    }

    private MessageEnvelope ToEnvelope(BasicDeliverEventArgs ea)
    {
        var props = ea.BasicProperties;
        var headers = new Dictionary<string, string>();

        if (props.Headers != null)
        {
            foreach (var kv in props.Headers)
            {
                headers[kv.Key] = kv.Value switch
                {
                    byte[] bytes => System.Text.Encoding.UTF8.GetString(bytes),
                    string s => s,
                    _ => kv.Value.ToString() ?? string.Empty
                };
            }
        }

        var messageId = props.MessageId;
        var correlationId = props.CorrelationId ?? Activity.Current?.Id ?? Guid.NewGuid().ToString();

        if (!headers.ContainsKey("message-id") && !string.IsNullOrWhiteSpace(messageId))
        {
            headers["message-id"] = messageId;
        }

        if (!headers.ContainsKey("correlation-id") && !string.IsNullOrWhiteSpace(correlationId))
        {
            headers["correlation-id"] = correlationId;
        }

        // Extract message version from headers
        string? messageVersion = null;
        if (headers.TryGetValue("message-version", out var versionHeader))
        {
            messageVersion = versionHeader;
        }

        // Extract timestamp from AMQP or header
        DateTimeOffset timestamp = DateTimeOffset.UtcNow;
        if (props.Timestamp.UnixTime > 0)
        {
            timestamp = DateTimeOffset.FromUnixTimeSeconds(props.Timestamp.UnixTime);
        }
        else if (headers.TryGetValue("timestamp", out var tsHeader) && long.TryParse(tsHeader, out var tsMs))
        {
            timestamp = DateTimeOffset.FromUnixTimeMilliseconds(tsMs);
        }

        return new MessageEnvelope
        {
            MessageType = props.Type ?? string.Empty,
            MessageVersion = messageVersion,
            MessageId = messageId,
            CorrelationId = correlationId,
            CausationId = headers.TryGetValue("causation-id", out var causationId) ? causationId : null,
            Timestamp = timestamp,
            Headers = headers,
            Body = ea.Body.ToArray()
        };
    }

    private void HandleAckResult(BasicDeliverEventArgs ea, TransportAcknowledge result)
    {
        switch (result)
        {
            case TransportAcknowledge.Ack:
                _channel.BasicAck(ea.DeliveryTag, false);
                break;

            case TransportAcknowledge.Retry:
            {
                var props = _channel.CreateBasicProperties();
                props.Persistent = true;
                props.Headers = ea.BasicProperties.Headers ?? new Dictionary<string, object>();

                int retryCount = 0;
                if (props.Headers.TryGetValue("x-retry-count", out var raw))
                {
                    if (raw is byte[] bytes)
                    {
                        var s = System.Text.Encoding.UTF8.GetString(bytes);
                        int.TryParse(s, out retryCount);
                    }
                }

                retryCount++;
                props.Headers["x-retry-count"] = System.Text.Encoding.UTF8.GetBytes(retryCount.ToString());

                _channel.BasicPublish(
                    exchange: _options.RetryExchangeName,
                    routingKey: _options.RoutingKey,
                    basicProperties: props,
                    body: ea.Body);

                _channel.BasicAck(ea.DeliveryTag, false);
                break;
            }

            case TransportAcknowledge.DeadLetter:
            {
                var props = _channel.CreateBasicProperties();
                props.Persistent = true;
                props.Headers = ea.BasicProperties.Headers;

                _channel.BasicPublish(
                    exchange: _options.DeadLetterExchangeName,
                    routingKey: _options.RoutingKey,
                    basicProperties: props,
                    body: ea.Body);

                _channel.BasicAck(ea.DeliveryTag, false);
                break;
            }
        }
    }

    public void Dispose()
    {
        _channel.Dispose();
        _connection.Dispose();
    }
}
