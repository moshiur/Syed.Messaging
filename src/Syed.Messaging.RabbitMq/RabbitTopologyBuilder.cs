using RabbitMQ.Client;

namespace Syed.Messaging.RabbitMq;

public sealed class RabbitTopologyBuilder
{
    private readonly IModel _channel;
    private readonly RabbitMqOptions _options;

    public RabbitTopologyBuilder(IModel channel, RabbitMqOptions options)
    {
        _channel = channel;
        _options = options;
    }

    public void Build()
    {
        var main = _options.MainExchangeName;
        var retry = _options.RetryExchangeName;
        var dlq = _options.DeadLetterExchangeName;

        var mainQ = _options.MainQueueName;
        var retryQ = _options.RetryQueueName;
        var dlqQ = _options.DeadLetterQueueName;
        var routingKey = _options.RoutingKey;

        _channel.ExchangeDeclare(main, ExchangeType.Direct, durable: true);
        _channel.ExchangeDeclare(retry, ExchangeType.Direct, durable: true);
        _channel.ExchangeDeclare(dlq, ExchangeType.Direct, durable: true);

        // DLQ
        _channel.QueueDeclare(dlqQ, durable: true, exclusive: false, autoDelete: false);
        _channel.QueueBind(dlqQ, dlq, routingKey);

        // Retry queue (TTL + DLX back to main)
        var retryArgs = new Dictionary<string, object>
        {
            { "x-dead-letter-exchange", main },
            { "x-dead-letter-routing-key", routingKey },
            { "x-message-ttl", (int)_options.RetryDelay.TotalMilliseconds }
        };

        _channel.QueueDeclare(retryQ, durable: true, exclusive: false, autoDelete: false, arguments: retryArgs);
        _channel.QueueBind(retryQ, retry, routingKey);

        // Main queue (DLX to retry)
        var mainArgs = new Dictionary<string, object>
        {
            { "x-dead-letter-exchange", retry },
            { "x-dead-letter-routing-key", routingKey }
        };

        _channel.QueueDeclare(mainQ, durable: true, exclusive: false, autoDelete: false, arguments: mainArgs);
        _channel.QueueBind(mainQ, main, routingKey);
    }
}
