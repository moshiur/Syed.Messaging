# Migrating from MassTransit to Syed.Messaging

## TL;DR

This is for senior .NET engineers running MassTransit v8 in production who want to get ahead of the **v9 commercial cutover** (v9 ships under Massient, a new commercial entity; v8 remains the free Apache-2.0 LTS for now). Moving before the cutover is forced gives you a permanently MIT-licensed alternative on your own schedule, instead of negotiating a commercial agreement when v8 stops getting updates.

Syed.Messaging is MIT-licensed, transport-agnostic across RabbitMQ / Kafka / Azure Service Bus, and ships with retry, DLQ, outbox, inbox, sagas, OTel tracing, and Prometheus metrics in the box. A trivial pubsub service typically moves in 30 minutes. A service with retry policies and middleware moves in 1-2 hours. A heavy Automatonymous saga is the real cost: budget 1-3 days per saga, more if your state machine has a dozen states and uses MassTransit's full DSL.

> **License status, accurately:** MassTransit v8 is Apache 2.0 ([github.com/MassTransit/MassTransit/blob/develop/LICENSE](https://github.com/MassTransit/MassTransit/blob/develop/LICENSE) — "MassTransit is Apache 2.0 licensed"). The maintainers have announced that v9 ships commercially under Massient. This guide treats that v9 cutover — not v8 today — as the reason to plan an exit path.

## What you're losing

Be honest with yourself before you start. Syed.Messaging at v1.2.0 does not match MassTransit feature-for-feature. The gaps that matter most:

- **No in-memory transport for tests.** MassTransit's `UsingInMemory` is a popular tool for integration testing without a broker. Syed.Messaging tests against real RabbitMQ / Kafka / ASB or stubs `IMessageBus` and `IMessageTransport` directly. Unit tests on handlers are trivial. End-to-end tests need Testcontainers or a local broker.
- **Fewer transport options.** RabbitMQ, Kafka, and Azure Service Bus only. No SQS, no ActiveMQ, no gRPC transport.
- **No Automatonymous-style state machine DSL.** Syed.Messaging sagas are plain C# classes implementing `ISagaHandler<TState, TMessage>`. No `Initially`, no `During`, no `When(Event).TransitionTo(State)`. State transitions are conditionals in your handler code. Some teams find this clearer; teams with large existing state machines will need to translate.
- **No request/response over multi-message conversations.** RPC exists (`IRpcHandler<TReq, TRes>`), but the conversation modelling MassTransit has around `IRequestClient<T>` with timeouts and multi-response handling is thinner here.
- **No batch consumers.** MassTransit's `IConsumer<Batch<T>>` has no direct equivalent.
- **No scheduled message API on the bus.** Scheduling is exposed only via `ISagaTimeoutScheduler`. ASB delayed messages are used internally for retry, not as a general "send this in 4 hours" API.
- **No conventional routing.** You set `Destination` and `SubscriptionName` explicitly per consumer. More typing, less magic.
- **Smaller community.** v1.x, pre-discovery. You will be reading source code occasionally.

If any of those are load-bearing for your team, weigh them honestly before you migrate.

## What you're gaining

- **MIT license.** No commercial tier, no per-seat fees, no licensing risk in the procurement review.
- **Smaller dependency surface.** `Syed.Messaging.Abstractions` has zero dependencies. Transports and capability packages depend only on what they need. No GreenPipes runtime to reason about.
- **One narrow API across three transports.** `IMessageBus.PublishAsync`, `IMessageHandler<T>`, `IMessageMiddleware`, and you're 80% of the way there.
- **Production patterns in the box.** Outbox, inbox, sagas with state and timeouts, retry with backoff, DLQ with diagnostic headers, OTel spans on `Syed.Messaging`, Prometheus metrics, KEDA / HPA wiring docs.
- **Per-destination queues on RabbitMQ.** No shared poison queue across consumers; each `AddConsumer<T>` declares its own queue bound by routing key (v1.2.0).
- **Source-linked symbols.** Step into the framework with the debugger when something behaves oddly.

## Side-by-side: consumer registration

### MassTransit

```csharp
public class OrderCreatedConsumer : IConsumer<OrderCreated>
{
    public Task Consume(ConsumeContext<OrderCreated> context)
    {
        return Process(context.Message, context.CancellationToken);
    }
}

services.AddMassTransit(x =>
{
    x.AddConsumer<OrderCreatedConsumer>();

    x.UsingRabbitMq((ctx, cfg) =>
    {
        cfg.Host("amqp://guest:guest@localhost:5672");
        cfg.ReceiveEndpoint("orders.created", e =>
        {
            e.ConfigureConsumer<OrderCreatedConsumer>(ctx);
            e.PrefetchCount = 10;
            e.ConcurrentMessageLimit = 4;
        });
    });
});
```

### Syed.Messaging

```csharp
[MessageType("orders.created")]
public record OrderCreated(Guid OrderId, string CustomerId);

public class OrderCreatedHandler : IMessageHandler<OrderCreated>
{
    public Task HandleAsync(OrderCreated message, MessageContext ctx, CancellationToken ct)
        => Process(message, ct);
}

services.AddMessaging(m =>
{
    m.UseRabbitMq(o =>
    {
        o.ConnectionString = "amqp://guest:guest@localhost:5672/";
        o.MainExchangeName = "orders.exchange";
        o.PrefetchCount = 10;
    });

    m.AddConsumer<OrderCreated, OrderCreatedHandler>(c =>
    {
        c.Destination = "orders.created";
        c.SubscriptionName = "orders-worker";
        c.MaxConcurrency = 4;
    });
});
```

A few notes. `IConsumer<T>.Consume` takes a `ConsumeContext<T>` that holds both the message and the cancellation token. `IMessageHandler<T>.HandleAsync` takes them as separate parameters plus a `MessageContext` for headers, message id, correlation id, and retry count. `[MessageType]` is the recommended way to give a message a stable wire identifier (see [MessageTypeAttribute.cs](../src/Syed.Messaging.Core/MessageTypeAttribute.cs)).

## Side-by-side: publishing

### MassTransit

```csharp
public class OrderService
{
    private readonly IPublishEndpoint _publish;
    public OrderService(IPublishEndpoint publish) => _publish = publish;

    public Task Place(Order o) =>
        _publish.Publish(new OrderCreated(o.Id, o.CustomerId));
}
```

### Syed.Messaging

```csharp
public class OrderService
{
    private readonly IMessageBus _bus;
    public OrderService(IMessageBus bus) => _bus = bus;

    public Task Place(Order o) =>
        _bus.PublishAsync("orders.created", new OrderCreated(o.Id, o.CustomerId));
}
```

The difference: MassTransit infers the destination from message type + endpoint conventions. Syed.Messaging takes the destination as an explicit string. That's deliberate. The destination is the routing key on RabbitMQ, the topic on Kafka, and the queue / topic name on Azure Service Bus. Making it explicit at the call site keeps the wire shape obvious. See [IMessageBus.cs](../src/Syed.Messaging.Abstractions/IMessageBus.cs).

For Kafka, set a `partition-key` header via `PublishRawAsync` to preserve per-aggregate ordering. README has the full example.

## Side-by-side: retry + DLQ

### MassTransit

```csharp
cfg.ReceiveEndpoint("orders.created", e =>
{
    e.UseMessageRetry(r => r.Exponential(
        retryLimit: 5,
        minInterval: TimeSpan.FromSeconds(5),
        maxInterval: TimeSpan.FromMinutes(1),
        intervalDelta: TimeSpan.FromSeconds(5)));

    e.UseDelayedRedelivery(r => r.Intervals(
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(30)));

    e.ConfigureConsumer<OrderCreatedConsumer>(ctx);
});
```

Failures past the policy land in `orders.created_error`.

### Syed.Messaging

```csharp
m.AddConsumer<OrderCreated, OrderCreatedHandler>(c =>
{
    c.Destination = "orders.created";
    c.SubscriptionName = "orders-worker";
    c.RetryPolicy = new RetryPolicy
    {
        MaxRetries = 5,
        InitialDelay = TimeSpan.FromSeconds(5),
        Backoff = RetryBackoff.Exponential
    };
});
```

Three backoff strategies: `Fixed`, `Linear`, `Exponential`. See [RetryPolicy.cs](../src/Syed.Messaging.Core/RetryPolicy.cs). On RabbitMQ, the retry queue uses TTL + DLX back to the main exchange and preserves the original routing key. On Kafka, retries land on per-delay topics. On Azure Service Bus, retries use `ScheduledEnqueueTime` on a republished message.

After the retry budget exhausts, the message goes to the DLQ with `x-poison-*` diagnostic headers (RabbitMQ) or equivalent metadata. Metrics fire on `messaging.messages.retried`, `messaging.messages.deadlettered`, and `messaging.messages.poisoned` with a `reason` tag (`max_retry_exhausted`, `handler_exception`, `deserialization_failure`). See [MessagingMetrics.cs](../src/Syed.Messaging.Core/MessagingMetrics.cs).

MassTransit's two-tier model (immediate retry inside the handler, then delayed redelivery) maps to a single policy here. If you need a more sophisticated schedule, fan out into a Polly pipeline via `AddMessageResilience()` (the [OrderSagaDemo](../samples/OrderSagaDemo/Program.cs) sample shows this).

## Side-by-side: sagas / state machines

This is the section where the porting cost is real. MassTransit sagas are typically Automatonymous state machines:

```csharp
public class OrderState : SagaStateMachineInstance
{
    public Guid CorrelationId { get; set; }
    public string CurrentState { get; set; }
    public Guid OrderId { get; set; }
}

public class OrderStateMachine : MassTransitStateMachine<OrderState>
{
    public State AwaitingPayment { get; private set; }
    public State Completed { get; private set; }

    public Event<OrderCreated> OrderCreatedEvt { get; private set; }
    public Event<PaymentCompleted> PaymentCompletedEvt { get; private set; }
    public Schedule<OrderState, PaymentTimeout> PaymentTimeoutSchedule { get; private set; }

    public OrderStateMachine()
    {
        InstanceState(x => x.CurrentState);
        Event(() => OrderCreatedEvt, x => x.CorrelateById(c => c.Message.OrderId));
        Event(() => PaymentCompletedEvt, x => x.CorrelateById(c => c.Message.OrderId));
        Schedule(() => PaymentTimeoutSchedule, x => x.PaymentTimeoutId,
            s => s.Delay = TimeSpan.FromMinutes(30));

        Initially(
            When(OrderCreatedEvt)
                .Then(ctx => ctx.Saga.OrderId = ctx.Message.OrderId)
                .Schedule(PaymentTimeoutSchedule, ctx => new PaymentTimeout(ctx.Saga.OrderId))
                .TransitionTo(AwaitingPayment));

        During(AwaitingPayment,
            When(PaymentCompletedEvt)
                .Unschedule(PaymentTimeoutSchedule)
                .TransitionTo(Completed));
    }
}
```

In Syed.Messaging, the same workflow is a plain class plus a correlation registration. The state machine logic lives in the handler:

```csharp
public class OrderSagaState : ISagaState
{
    public Guid Id { get; set; }
    public int Version { get; set; }
    public Guid OrderId { get; set; }
    public string CurrentState { get; set; } = "AwaitingPayment";
}

public class OrderSaga :
    ISagaHandler<OrderSagaState, OrderCreated>,
    ISagaHandler<OrderSagaState, PaymentCompleted>,
    ISagaHandler<OrderSagaState, PaymentTimeout>
{
    private readonly ISagaTimeoutScheduler _timeouts;

    public OrderSaga(ISagaTimeoutScheduler timeouts) => _timeouts = timeouts;

    public async Task HandleAsync(OrderSagaState state, OrderCreated msg, MessageContext ctx, CancellationToken ct)
    {
        state.OrderId = msg.OrderId;
        state.CurrentState = "AwaitingPayment";
        await _timeouts.ScheduleAsync(
            typeof(OrderSaga),
            msg.OrderId.ToString(),
            TimeSpan.FromMinutes(30),
            new PaymentTimeout(msg.OrderId),
            ct);
    }

    public Task HandleAsync(OrderSagaState state, PaymentCompleted msg, MessageContext ctx, CancellationToken ct)
    {
        if (state.CurrentState != "AwaitingPayment") return Task.CompletedTask;
        state.CurrentState = "Completed";
        return _timeouts.CancelAsync<PaymentTimeout>(typeof(OrderSaga), msg.OrderId.ToString(), ct);
    }

    public Task HandleAsync(OrderSagaState state, PaymentTimeout msg, MessageContext ctx, CancellationToken ct)
    {
        if (state.CurrentState != "AwaitingPayment") return Task.CompletedTask;
        state.CurrentState = "TimedOut";
        return Task.CompletedTask;
    }
}
```

Wiring:

```csharp
services
    .AddMessaging(m =>
    {
        m.UseRabbitMq(/* ... */);
        m.AddConsumer<OrderCreated, SagaMessageHandler<OrderCreated>>(c =>
            { c.Destination = "orders.saga"; c.SubscriptionName = "orders-saga-created"; });
        m.AddConsumer<PaymentCompleted, SagaMessageHandler<PaymentCompleted>>(c =>
            { c.Destination = "orders.saga"; c.SubscriptionName = "orders-saga-paid"; });
        m.AddConsumer<PaymentTimeout, SagaMessageHandler<PaymentTimeout>>(c =>
            { c.Destination = "orders.saga"; c.SubscriptionName = "orders-saga-timeout"; });
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

services.AddEfSagaStateStore<MyDbContext, OrderSagaState>();
services.AddEfSagaTimeoutStore<MyDbContext>();
```

The EF stores read from your `DbContext`, but you also need to register the entity model. In your `DbContext.OnModelCreating`:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);
    modelBuilder.ConfigureSagaEntities(); // adds SagaStates + SagaTimeouts tables
}
```

`ConfigureSagaEntities()` lives in `Syed.Messaging.Sagas.EfCore` ([SagaEfCoreServiceCollectionExtensions.cs](../src/Syed.Messaging.Sagas.EfCore/SagaEfCoreServiceCollectionExtensions.cs)). Run `dotnet ef migrations add AddSagaTables` after wiring it, then `dotnet ef database update`.

What changes in your head:

- The state machine is implicit in your handler's branching, not declared up front. You write `if (state.CurrentState != "AwaitingPayment") return;` instead of `During(AwaitingPayment, ...)`. Some teams hate this. Some teams find it more debuggable.
- Correlation is per-message, declared in `AddSaga`. `startsNew: true` means a message of that type can create a new saga instance.
- Timeouts go through `ISagaTimeoutScheduler.ScheduleAsync` and `CancelAsync` (see [SagaTimeouts.cs](../src/Syed.Messaging.Sagas/SagaTimeouts.cs)). The `SagaTimeoutDispatcher` background service polls due timeouts every 10 seconds by default.
- Optimistic concurrency on the saga state uses the `Version` field. Locking is per-instance, pluggable: in-memory, no-op, or Redis (`Syed.Messaging.Sagas.Redis`).

The full working example is in [samples/OrderSagaDemo/Program.cs](../samples/OrderSagaDemo/Program.cs).

## Side-by-side: outbox

### MassTransit

```csharp
services.AddDbContext<AppDbContext>(/* ... */);
services.AddMassTransit(x =>
{
    x.AddEntityFrameworkOutbox<AppDbContext>(o =>
    {
        o.UseSqlServer();
        o.UseBusOutbox();
    });
});
```

Publish from inside a handler / endpoint and the message is staged in the EF Core context until `SaveChangesAsync`.

### Syed.Messaging

```csharp
services.AddDbContext<AppDbContext>(/* ... */);
services.AddScoped<IOutboxStore, EfCoreOutboxStore<AppDbContext>>();
services.AddHostedService<OutboxPublisherService>();

// In your application service:
public class PlaceOrder
{
    private readonly AppDbContext _db;
    private readonly IOutboxStore _outbox;

    public async Task Handle(Order o, CancellationToken ct)
    {
        _db.Orders.Add(o);

        // EfCoreOutboxStore.SaveAsync internally calls _db.SaveChangesAsync(ct),
        // which commits BOTH the staged Order and the new OutboxMessage in one
        // EF Core transaction. Don't call SaveChangesAsync again afterward.
        await _outbox.SaveAsync(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Destination = "orders.created",
            MessageType = "orders.created",
            Payload = JsonSerializer.SerializeToUtf8Bytes(new OrderCreated(o.Id, o.CustomerId)),
            CreatedAtUtc = DateTimeOffset.UtcNow
        }, ct);
    }
}
```

The DB write and the outbox row commit in the same EF Core transaction because both `Add` calls accumulate in the same `DbContext` change tracker and `EfCoreOutboxStore.SaveAsync` commits everything in one `SaveChangesAsync`. `OutboxPublisherService` polls every 5 seconds, drains pending rows, and publishes via `IMessageBus`. See [EfCoreOutboxStore.cs](../src/Syed.Messaging.Outbox.EfCore/EfCoreOutboxStore.cs) and [OutboxPublisherService.cs](../src/Syed.Messaging.Outbox.EfCore/OutboxPublisherService.cs). Raw mode (set `UseRawMode = true` on the publisher service) publishes payloads without round-tripping through the type registry, which is useful when the producing service and consuming service don't share CLR types.

Pair the outbox with the inbox (`AddEfInboxStore<TDbContext>`) on the consumer side and you get at-least-once + idempotent processing across services.

**PII / payload encryption.** Outbox rows are stored in your application database as `byte[]` payloads, unencrypted at the framework layer. For messages that carry PII, PHI, payment data, or other sensitive content, apply column-level encryption (SQL Server TDE / Always Encrypted, PostgreSQL `pgcrypto`, MySQL transparent encryption) and configure retention/purge on processed rows. MassTransit users moving production workloads should mirror whatever payload-encryption policy they had on the MT outbox tables.

## Credentials, secrets, and production hardening

A few patterns that MassTransit's docs cover explicitly and Syed.Messaging's don't yet:

1. **Never paste the production connection string into `appsettings.json` and commit it.** Use a configuration provider — Azure Key Vault, AWS Secrets Manager, `dotnet user-secrets` for local dev, Kubernetes Secret + env-var binding for prod. Wire it into `RabbitMqOptions.ConnectionString` from configuration, not from a literal.
2. **Create dedicated broker users per service, not `guest`.** RabbitMQ's `guest` user is unrestricted to loopback only by default but unrestricted everywhere else. In prod, create a vhost per logical service and a user with vhost-scoped read/write permissions. Limits the blast radius of a credential leak.
3. **Use TLS (`amqps://`, `SASL_SSL`, ASB managed identity) in production.** Syed.Messaging passes the connection string straight to the underlying transport client (`RabbitMQ.Client`, `Confluent.Kafka`, `Azure.Messaging.ServiceBus`) — every native auth mode those libraries support works. Sample-level helpers for TLS configurations are on the contribution wishlist.
4. **Connection-string logging caveat.** As of v1.2.0, `RabbitMqTransport` logs the full `ConnectionString` (including embedded `user:password`) at `LogError` level on connection-failure ([src/Syed.Messaging.RabbitMq/RabbitMqTransport.cs:70](../src/Syed.Messaging.RabbitMq/RabbitMqTransport.cs#L70)). If your log sink retains error lines (App Insights, Datadog, ELK), production credentials will land there. The fix is queued — see [GitHub issues](https://github.com/moshiur/Syed.Messaging/issues). Until it ships, set `ConnectionString` from a config provider rather than appsettings, and consider auditing your error-log retention.
5. **Cross-tenant header trust.** The `TenantContextMiddleware` pattern shown in this guide assumes `tenant-id` headers come from a trusted producer (your own services). If your queues are reachable from untrusted producers, validate or signature-check the header before trusting it — same advice as any HTTP header from outside your trust boundary.

## Side-by-side: middleware / filters

### MassTransit

```csharp
public class TenantFilter<T> : IFilter<ConsumeContext<T>> where T : class
{
    public async Task Send(ConsumeContext<T> context, IPipe<ConsumeContext<T>> next)
    {
        if (context.Headers.TryGetHeader("tenant-id", out var value))
            SetTenant(value.ToString());
        await next.Send(context);
    }
    public void Probe(ProbeContext ctx) { }
}

cfg.UseConsumeFilter(typeof(TenantFilter<>), ctx);
```

### Syed.Messaging

```csharp
public class TenantContextMiddleware : IMessageMiddleware
{
    public async Task InvokeAsync(IMessageEnvelope envelope, IServiceProvider sp, Func<Task> next)
    {
        if (envelope.Headers.TryGetValue("tenant-id", out var tenantId))
            sp.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        await next();
    }
}

services.AddMessaging(m => m.AddMiddleware<TenantContextMiddleware>());
```

`IMessageMiddleware` is non-generic — it sees the raw envelope (headers + body) and decides whether to call `next()`. Middlewares run in registration order, first registered = outermost wrapper. See [IMessageMiddleware.cs](../src/Syed.Messaging.Abstractions/IMessageMiddleware.cs). What you don't get: MassTransit's `IPipe<T>` composition with `Probe`, scoped pipelines per message type, or pipe specification chaining. If you need per-message-type middleware, branch inside `InvokeAsync` on `envelope.MessageType`.

## Side-by-side: observability

### MassTransit

```csharp
services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource("MassTransit"))
    .WithMetrics(m => m.AddMeter("MassTransit"));
```

### Syed.Messaging

```csharp
services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource("Syed.Messaging"))
    .WithMetrics(m => m.AddMeter("Syed.Messaging"));
```

Or use the convenience extension from `Syed.Messaging.OpenTelemetry`:

```csharp
services.AddOpenTelemetry()
    .WithTracing(t => t.AddSyedMessagingInstrumentation());
```

You get publish + consume activities with W3C trace context propagation across the wire. Metrics emitted by the `Syed.Messaging` meter:

- `messaging.messages.published`
- `messaging.messages.received`
- `messaging.messages.processed`
- `messaging.messages.failed`
- `messaging.messages.retried`
- `messaging.messages.deadlettered`
- `messaging.messages.poisoned`
- `messaging.messages.processing_duration` (histogram, ms)

Tagging varies by counter. The DLQ counter (`messaging.messages.deadlettered`) is the richest: `transport`, `destination`, `message_type`, and `reason` (`max_retry_exhausted`, `handler_exception`, `deserialization_failure`, `schema_validation_failed`, `transport_reject`). Its `destination` tag is normalized to keep cardinality low (GUIDs collapse to `{id}`, long numeric segments to `{n}`, see `MessagingMetrics.NormalizeDestination`). The other counters today tag only `message_type`; if you need richer tagging on those, that's a worthwhile contribution. Prometheus dashboards and KEDA autoscaling signals live in [docs/observability/](observability/).

## Concept mapping table

| MassTransit | Syed.Messaging | Reference |
|---|---|---|
| `IConsumer<T>` | `IMessageHandler<T>` | [IMessageHandler.cs](../src/Syed.Messaging.Abstractions/IMessageHandler.cs) |
| `ConsumeContext<T>` | `MessageContext` (headers, message id, correlation id, retry count) | [MessageContext.cs](../src/Syed.Messaging.Abstractions/MessageContext.cs) |
| `IBus` / `IPublishEndpoint` / `ISendEndpoint` | `IMessageBus` | [IMessageBus.cs](../src/Syed.Messaging.Abstractions/IMessageBus.cs) |
| `IRequestClient<T>` (RPC) | `IRpcHandler<TReq, TRes>` + `IMessageBus.RequestAsync` | [IRpcHandler.cs](../src/Syed.Messaging.Abstractions/IRpcHandler.cs) |
| `SagaStateMachineInstance` + `MassTransitStateMachine<T>` | `ISagaState` + `ISagaHandler<TState, TMsg>` | [Sagas.cs](../src/Syed.Messaging.Abstractions/Sagas.cs) |
| `Event<T>.CorrelateById(...)` | `SagaBuilder.CorrelateOn<T>(m => m.Key, startsNew: ...)` | [SagaBuilder.cs](../src/Syed.Messaging.Sagas/SagaBuilder.cs) |
| `Schedule<T>` + `MessageScheduler` | `ISagaTimeoutScheduler.ScheduleAsync` / `CancelAsync` | [SagaTimeouts.cs](../src/Syed.Messaging.Sagas/SagaTimeouts.cs) |
| `IFilter<ConsumeContext<T>>` / `IPipe` | `IMessageMiddleware` | [IMessageMiddleware.cs](../src/Syed.Messaging.Abstractions/IMessageMiddleware.cs) |
| `[MessageUrn]` / `MessageInitializer` | `[MessageType("orders.created")]` | [MessageTypeAttribute.cs](../src/Syed.Messaging.Core/MessageTypeAttribute.cs) |
| `EntityFrameworkOutboxConfigurator` | `EfCoreOutboxStore<TContext>` + `OutboxPublisherService` | [EfCoreOutboxStore.cs](../src/Syed.Messaging.Outbox.EfCore/EfCoreOutboxStore.cs) |
| `cfg.Host("amqp://...")` (RabbitMQ) | `RabbitMqOptions.ConnectionString` | [RabbitMqOptions.cs](../src/Syed.Messaging.RabbitMq/RabbitMqOptions.cs) |
| `e.PrefetchCount` / `e.ConcurrentMessageLimit` | `RabbitMqOptions.PrefetchCount` + `ConsumerOptions.MaxConcurrency` | [ConsumerOptions.cs](../src/Syed.Messaging.Core/ConsumerOptions.cs) |
| `UseMessageRetry(r => r.Exponential(...))` | `ConsumerOptions.RetryPolicy = new RetryPolicy { ... }` | [RetryPolicy.cs](../src/Syed.Messaging.Core/RetryPolicy.cs) |
| `_error` queue | DLQ via `DeadLetterQueueName` + `x-poison-*` headers | [RabbitMqOptions.cs](../src/Syed.Messaging.RabbitMq/RabbitMqOptions.cs) |
| `AddSource("MassTransit")` | `AddSource("Syed.Messaging")` | [MessagingMetrics.cs](../src/Syed.Messaging.Core/MessagingMetrics.cs) |

## Realistic time budget

What porting actually costs, in our experience:

- **Trivial pubsub services (1-3 consumers, no retry tuning, no sagas):** 30 minutes. The work is mechanical: rename `IConsumer<T>` to `IMessageHandler<T>`, swap the registration block, replace `IPublishEndpoint` with `IMessageBus`, ship it.
- **Retry-heavy consumers with custom error handling:** 1-2 hours per service. The retry model is simpler in Syed.Messaging, so you'll need to flatten MassTransit's two-tier policies into a single `RetryPolicy` plus Polly for anything more sophisticated.
- **Outbox-backed services:** 1-2 hours. The shape is similar but the API is more explicit. You write the `OutboxMessage` rather than letting MassTransit infer it from `Publish` inside a transactional scope.
- **Non-trivial sagas (3-5 states, a couple of timeouts):** 1-3 days per saga. The mechanical translation isn't hard, but you need to convince yourself the new conditional-branching shape preserves the old state machine semantics. Write a property-based test or a state-table test before you cut over.
- **Codebases that lean hard on Automatonymous (10+ states, complex composite events, request/response choreography):** 1 week or more. Consider whether the saga model is the right fit at all, or whether what you have should become a process manager backed by a state column and a queue of events. We've seen teams take the migration as an opportunity to drop dead transitions.
- **Test-suite migration:** budget separately. If you rely heavily on `UsingInMemory` for integration tests, you'll need to switch to Testcontainers (RabbitMQ image starts in ~2 seconds) or stub `IMessageBus` and `IMessageTransport` directly.

A pragmatic path: pick the lowest-risk service first, port it, run it in parallel with the MassTransit version for a week reading the same queue (different consumer group), compare outputs. Once you trust the migration, expand outward.

## What we'd love help with

This is a v1.x project, pre-discovery, and there's plenty of room for contribution. PRs we'd especially welcome:

- **In-memory transport for integration tests.** A `UseInMemory()` builder that satisfies `IMessageTransport` + `IMessageBus` without a broker, so MassTransit refugees can keep their existing test patterns.
- **Richer saga DSL.** A fluent state-machine layer on top of `ISagaHandler<TState, TMsg>` that lets you declare `When(X).In(State).Do(...).TransitionTo(Y)` if you want it. The current shape stays; the DSL is opt-in.
- **More transports.** Amazon SQS / SNS is the biggest gap. NATS would be a strong addition. The transport abstraction is small ([IMessageTransport.cs](../src/Syed.Messaging.Abstractions/IMessageTransport.cs)).
- **General scheduled message API.** `IMessageBus.PublishAsync(destination, message, scheduledAt)` exposed across all three transports, not just internally for retry.
- **Batch consumers.** `IMessageHandler<Batch<T>>` with `BatchSize` and `BatchTimeout` on `ConsumerOptions`.
- **Better docs.** This guide is one of the first migration docs. If you spot a rough edge while porting, open a PR or an issue. The friction you hit is the friction the next person hits.

Issues and pull requests welcome at the repo. Architectural feedback is the most valuable thing you can give a young platform library.
