# Syed.Messaging

### A transport-agnostic .NET messaging framework with built-in retry, DLQ, middleware, outbox, inbox, sagas, and observability.

[![Build & Test](https://github.com/moshiur/Syed.Messaging/actions/workflows/publish.yml/badge.svg)](https://github.com/moshiur/Syed.Messaging/actions/workflows/publish.yml)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)
![Version](https://img.shields.io/badge/version-1.2.0-green.svg)

---

## Why Syed.Messaging?

Most .NET messaging libraries force you into a specific broker. Syed.Messaging gives you **one API** across RabbitMQ, Kafka, and Azure Service Bus — with production patterns built in, not bolted on.

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

## ✨ Features

| Feature | Description |
|:--|:--|
| **Transport-agnostic** | RabbitMQ, Kafka, Azure Service Bus — same API |
| **Typed consumers** | `IMessageHandler<T>` with automatic deserialization |
| **Per-destination queues** | Each consumer gets its own queue — no cross-talk *(v1.2.0)* |
| **Middleware pipeline** | `IMessageMiddleware` for cross-cutting concerns *(v1.1.0)* |
| **Retry + DLQ** | Configurable retry policies with exponential backoff and dead-letter routing |
| **Outbox pattern** | EF Core-based transactional outbox with raw mode support |
| **Inbox deduplication** | Idempotent consumer pattern via EF Core |
| **Saga orchestration** | State management, correlation, timeouts, distributed locking |
| **RPC support** | Request/response messaging with `IRpcHandler<TReq, TRes>` |
| **Observability** | OpenTelemetry spans + 7 Prometheus-ready metrics |
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

Packages are published to [GitHub Packages](https://github.com/moshiur/Syed.Messaging/packages):

```bash
dotnet add package Syed.Messaging.Core --version 1.2.0
dotnet add package Syed.Messaging.RabbitMq --version 1.2.0
```

---

## 🚀 Quick Start

### 1. Define a message and handler

```csharp
[MessageType("orders.created")]
public record OrderCreated(Guid OrderId, string CustomerId);

public class OrderCreatedHandler : IMessageHandler<OrderCreated>
{
    public async Task HandleAsync(OrderCreated message, MessageContext ctx, CancellationToken ct)
    {
        Console.WriteLine($"Processing order {message.OrderId} for {message.CustomerId}");
    }
}
```

### 2. Wire up in `Program.cs`

```csharp
var builder = Host.CreateDefaultBuilder(args);
builder.ConfigureServices(services =>
{
    services.AddMessaging(m =>
    {
        m.UseRabbitMq(o =>
        {
            o.ConnectionString = "amqp://guest:guest@localhost:5672";
            o.MainExchangeName = "myapp.events";
        });

        m.AddConsumer<OrderCreated, OrderCreatedHandler>(o =>
        {
            o.Destination = "orders.created";
            o.SubscriptionName = "orders.consumer";
        });
    });
});

await builder.Build().RunAsync();
```

### 3. Publish messages

```csharp
var bus = serviceProvider.GetRequiredService<IMessageBus>();
await bus.PublishAsync("orders.created", new OrderCreated(Guid.NewGuid(), "cust-123"));
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

## 📤 Outbox Pattern

Guarantee at-least-once delivery with transactional outbox:

```csharp
// Save to DB + queue atomically
var outbox = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
await outbox.SaveAsync(new OutboxMessage
{
    Destination = "orders.created",
    MessageType = "OrderCreated",
    Payload = JsonSerializer.SerializeToUtf8Bytes(order)
});
await dbContext.SaveChangesAsync(); // single transaction

// OutboxPublisherService polls and publishes in background
services.AddHostedService<OutboxPublisherService>();
```

Supports **raw mode** for anonymous payloads (no CLR type resolution needed).

---

## ♻ Saga Orchestration

Long-running workflows with state management and distributed locking:

```csharp
public class OrderSaga : ISagaHandler<OrderSagaState, OrderCreated>
{
    public async Task HandleAsync(OrderSagaState state, OrderCreated msg, ISagaContext ctx)
    {
        state.OrderId = msg.OrderId;
        await ctx.SendAsync("inventory.reserve", new ReserveInventory(msg.OrderId));
        ctx.SetTimeout(TimeSpan.FromMinutes(5), "InventoryTimeout");
    }
}
```

Persistence: EF Core or in-memory. Locking: Redis, in-memory, or no-op.

---

## 📊 Observability

```csharp
// OpenTelemetry integration
services.AddOpenTelemetry()
    .WithTracing(b => b.AddSource("Syed.Messaging"));

// Built-in metrics (System.Diagnostics.Metrics)
// - messages_published, messages_consumed, messages_retried
// - messages_dead_lettered, handler_duration, handler_errors
// - active_consumers
```

---

## 🧪 Testing

76 tests across 5 test projects:

```bash
dotnet test --configuration Release
```

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
