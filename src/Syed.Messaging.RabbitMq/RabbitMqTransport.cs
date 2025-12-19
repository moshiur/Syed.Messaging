using System.Diagnostics;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Syed.Messaging;
using System.Collections.Concurrent;

namespace Syed.Messaging.RabbitMq;

public sealed class RabbitMqTransport : IMessageTransport, IDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqTransport> _logger;
    private readonly IConnection _connection;
    private readonly IModel _channel;
    
    private readonly ConcurrentDictionary<string, TaskCompletionSource<IMessageEnvelope>> _pendingRequests = new();

    public RabbitMqTransport(RabbitMqOptions options, ILogger<RabbitMqTransport> logger)
    {
        try
        {
            _options = options;
            _logger = logger;

            var factory = new ConnectionFactory
            {
                Uri = new Uri(options.ConnectionString),
                DispatchConsumersAsync = true
            };

            _connection = factory.CreateConnection();
            _channel = _connection.CreateModel();

            // Setup Direct Reply-to Consumer
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

        props.Headers = envelope.Headers.ToDictionary(kv => kv.Key, kv => (object)kv.Value);
        props.CorrelationId = envelope.CorrelationId;
        props.Type = envelope.MessageType;
        props.MessageId = envelope.MessageId ?? Guid.NewGuid().ToString();
        
        if (!string.IsNullOrEmpty(envelope.ReplyTo))
        {
            props.ReplyTo = envelope.ReplyTo;
        }

        _channel.BasicPublish(
            exchange: _options.MainExchangeName,
            routingKey: destination,
            basicProperties: props,
            body: envelope.Body);

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

        // Use Direct Reply-to
        // Note: The ReplyTo property on the envelope object passed in is ignored/overwritten because
        // we must use the specific address for this transport's reply mechanism.
        // Actually, we should set it on the envelope that goes to PublishAsync?
        // But PublishAsync takes the envelope property.
        // Let's modify the envelope passing through.
        // Since IMessageEnvelope is immutable-ish (properties are init), we might need to cast or rely on properties map.
        // But we added ReplyTo to the interface.
        
        // However, we can't mutate the input envelope easily.
        // We'll pass the ReplyTo via `PublishAsync` logic modification above.
        // Wait, I updated PublishAsync to read envelope.ReplyTo.
        // But I need to set it to "amq.rabbitmq.reply-to".
        
        // We need to create a new envelope or modify property logic?
        // MessageEnvelope is a class with init properties.
        var requestEnvelope = new MessageEnvelope
        {
            MessageType = envelope.MessageType,
            MessageId = envelope.MessageId,
            CorrelationId = envelope.CorrelationId,
            CausationId = envelope.CausationId,
            Headers = envelope.Headers,
            Body = envelope.Body,
            ReplyTo = "amq.rabbitmq.reply-to" 
        };

        var tcs = new TaskCompletionSource<IMessageEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        
        // Register cancellation
        using var reg = ct.Register(() => 
        {
            if (_pendingRequests.TryRemove(envelope.CorrelationId, out var removedTcs))
            {
                removedTcs.TrySetCanceled();
            }
        });

        if (!_pendingRequests.TryAdd(envelope.CorrelationId, tcs))
        {
             throw new InvalidOperationException($"Duplicate request with correlation ID {envelope.CorrelationId}");
        }

        try
        {
            await PublishAsync(requestEnvelope, destination, ct);
            return await tcs.Task;
        }
        catch
        {
            _pendingRequests.TryRemove(envelope.CorrelationId, out _);
            throw;
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
                ackResult = await handler(envelope, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in message handler; will retry.");
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

        return new MessageEnvelope
        {
            MessageType = props.Type ?? string.Empty,
            MessageId = messageId,
            CorrelationId = correlationId,
            CausationId = null,
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
