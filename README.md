# Syed.Messaging

### MIT-licensed, transport-agnostic .NET messaging. Outbox, sagas, OTel, and DLQ-driven autoscaling out of the box.

[![Build & Test](https://github.com/moshiur/Syed.Messaging/actions/workflows/publish.yml/badge.svg)](https://github.com/moshiur/Syed.Messaging/actions/workflows/publish.yml)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)
![Version](https://img.shields.io/badge/version-1.2.0-green.svg)

---

## What it is

Syed.Messaging is an **MIT-licensed**, transport-agnostic .NET messaging framework with one API across RabbitMQ, Kafka, and Azure Service Bus. The operational stack — retry, DLQ, outbox, inbox, sagas, OpenTelemetry, Prometheus metrics, KEDA autoscaling — is built in, not bolted on.

- 🔌 **One API, three transports.** RabbitMQ, Kafka, Azure Service Bus. Swap transport without changing handler code.
- 📊 **Observability-first.** OpenTelemetry traces + 7 counters + 1 histogram + DLQ dashboard + autoscaling playbook ship with the library.
- 🧰 **Production patterns built in.** Outbox, inbox, sagas, retry + DLQ, middleware pipeline, RPC, health checks.
- ⚡ **Quick to hello-world.** `docker compose up -d` then `dotnet run` — see [Quick Start](#-quick-start).

Coming from MassTransit? The migration guide at [docs/migrating-from-masstransit.md](docs/migrating-from-masstransit.md) covers consumer registration, sagas, outbox, retry, and middleware side-by-side.

```csharp
// Register everything in one fluent chain
services.AddMessaging(builder =>
{
    builder.UseRabbitMq(o => o.ConnectionString = "amqp://localhost");
    builder.AddMiddleware<TenantContextMiddleware>();
    builder.AddConsumer<OrderCreated, OrderCreatedHandler>(o =>
    {
        o.Destination = "orders.created";
        o.SubscriptionName = "orders.consumer";
        o.RetryPolicy = new RetryPolicy { MaxRetries = 3, Backoff = RetryBackoff.Exponential };
        o.MaxConcurrency = 4;
    });
});
```

---

## 📊 DLQ-driven autoscaling, with a documented signal model

Most messaging libraries ship metrics. Few ship the playbook for what to do with them.

Syed.Messaging emits a Prometheus-ready meter (`Syed.Messaging`) with 7 counters and a processing-duration histogram. The DLQ counter carries `transport`, `destination`, `message_type`, and `reason` tags (destination is normalized to keep cardinality bounded). The repo ships:

- 📈 **DLQ dashboard + alerts** — [docs/observability/dlq-dashboard.md](docs/observability/dlq-dashboard.md)
- 📐 **Autoscaling signal model** — retry pressure, throughput headroom, retry-to-DLQ conversion, poison ratio — [docs/observability/autoscaling-signals.md](docs/observability/autoscaling-signals.md)
- ☸️ **KEDA + HPA reference manifests** — [docs/deploy/kubernetes/](docs/deploy/kubernetes/)

Block scale-up when conversion ratio spikes (broken pipeline). Scale on retry pressure (real load). Don't burn replicas on poison messages. The decision flow is documented.

---

## ✨ Features

| Feature | Description |
|:--|:--|
| **Transport-agnostic** | RabbitMQ, Kafka, Azure Service Bus — same API |
| **Typed consumers** | `IMessageHandler<T>` with automatic deserialization |
| **Middleware pipeline** | `IMessageMiddleware` for cross-cutting concerns *(v1.1.0)* |
| **Retry + DLQ** | Configurable retry policies with exponential backoff and dead-letter routing |
| **Outbox pattern** | EF Core-based transactional outbox with raw mode support |
| **Inbox deduplication** | Idempotent consumer pattern via EF Core |
| **Saga orchestration** | State management, correlation, timeouts, distributed locking |
| **RPC support** | Request/response messaging with `IRpcHandler<TReq, TRes>` |
| **Observability** | OpenTelemetry spans + 7 counters + 1 histogram on the `Syed.Messaging` meter |
| **Autoscaling playbook** | DLQ + retry signal model with KEDA / HPA reference manifests |
| **Per-destination queues (RabbitMQ)** | Each consumer gets its own queue — no cross-talk *(v1.2.0)* |
| **Health checks** | ASP.NET Core health check integration per transport |
| **SignalR bridge** | Route messaging events to SignalR hubs |
| **Service discovery** | Kubernetes DNS, Consul, standard DNS |
| **Message versioning** | `VersionedMessage<T>`, schema registry, compatibility rules |

---

## 📦 Packages

| Package | Description |
|:--|:--|
| `Syed.Messaging.Abstractions` | Core interfaces: `IMessageBus`, `IMessageHandler<T>`, `IMessageMiddleware`, `IMessageTransport` |
| `Syed.Messaging.Core` | `GenericMessageConsumer<T>`, `RpcMessageConsumer`, `MessagingBuilder`, retry/DLQ logic |
| `Syed.Messaging.RabbitMq` | RabbitMQ transport with topology builder, publisher confirms, per-destination queues |
| `Syed.Messaging.Kafka` | Kafka transport with topic-based retry/DLQ |
| `Syed.Messaging.AzureServiceBus` | Azure Service Bus transport with scheduled message retry |
| `Syed.Messaging.Outbox.EfCore` | Transactional outbox with `OutboxPublisherService` and raw mode |
| `Syed.Messaging.Inbox.EfCore` | Idempotent consumer inbox pattern |
| `Syed.Messaging.Sagas` | Saga primitives: state, correlation, timeouts, locking |
| `Syed.Messaging.Sagas.EfCore` | EF Core persistence for saga state and timeouts |
| `Syed.Messaging.Sagas.Redis` | Redis distributed saga locking |
| `Syed.Messaging.OpenTelemetry` | Activity spans for publish/consume and trace context propagation |
| `Syed.Messaging.HealthChecks` | ASP.NET Core health check integration |
| `Syed.Messaging.SignalR` | Bridge messaging events to SignalR hubs |
| `Syed.Messaging.Aspire` | .NET Aspire integration helpers |
| `Syed.BuildingBlocks` | Shared utilities and feature flags |

### Installation

The publish workflow ([.github/workflows/publish.yml](.github/workflows/publish.yml)) pushes packages to **NuGet.org** (when the `NUGET_API_KEY` secret is set on the release) and **GitHub Packages** (always). For most users, NuGet.org is the easier path:

```bash
dotnet add package Syed.Messaging.Core --version 1.2.0
dotnet add package Syed.Messaging.RabbitMq --version 1.2.0
```

> If a specific version isn't on NuGet.org yet, it's available on [GitHub Packages](https://github.com/moshiur/Syed.Messaging/packages). Add that feed with a personal access token scoped to `read:packages`:
>
> ```bash
> # Read PAT from env or prompt — avoids writing it into shell history.
> # On non-Windows, NuGet.config stores the password in plaintext;
> # consider using `dotnet user-secrets` for a more secure local-dev pattern.
> read -rs -p "GitHub PAT (read:packages scope): " GH_PAT && echo
>
> dotnet nuget add source https://nuget.pkg.github.com/moshiur/index.json \
>   --name syed-messaging \
>   --username "$GITHUB_USERNAME" \
>   --password "$GH_PAT"
> ```

---

## 🚀 Quick Start

**Prerequisites:** Docker (for the broker stack) + .NET 10 preview SDK.

### 1. Clone and start the broker stack

```bash
git clone https://github.com/moshiur/Syed.Messaging.git
cd Syed.Messaging
docker compose up -d   # RabbitMQ + Kafka + Zookeeper (bound to 127.0.0.1)
```

[docker-compose.yml](docker-compose.yml) exposes RabbitMQ on `localhost:5672` (management UI on `localhost:15672`) and Kafka on `localhost:9092`. **Loopback-only by design** — see the security note in that file. Azure Service Bus needs a real namespace; see [samples/ServiceBusWorker/](samples/ServiceBusWorker/).

### 2. Run the OrderWorker sample

```bash
dotnet run --project samples/OrderWorker/OrderWorker.csproj
```

Within ~30 seconds you should see the handler log line:

```
info: OrderCreatedHandler[0]
      Worker received OrderCreated: OrderId=<guid>, CustomerId=customer-123, Retry=0
```

The sample publishes a test event on startup and consumes it via `OrderCreatedHandler` ([Program.cs](samples/OrderWorker/Program.cs), [OrderCreatedHandler.cs](samples/OrderWorker/OrderCreatedHandler.cs)). It's the canonical "hello world" — read those two files to see the working shape.

### 3. Build your own (the same shape)

Define a message and handler:

```csharp
[MessageType("orders.created")]
public record OrderCreated(Guid OrderId, string CustomerId);

public class OrderCreatedHandler : IMessageHandler<OrderCreated>
{
    private readonly ILogger<OrderCreatedHandler> _logger;
    public OrderCreatedHandler(ILogger<OrderCreatedHandler> logger) => _logger = logger;

    public Task HandleAsync(OrderCreated message, MessageContext ctx, CancellationToken ct)
    {
        _logger.LogInformation("Got order {OrderId} for {CustomerId}", message.OrderId, message.CustomerId);
        return Task.CompletedTask;
    }
}
```

Wire it up:

```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddMessaging(m =>
{
    m.UseRabbitMq(o =>
    {
        o.ConnectionString = "amqp://guest:guest@localhost:5672/";
        o.MainExchangeName = "orders.exchange";
    });

    m.AddConsumer<OrderCreated, OrderCreatedHandler>(c =>
    {
        c.Destination = "orders.created";
        c.SubscriptionName = "orders.consumer";
    });
});

var app = builder.Build();

// Publish from any DI scope — e.g. on startup or from an ASP.NET Core controller.
using (var scope = app.Services.CreateScope())
{
    var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
    await bus.PublishAsync("orders.created", new OrderCreated(Guid.NewGuid(), "cust-123"));
}

await app.RunAsync();
```

---

## 🔌 Middleware Pipeline

Middleware runs before every handler — ideal for tenant context, logging, auth propagation:

```csharp
public class TenantContextMiddleware : IMessageMiddleware
{
    public async Task InvokeAsync(IMessageEnvelope envelope, IServiceProvider sp, Func<Task> next)
    {
        if (envelope.Headers.TryGetValue("tenant-id", out var tenantId))
        {
            var ctx = sp.GetRequiredService<ITenantContext>();
            ctx.SetTenant(tenantId);
        }
        await next(); // handler runs with tenant context set
    }
}

// Register
services.AddMessaging(m => m.AddMiddleware<TenantContextMiddleware>());
```

Middlewares execute in registration order (first registered = outermost wrapper).

---

## 🐇 RabbitMQ Transport

Per-destination queue routing *(v1.2.0)*:

```
Publisher ──routing key──► Main Exchange (Direct)
                              │
                    ┌─────────┼──────────┐
                    ▼         ▼          ▼
            orders.queue  billing.queue  notifications.queue
                    │
                    ▼ (on failure)
              Retry Exchange ──► Retry Queue (TTL) ──DLX──► Main Exchange
                                                            (preserves routing key)
```

- Each `AddConsumer<T>()` auto-declares its own queue bound by destination
- Retry queue preserves original routing key on DLX — messages return to the correct queue
- DLQ captures poison messages with diagnostic headers (`x-poison-*`)

---

## 🧵 Kafka Partition Strategy

Kafka message ordering is partition-scoped, not topic-scoped. To preserve ordering for a business entity, publish a stable `partition-key`.

```csharp
await bus.PublishRawAsync(
    "orders.created",
    JsonSerializer.SerializeToUtf8Bytes(new OrderCreated(orderId, customerId)),
    "OrderCreated",
    new Dictionary<string, string> { ["partition-key"] = customerId });
```

### Practical guidance

- Use **aggregate IDs** (`CustomerId`, `OrderId`, `TenantId`) as `partition-key`.
- Same key means same partition, which gives deterministic in-order handling for that key.
- Different keys can run in parallel with:

```csharp
services.AddMessaging(m =>
{
    m.UseKafka(k =>
    {
        k.Consumer.MaxConcurrentPartitions = 4;
        k.Consumer.PartitionAssignmentStrategy = KafkaPartitionAssignmentStrategy.CooperativeSticky;
    });
});
```

This gives you the common production shape: strict ordering per entity, concurrency across entities.

---

## 📤 Outbox Pattern

Guarantee at-least-once delivery with a transactional outbox. The DB write and the outbox row commit in the **same EF Core transaction** because both `Add` calls accumulate in the same `DbContext` and `EfCoreOutboxStore.SaveAsync` commits everything in one `SaveChangesAsync`:

```csharp
dbContext.Orders.Add(order);

// EfCoreOutboxStore.SaveAsync internally calls dbContext.SaveChangesAsync(),
// which commits BOTH the staged Order and the new OutboxMessage in one
// transaction. Do not call SaveChangesAsync again afterward.
await outbox.SaveAsync(new OutboxMessage
{
    Id           = Guid.NewGuid(),
    Destination  = "orders.created",
    MessageType  = "orders.created",                     // stable wire key (see [MessageType] convention)
    Payload      = JsonSerializer.SerializeToUtf8Bytes(order),
    CreatedAtUtc = DateTimeOffset.UtcNow,
}, ct);

// OutboxPublisherService polls every 5s and republishes via IMessageBus.
services.AddHostedService<OutboxPublisherService>();
```

Supports **raw mode** (`UseRawMode = true` on `OutboxPublisherService`) for anonymous payloads when the producing and consuming services don't share CLR types.

> **PII / encryption note:** outbox rows are stored in your application database as `byte[]` payloads, unencrypted. For PII / PHI / payment data, apply column-level encryption (TDE / Always Encrypted / pgcrypto) and configure retention/purge on processed rows.

---

## ♻ Saga Orchestration

Long-running workflows with state management and distributed locking. Sagas are plain classes that implement `ISagaHandler<TSagaState, TMessage>`. Sending happens via the injected `IMessageBus`; timeouts via the injected `ISagaTimeoutScheduler`.

```csharp
public class OrderSagaState : ISagaState
{
    public Guid Id { get; set; }
    public int Version { get; set; }
    public Guid OrderId { get; set; }
    public string CurrentState { get; set; } = "AwaitingInventory";
}

public class OrderSaga : ISagaHandler<OrderSagaState, OrderCreated>
{
    private readonly IMessageBus _bus;
    private readonly ISagaTimeoutScheduler _timeouts;

    public OrderSaga(IMessageBus bus, ISagaTimeoutScheduler timeouts)
    {
        _bus = bus;
        _timeouts = timeouts;
    }

    public async Task HandleAsync(OrderSagaState state, OrderCreated msg, MessageContext ctx, CancellationToken ct)
    {
        state.OrderId = msg.OrderId;
        state.CurrentState = "AwaitingInventory";

        await _bus.PublishAsync("inventory.reserve", new ReserveInventory(msg.OrderId), ct);

        await _timeouts.ScheduleAsync(
            sagaType: typeof(OrderSaga),
            correlationKey: msg.OrderId.ToString(),
            delay: TimeSpan.FromMinutes(5),
            timeout: new InventoryTimeout(msg.OrderId),
            ct: ct);
    }
}
```

Wiring needs a `SagaMessageHandler<TMessage>` consumer for each triggering message, plus an `AddSagas` block that declares correlation. The full worked example is in [samples/OrderSagaDemo/](samples/OrderSagaDemo/). Persistence options: EF Core (`Syed.Messaging.Sagas.EfCore`) or in-memory. Locking: Redis (`Syed.Messaging.Sagas.Redis`), in-memory, or no-op.

---

## 📊 Observability

```csharp
// OpenTelemetry tracing (publish + consume activities, W3C trace context).
services.AddOpenTelemetry()
    .WithTracing(b => b.AddSource("Syed.Messaging"))
    .WithMetrics(b => b.AddMeter("Syed.Messaging"));
```

Metrics emitted by the `Syed.Messaging` meter (defined in [`src/Syed.Messaging.Core/MessagingMetrics.cs`](src/Syed.Messaging.Core/MessagingMetrics.cs)) — 7 counters plus a processing-duration histogram:

- `messaging.messages.published`
- `messaging.messages.received`
- `messaging.messages.processed`
- `messaging.messages.failed`
- `messaging.messages.retried`
- `messaging.messages.deadlettered` *(richest tagging: `transport`, `destination`, `message_type`, `reason`; destination normalized)*
- `messaging.messages.poisoned`
- `messaging.messages.processing_duration` *(histogram, ms)*

The convenience extension from `Syed.Messaging.OpenTelemetry` is equivalent to the tracing block above:

```csharp
services.AddOpenTelemetry()
    .WithTracing(t => t.AddSyedMessagingInstrumentation());
```

---

## 🧪 Testing

A broker-free unit test suite across 7 test projects (run `dotnet test` for the current count). Broker-touching tests are skipped when the relevant env var is unset; the CI workflow at [.github/workflows/publish.yml](.github/workflows/publish.yml) spins up Kafka + Zookeeper as service containers.

```bash
dotnet test Syed.Messaging.sln -c Release
```

> No in-memory test transport ships in v1.2.0 — integration tests need Testcontainers or a real local broker. An `IMessageTransport` in-memory implementation is on the contribution wishlist.

---

## 📋 Changelog

### v1.2.0 — Per-Destination Queue Routing
- `SubscribeAsync` auto-declares per-destination queues bound by routing key
- Each consumer only receives its own messages — fixes shared-queue poison issue
- Retry/DLQ routing preserves original destination
- `MainQueueName` deprecated

### v1.1.0 — Middleware Pipeline
- `IMessageMiddleware` interface for pre-handler cross-cutting concerns
- Integrated into `GenericMessageConsumer` and `RpcMessageConsumer`
- `MessagingBuilder.AddMiddleware<T>()` for fluent registration

---

## 🤝 Contributing

PRs, issues, and design discussions are welcome.
This is a platform-style library — architectural feedback is especially valuable.

See [ROADMAP.md](ROADMAP.md) for planned features and current status.

---

## 📝 License

[MIT](LICENSE)
