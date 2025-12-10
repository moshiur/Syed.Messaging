using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Syed.Messaging;
using Syed.Messaging.RabbitMq;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddMessaging(m =>
{
    m.UseRabbitMq(rabbit =>
    {
        rabbit.ConnectionString = "amqp://guest:guest@localhost:5672/";
        rabbit.MainExchangeName = "orders.exchange";
        rabbit.MainQueueName = "orders.queue";
        rabbit.RetryQueueName = "orders.retry.queue";
        rabbit.DeadLetterQueueName = "orders.dlq.queue";
        rabbit.RoutingKey = "orders.created";
    });

    m.AddConsumer<OrderCreated, OrderCreatedHandler>(c =>
    {
        c.Destination = "orders.created";
        c.SubscriptionName = "orders-worker";
        c.MaxConcurrency = 4;
        c.RetryPolicy = new RetryPolicy { MaxRetries = 5 };
    });
});

var app = builder.Build();

// Example: fire a test message once on startup
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Bootstrap");
    logger.LogInformation("Publishing a test OrderCreated event...");
    var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
    await bus.PublishAsync("orders.created", new OrderCreated(Guid.NewGuid(), "customer-123"));
}

await app.RunAsync();
