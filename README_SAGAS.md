# Syed.Messaging.Sagas

This package adds **saga orchestration primitives** on top of Syed.Messaging:

- `ISagaState`, `ISagaHandler<TSagaState, TMessage>` (expected in Syed.Messaging.Abstractions)
- `ISagaStateStore<TSagaState>` + in-memory implementation
- `ISagaRegistry`, `SagaRegistryBuilder`, `SagaBuilder`
- `SagaRuntime` – loads state, invokes saga, saves state
- `SagaMessageHandler<T>` – adapts saga runtime to `IMessageHandler<T>`
- Timeout support:
  - `ISagaTimeoutStore` + in-memory implementation
  - `ISagaTimeoutScheduler`
  - `SagaTimeoutDispatcher` background service

## Example registration

```csharp
using Syed.Messaging;
using Syed.Messaging.RabbitMq;
using Syed.Messaging.Sagas;

builder.Services
    .AddMessaging(m =>
    {
        m.UseRabbitMq(r =>
        {
            r.ConnectionString = "amqp://guest:guest@localhost:5672/";
            r.MainExchangeName = "orders.exchange";
            r.MainQueueName = "orders.queue";
            r.RetryQueueName = "orders.retry.queue";
            r.DeadLetterQueueName = "orders.dlq.queue";
            r.RoutingKey = "orders.created";
        });

        // Wire saga message adapter for each message the saga handles
        m.AddConsumer<OrderCreated, SagaMessageHandler<OrderCreated>>(c =>
        {
            c.Destination = "orders.created";
            c.SubscriptionName = "orders-saga";
        });
    })
    .AddSagas(s =>
    {
        s.AddSaga<OrderSagaState, OrderSaga>(cfg =>
        {
            cfg.CorrelateOn<OrderCreated>(m => m.OrderId, startsNew: true);
            // Add more CorrelateOn calls for other messages
        });
    });
```

Use `ISagaTimeoutScheduler` in your saga to schedule timeouts:

```csharp
public class OrderSaga :
    ISagaHandler<OrderSagaState, OrderCreated>
{
    private readonly ISagaTimeoutScheduler _timeouts;

    public OrderSaga(ISagaTimeoutScheduler timeouts)
    {
        _timeouts = timeouts;
    }

    public async Task HandleAsync(OrderSagaState state, OrderCreated message, MessageContext ctx, CancellationToken ct)
    {
        state.Id = Guid.NewGuid();
        state.Version++;

        await _timeouts.ScheduleAsync(
            sagaType: typeof(OrderSaga),
            correlationKey: message.OrderId.ToString(),
            delay: TimeSpan.FromMinutes(30),
            timeout: new PaymentTimeout(message.OrderId),
            ct: ct);
    }
}
```

This saga package is intentionally **minimal** and uses **in-memory stores** by default to keep it lightweight.
You can add EF Core or Redis-backed implementations of `ISagaStateStore<T>` and `ISagaTimeoutStore` in your own codebase.
