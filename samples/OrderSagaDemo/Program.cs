using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Syed.Messaging;
using Syed.Messaging.RabbitMq;
using Syed.Messaging.Sagas;

var builder = Host.CreateApplicationBuilder(args);

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

var app = builder.Build();

// On startup: publish a single OrderCreated to kick off the saga
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Bootstrap");
    var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

    var orderId = Guid.NewGuid();
    logger.LogInformation("Publishing OrderCreated for OrderId={OrderId}", orderId);

    await bus.PublishAsync("orders.saga", new OrderCreated(orderId, 99.0m));
}

await app.RunAsync();
