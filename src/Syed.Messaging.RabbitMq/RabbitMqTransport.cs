using System.Diagnostics;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Syed.Messaging;

namespace Syed.Messaging.RabbitMq;

public sealed class RabbitMqTransport : IMessageTransport, IDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqTransport> _logger;
    private readonly IConnection _connection;
    private readonly IModel _channel;

    public RabbitMqTransport(RabbitMqOptions options, ILogger<RabbitMqTransport> logger)
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

        var topology = new RabbitTopologyBuilder(_channel, _options);
        topology.Build();

        _channel.BasicQos(0, _options.PrefetchCount, false);
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

        _channel.BasicPublish(
            exchange: _options.MainExchangeName,
            routingKey: destination,
            basicProperties: props,
            body: envelope.Body);

        return Task.CompletedTask;
    }

    public Task SendAsync(IMessageEnvelope envelope, string destination, CancellationToken ct)
        => PublishAsync(envelope, destination, ct);

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
