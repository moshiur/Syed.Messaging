# Syed.Messaging  
### A lightweight, transport-agnostic .NET messaging framework with RabbitMQ, Kafka, Azure Service Bus and .NET Aspire integration.

[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
![Build](https://img.shields.io/badge/build-alpha-brightgreen.svg)
![.NET](https://img.shields.io/badge/.NET-10.0-blue.svg)
![Status](https://img.shields.io/badge/status-experimental-orange.svg)

Syed.Messaging is a **minimal, clean, extensible messaging framework** for .NET applications.

It gives you:

- Transport-agnostic **abstractions**
- **RabbitMQ**, **Kafka**, and **Azure Service Bus** transports
- Retry, delayed retry (TTL / retry topics / scheduled messages) and **DLQ**
- Simple `IMessageHandler<T>` consumer model
- **EF Core Outbox** implementation (with type metadata)
- Message versioning helpers (version header + utilities)
- Distributed tracing hooks (via `ActivitySource` / OpenTelemetry-ready)
- Per-consumer **concurrency configuration**
- Early **Saga primitives** for orchestration
- Optional .NET Aspire helper to wire RabbitMQ

> ⚠️ This is still **alpha / experimental** – ideal as a learning platform or a base to evolve inside your organization.

---

## 📁 Repository Structure

```text
/ src
  / Syed.Messaging.Abstractions
  / Syed.Messaging.Core
  / Syed.Messaging.RabbitMq
  / Syed.Messaging.Kafka
  / Syed.Messaging.AzureServiceBus
  / Syed.Messaging.Outbox.EfCore
  / Syed.Messaging.Aspire

/ samples
  / OrderWorker        # RabbitMQ-based worker sample

README.md
```

---

## ✨ Core Concepts

### Abstractions (in `Syed.Messaging.Abstractions`)

```csharp
public interface IMessageBus
{
    Task PublishAsync<T>(string destination, T message, CancellationToken ct = default);
    Task SendAsync<T>(string destination, T message, CancellationToken ct = default);
}

public interface IMessageHandler<TMessage>
{
    Task HandleAsync(TMessage message, MessageContext context, CancellationToken ct);
}

public interface IMessageTransport
{
    Task PublishAsync(IMessageEnvelope envelope, string destination, CancellationToken ct);
    Task SendAsync(IMessageEnvelope envelope, string destination, CancellationToken ct);
    Task SubscribeAsync(
        string subscriptionName,
        string destination,
        Func<IMessageEnvelope, CancellationToken, Task<TransportAcknowledge>> handler,
        CancellationToken ct);
}
```

### Consumers (in `Syed.Messaging.Core`)

```csharp
public sealed class ConsumerOptions<TMessage>
{
    public string Destination { get; set; } = default!;
    public string SubscriptionName { get; set; } = default!;
    public RetryPolicy RetryPolicy { get; set; } = new();
    public int MaxConcurrency { get; set; } = 1;
}
```

`GenericMessageConsumer<T>` uses these options and enforces `MaxConcurrency` via a semaphore around handler execution.

### Retry Policy

```csharp
public sealed class RetryPolicy
{
    public int MaxRetries { get; init; } = 3;
    public TimeSpan InitialDelay { get; init; } = TimeSpan.FromSeconds(5);
    public RetryBackoff Backoff { get; init; } = RetryBackoff.Exponential;
}
```

Transports interpret **Retry / DeadLetter** decisions from the generic consumer and route to retry queues/topics or DLQ.

---

## 🚀 Quickstart (RabbitMQ + Worker Sample)

### 1. Start RabbitMQ locally

```bash
docker run -d --hostname rabbit --name rabbit   -p 5672:5672 -p 15672:15672   rabbitmq:3-management
```

### 2. Build the solution

From repo root:

```bash
dotnet new sln -n Syed.Messaging
dotnet sln add ./src/Syed.Messaging.Abstractions/Syed.Messaging.Abstractions.csproj
dotnet sln add ./src.Syed.Messaging.Core/Syed.Messaging.Core.csproj
dotnet sln add ./src/Syed.Messaging.RabbitMq/Syed.Messaging.RabbitMq.csproj
dotnet sln add ./src/Syed.Messaging.Kafka/Syed.Messaging.Kafka.csproj
dotnet sln add ./src/Syed.Messaging.AzureServiceBus/Syed.Messaging.AzureServiceBus.csproj
dotnet sln add ./src/Syed.Messaging.Outbox.EfCore/Syed.Messaging.Outbox.EfCore.csproj
dotnet sln add ./src/Syed.Messaging.Aspire/Syed.Messaging.Aspire.csproj
dotnet sln add ./samples/OrderWorker/OrderWorker.csproj

dotnet build
```

> Adjust paths if your layout differs.

### 3. Run the sample worker

```bash
cd samples/OrderWorker
dotnet run
```

The worker will:

- connect to RabbitMQ
- declare exchanges/queues (main + retry + DLQ)
- consume `OrderCreated` messages from `orders.created`

### 4. Publish a test message

You can hit RabbitMQ with any client, or (for dev) you can temporarily tweak the sample worker to send a test message on startup.

Example snippet to drop into `Program.cs` if you want auto-fire:

```csharp
using (var scope = app.Services.CreateScope())
{
    var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
    await bus.PublishAsync("orders.created", new OrderCreated(Guid.NewGuid(), "customer-123"));
}
```

You should see logs like:

```text
Worker received OrderCreated event: OrderId=..., CustomerId=customer-123, Retry=0
```

---

## 🧩 Transports Overview

### 🐇 RabbitMQ (`Syed.Messaging.RabbitMq`)

- Exchange & queue topology provisioned by `RabbitTopologyBuilder`
- Retry queue (TTL) loops back to main exchange
- DLQ queue receives poisoned messages
- Uses headers: `x-retry-count`, `message-id`, `correlation-id`

### 🧵 Kafka (`Syed.Messaging.Kafka`) — Minimal Implementation

- Topic-based publishing and subscribing using Confluent.Kafka
- Simple retry via separate `-retry` topic
- DLQ via `-dlq` topic
- This is intentionally minimal; extend partitions, consumer group settings as needed.

### ☁ Azure Service Bus (`Syed.Messaging.AzureServiceBus`) — Minimal Implementation

- Queue / topic publishing and subscribing
- Retry by sending scheduled messages (delayed)
- DLQ via Service Bus’ native DLQ
- Skeleton for you to tune per your environment (lock duration, prefetch, etc.).

---

## 📦 Outbox, Versioning, Tracing, Sagas

### ✅ Outbox (EF Core)

- `OutboxMessage` holds `Destination`, `MessageType`, `Payload`, and timestamps.
- `EfCoreOutboxStore<TContext>` works with your DbContext
- `OutboxPublisherService` loads pending messages and publishes using `IMessageBus`

Current strategy:

- `MessageType` stores the CLR type name
- A small helper resolves `Type.GetType(message.MessageType)` and uses the main serializer

You will likely want to:

- constrain assemblies for security
- plug a custom type-mapping strategy

### 🧬 Message Versioning Helpers

- Version header: `"message-version"`
- Helper methods in `MessageVersioning` to read/write versions and to default on missing versions.

### 📡 Distributed Tracing

- Uses `System.Diagnostics.ActivitySource` in core to create spans for:
  - publish/send calls
  - message consumption
- You can hook this into OpenTelemetry by registering `ActivitySource` with your OTEL pipeline.

### 🔁 Concurrency per Consumer

- `ConsumerOptions<T>.MaxConcurrency` controls how many handler invocations can run at once.  
- GenericMessageConsumer wraps handler in a `SemaphoreSlim` to enforce this.

### ♻ Saga Primitives (very early)

- `ISagaState` – base for saga state objects (`Id`, `Version`, etc.)
- `ISagaHandler<TSagaState, TMessage>` – pattern for handling messages in the context of a saga state
- No persistence or orchestration engine is included yet — these are building blocks.

---

## 🧭 Roadmap

### Short-term
- Flesh out Kafka and Azure Service Bus transports (configuration, retries)
- Stronger integration tests (dockerized brokers)
- Better Outbox configuration & examples

### Medium-term
- Built-in OpenTelemetry instrumentation package
- Generic saga orchestration hosted service
- Polished samples for each transport (RabbitMQ, Kafka, Azure Service Bus)

---

## 🤝 Contributing

PRs, issues, and design discussions are welcome.  
This is a platform-style library — architectural feedback is especially valuable.

---

## 📝 License

MIT.
