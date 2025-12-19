using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OrderSagaDemo;
using Syed.Messaging;
using Syed.Messaging.RabbitMq;
using Syed.Messaging.Sagas;
using Syed.Messaging.Sagas.EfCore;
using Syed.Messaging.Core;

using Syed.Messaging.Inbox.EfCore;

var builder = Host.CreateApplicationBuilder(args);

// Add SQLite DbContext for saga persistence
builder.Services.AddDbContext<SagaDbContext>(options =>
    options.UseSqlite("Data Source=sagas.db"));

// Add resilience pipeline (Polly)
builder.Services.AddMessageResilience();

builder.Services
    .AddMessaging(m =>
    {
        m.UseRabbitMq(r =>
        {
            r.ConnectionString = "amqp://guest:guest@localhost:5672/";
            r.MainExchangeName = "orders.exchange";
            r.MainQueueName = "orders.saga.queue";
            r.RetryQueueName = "orders.saga.retry.queue";
            r.DeadLetterQueueName = "orders.saga.dlq.queue";
            r.RoutingKey = "orders.saga";
        });

        // Wire saga message adapter for all message types handled by the saga
        m.AddConsumer<OrderCreated, SagaMessageHandler<OrderCreated>>(c =>
        {
            c.Destination = "orders.saga";
            c.SubscriptionName = "orders-saga-ordercreated";
        });

        m.AddConsumer<PaymentCompleted, SagaMessageHandler<PaymentCompleted>>(c =>
        {
            c.Destination = "orders.saga";
            c.SubscriptionName = "orders-saga-paymentcompleted";
        });

        m.AddConsumer<PaymentTimeout, SagaMessageHandler<PaymentTimeout>>(c =>
        {
            c.Destination = "orders.saga";
            c.SubscriptionName = "orders-saga-paymenttimeout";
        });
    })
    .AddSagas(s =>
    {
        s.AddSaga<OrderSagaState, OrderSaga>(cfg =>
        {
            cfg.CorrelateOn<OrderCreated>(m => m.OrderId, startsNew: true);
            cfg.CorrelateOn<PaymentCompleted>(m => m.OrderId);
            cfg.CorrelateOn<PaymentTimeout>(m => m.OrderId);
        });
    });

// Register EF Core saga stores
builder.Services.AddEfSagaStateStore<SagaDbContext, OrderSagaState>();
builder.Services.AddEfSagaTimeoutStore<SagaDbContext>();
builder.Services.AddEfInboxStore<SagaDbContext>();

var app = builder.Build();

// Ensure database is created
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SagaDbContext>();
    await db.Database.EnsureCreatedAsync();
    
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Bootstrap");
    logger.LogInformation("✅ Saga database initialized (SQLite: sagas.db)");
    
    var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
    var orderId = Guid.NewGuid();
    logger.LogInformation("Publishing OrderCreated for OrderId={OrderId}", orderId);

    await bus.PublishAsync("orders.saga", new OrderCreated(orderId, 99.0m));
}

await app.RunAsync();

