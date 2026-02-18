using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Syed.Messaging;
using Syed.Messaging.AzureServiceBus;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddMessaging(m =>
{
    m.UseAzureServiceBus(asb =>
    {
        // Replace with your Azure Service Bus connection string
        asb.ConnectionString = "Endpoint=sb://your-namespace.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=YOUR_KEY";
        asb.QueueOrTopicName = "orders";
        asb.SubscriptionName = "order-worker";
        asb.MaxConcurrentCalls = 8;
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
    logger.LogInformation("Publishing a test OrderCreated event via Azure Service Bus...");
    var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
    await bus.PublishAsync("orders.created", new OrderCreated(Guid.NewGuid(), "customer-789"));
}

await app.RunAsync();
