namespace Syed.Messaging.RabbitMq;

public sealed class RabbitMqOptions
{
    public string ConnectionString { get; set; } = string.Empty;

    public string MainExchangeName { get; set; } = "app.main.exchange";
    public string RetryExchangeName { get; set; } = "app.retry.exchange";
    public string DeadLetterExchangeName { get; set; } = "app.dlq.exchange";

    public string MainQueueName { get; set; } = "app.main.queue";
    public string RetryQueueName { get; set; } = "app.retry.queue";
    public string DeadLetterQueueName { get; set; } = "app.dlq.queue";

    public string RoutingKey { get; set; } = "app.message";

    public ushort PrefetchCount { get; set; } = 10;

    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(10);
}
