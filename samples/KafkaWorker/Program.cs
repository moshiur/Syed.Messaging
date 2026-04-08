using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Syed.Messaging;
using Syed.Messaging.Kafka;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddMessaging(m =>
{
    m.UseKafka(kafka =>
    {
        kafka.BootstrapServers = "localhost:9092";
        kafka.ConsumerGroupId = "order-worker";
        kafka.TopicPrefix = "orders.";
        kafka.Consumer.MaxConcurrentPartitions = 4;
        kafka.Consumer.LogRebalanceEvents = true;
    });

    m.AddConsumer<OrderCreated, OrderCreatedHandler>(c =>
    {
        c.Destination = "orders.created";
        c.SubscriptionName = "order-worker";
        c.MaxConcurrency = 4;
        c.RetryPolicy = new RetryPolicy { MaxRetries = 3 };
    });
});

var app = builder.Build();

// Example: fire a test message once on startup
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Bootstrap");
    logger.LogInformation("Publishing Kafka startup events with partition keys...");
    var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

    // Same customer uses same partition-key, so ordering for that customer is preserved.
    await bus.PublishRawAsync(
        "orders.created",
        JsonSerializer.SerializeToUtf8Bytes(new OrderCreated(Guid.NewGuid(), "customer-456")),
        nameof(OrderCreated),
        new Dictionary<string, string> { ["partition-key"] = "customer-456" });

    await bus.PublishRawAsync(
        "orders.created",
        JsonSerializer.SerializeToUtf8Bytes(new OrderCreated(Guid.NewGuid(), "customer-789")),
        nameof(OrderCreated),
        new Dictionary<string, string> { ["partition-key"] = "customer-789" });
}

await app.RunAsync();
