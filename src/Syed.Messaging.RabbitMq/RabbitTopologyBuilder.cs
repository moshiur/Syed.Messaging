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

        var retryQ = _options.RetryQueueName;
        var dlqQ = _options.DeadLetterQueueName;

        _channel.ExchangeDeclare(main, ExchangeType.Direct, durable: true);
        _channel.ExchangeDeclare(retry, ExchangeType.Direct, durable: true);
        _channel.ExchangeDeclare(dlq, ExchangeType.Direct, durable: true);

        // DLQ — bindings added lazily per destination in SubscribeAsync
        _channel.QueueDeclare(dlqQ, durable: true, exclusive: false, autoDelete: false);

        // Retry queue — TTL + DLX back to main exchange.
        // No x-dead-letter-routing-key so RabbitMQ preserves the original routing key,
        // ensuring retried messages route back to the correct per-destination queue.
        var retryArgs = new Dictionary<string, object>
        {
            { "x-dead-letter-exchange", main },
            { "x-message-ttl", (int)_options.RetryDelay.TotalMilliseconds }
        };

        _channel.QueueDeclare(retryQ, durable: true, exclusive: false, autoDelete: false, arguments: retryArgs);

        // Note: Per-destination queues and their bindings to main/retry/dlq exchanges
        // are declared lazily in RabbitMqTransport.SubscribeAsync().
    }
}
