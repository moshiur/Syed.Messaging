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
    logger.LogInformation("Publishing a test OrderCreated event via Kafka...");
    var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
    await bus.PublishAsync("orders.created", new OrderCreated(Guid.NewGuid(), "customer-456"));
}

await app.RunAsync();
