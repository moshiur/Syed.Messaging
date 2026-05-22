# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

**Syed.Messaging** — a transport-agnostic .NET 10 messaging framework distributed as ~15 NuGet packages (Abstractions, Core, RabbitMq, Kafka, AzureServiceBus, Outbox.EfCore, Inbox.EfCore, Sagas + Sagas.EfCore + Sagas.Redis, OpenTelemetry, HealthChecks, SignalR, Aspire, BuildingBlocks). One API across RabbitMQ / Kafka / Azure Service Bus with production patterns (retry, DLQ, outbox/inbox, sagas, OTel) built in.

The full solution is `Syed.Messaging.sln` at the repo root. Shared MSBuild config lives in `Directory.Build.props` (TFM, package metadata, source-link, snupkg symbols). Library projects are non-packable by default — they opt in by setting `<IsPackable>true</IsPackable>`.

## Commands

All commands run from the repo root. The solution targets **.NET 10.0** (preview-track SDK).

```powershell
# Restore + build the whole solution
dotnet build Syed.Messaging.sln -c Release

# Run every test project
dotnet test Syed.Messaging.sln -c Release

# Run a single test project
dotnet test tests/Syed.Messaging.Sagas.Tests/Syed.Messaging.Sagas.Tests.csproj

# Run a single test by fully-qualified name (xUnit)
dotnet test --filter "FullyQualifiedName~MessageTypeRegistryTests.Resolve_ReturnsRegisteredType"

# Code coverage (writes to ./coverage)
dotnet test --collect:"XPlat Code Coverage" --results-directory ./coverage

# Run a sample (requires the matching broker running locally)
dotnet run --project samples/OrderWorker/OrderWorker.csproj          # RabbitMQ
dotnet run --project samples/KafkaWorker/KafkaWorker.csproj          # Kafka
dotnet run --project samples/ServiceBusWorker/ServiceBusWorker.csproj
dotnet run --project samples/OrderSagaDemo/OrderSagaDemo.csproj      # Saga end-to-end (SQLite)

# Pack a single library (for local NuGet testing)
dotnet pack src/Syed.Messaging.Core/Syed.Messaging.Core.csproj -c Release -o ./nupkgs
```

CI ([.github/workflows/publish.yml](.github/workflows/publish.yml)) spins up Kafka + Zookeeper as service containers and runs `dotnet test` against `KAFKA_BOOTSTRAP_SERVERS=localhost:9092`. Kafka tests will skip or fail locally without a broker — set that env var to point at a running Kafka. RabbitMQ and Azure Service Bus tests likewise need real brokers; most unit tests use in-memory stubs and are broker-free.

The `publish` job runs only on GitHub Release events, packs every `src/**/*.csproj`, and pushes to both GitHub Packages and (if `NUGET_API_KEY` is set) NuGet.org. Version is taken from the release tag (`v1.2.3` → `1.2.3`).

## Architecture

### Layering

```
Abstractions  ← zero-dependency contracts (IMessageBus, IMessageHandler<T>,
                IMessageTransport, IMessageEnvelope, IMessageMiddleware,
                ISagaHandler<TState,TMsg>, IMessageTypeRegistry)
    ▲
Core          ← runtime plumbing: MessagingBuilder, GenericMessageConsumer<T>,
                RpcMessageConsumer, TransportMessageBus, MessageTypeRegistry,
                MessagingMetrics, MessagingDiagnostics, ResiliencePipeline,
                System.Text.Json serializer, service discovery
    ▲
Transports    ← RabbitMq / Kafka / AzureServiceBus implement IMessageTransport
                + transport-specific IMessageBus adapter
    ▲
Capabilities  ← Outbox.EfCore, Inbox.EfCore, Sagas (+ EfCore + Redis),
                OpenTelemetry, HealthChecks, SignalR, Aspire
```

`Syed.Messaging.Abstractions` must stay dependency-free — transports and capability packages depend on it. `Core` is the shared runtime everyone else builds on.

### Wiring model

Everything goes through `services.AddMessaging(builder => { ... })` (see the [Quick Start](README.md#-quick-start) section). The fluent `MessagingBuilder` ([src/Syed.Messaging.Core/MessagingBuilder.cs](src/Syed.Messaging.Core/MessagingBuilder.cs)) is the canonical surface:

- `UseRabbitMq` / `UseKafka` / `UseAzureServiceBus` — register transport + bus
- `AddConsumer<TMessage, THandler>(o => ...)` — registers `THandler` (scoped), `ConsumerOptions<TMessage>` (singleton), and a `GenericMessageConsumer<TMessage>` hosted service
- `AddRpcHandler<TReq, TRes, THandler>(...)` — same pattern but with `RpcMessageConsumer`
- `AddMiddleware<T>()` — `IMessageMiddleware` registered scoped; middlewares run in **registration order**, first registered = outermost wrapper
- `UseSerializer<T>()` — replaces the default `SystemTextJsonSerializer`

`GenericMessageConsumer<T>` is the hot path: it owns the subscription lifetime, builds the `MessageContext`, runs the middleware pipeline, dispatches to the handler under a DI scope, applies the `RetryPolicy`, and routes to DLQ on poison/exhaustion. RPC follows the same shape via `RpcMessageConsumer`.

### Message identity and versioning

Do not use `Type.GetType()` or `AssemblyQualifiedName` for cross-boundary message resolution — that was deliberately removed. Instead:

- `IMessageTypeRegistry` ([src/Syed.Messaging.Core/MessageTypeRegistry.cs](src/Syed.Messaging.Core/MessageTypeRegistry.cs)) maps stable string keys (e.g. `orders.created`) to CLR types, with optional version
- Declare a key via `[MessageType("orders.created")]` on the message record/class
- Transports set `message-type` + `message-version` headers via the registry on publish, and resolve via the registry on receive
- Saga timeouts persist `TimeoutTypeKey` + `TimeoutTypeVersion` (not assembly-qualified names) — same model

When adding a new message type, prefer the attribute. When deserializing in a new transport or store, go through the registry.

### Transport-specific notes

- **RabbitMQ** — Direct exchange + **per-destination queues** (v1.2.0). Each `AddConsumer<T>` declares its own queue bound by routing key. Retry queue has TTL + DLX back to the main exchange, preserving the original routing key so retried messages land in the same consumer queue. DLQ adds `x-poison-*` diagnostic headers. Publisher confirms enabled via `ConfirmSelect` + `WaitForConfirmsOrDie`.
- **Kafka** — Ordering is per-partition. Producers should set the `partition-key` header (aggregate id) so related events land on the same partition. `KafkaOptions.Consumer.MaxConcurrentPartitions` and `PartitionAssignmentStrategy=CooperativeSticky` give per-entity ordering with cross-entity parallelism. Delayed retry is implemented as retry topics per delay (e.g. `retry-30s`, `retry-60s`, `retry-300s`).
- **Azure Service Bus** — Delayed retry uses `ScheduledEnqueueTime` on a republished message (not a separate retry queue). Sessions are propagated via the `session-id` header for session-aware sagas.

### Sagas

Two-step wiring: register a `SagaMessageHandler<TMessage>` as the consumer for each saga-triggering message, then `AddSagas(s => s.AddSaga<TState, TSaga>(cfg => cfg.CorrelateOn<TMessage>(m => m.Key, startsNew: true)))`. The `SagaRuntime` loads state (via `ISagaStateStore<T>` — InMemory, `EfSagaStateStore`, or custom), takes a per-instance lock (via `ISagaLockProvider` — InMemory / NoOp / `RedisSagaLockProvider`), invokes the saga, and saves state with optimistic concurrency. Timeouts go through `ISagaTimeoutScheduler` + `SagaTimeoutDispatcher` (polling background service); persistence via `InMemorySagaTimeoutStore` or `EfSagaTimeoutStore`.

### Observability

- Activities are emitted from `MessagingDiagnostics.PublishActivityName` and `ConsumeActivityName` — enable with `AddSource("Syed.Messaging")` on the OTel tracer. W3C trace context propagation lives in `TraceContextPropagation`.
- Metrics are emitted from the `Syed.Messaging` meter ([src/Syed.Messaging.Core/MessagingMetrics.cs](src/Syed.Messaging.Core/MessagingMetrics.cs)) — 8 instruments: 7 counters (`messaging.messages.published`, `received`, `processed`, `failed`, `retried`, `deadlettered`, `poisoned`) plus a `messaging.messages.processing_duration` histogram (ms). Tagging is uneven today: the DLQ counter (`deadlettered`) uses `BuildDeadLetterTags` with `transport`, `destination` (normalized via `NormalizeDestination` — GUIDs → `{id}`, long numeric segments → `{n}`), `message_type`, and a `reason` from the fixed taxonomy (`max_retry_exhausted`, `handler_exception`, `deserialization_failure`, `schema_validation_failed`, `transport_reject`). The other counters tag only `message_type` at their current call sites — opportunity for tighter tagging there.
- Prometheus dashboards, DLQ runbook, and KEDA/HPA autoscaling signals are documented in [docs/observability/](docs/observability/) and reference manifests in [docs/deploy/kubernetes/](docs/deploy/kubernetes/).

### Outbox / Inbox

`OutboxPublisherService` is a polling background worker that drains `IOutboxStore` (default: `EfCoreOutboxStore`). Saving to the DB and writing to the outbox must share the same EF Core transaction — `dbContext.SaveChangesAsync()` commits both. **Raw mode** supports anonymous payloads where the consumer doesn't have the CLR type; use it when crossing service boundaries. The Inbox pattern (`Syed.Messaging.Inbox.EfCore`) is the consumer-side counterpart: `GenericMessageConsumer` checks `IInboxStore` for the `MessageId` before invoking the handler to deduplicate at-least-once delivery.

## Conventions

- **Test framework** — xUnit + FluentAssertions + Moq, in-memory EF Core for outbox/inbox/saga store tests. Broker-free unit tests across 7 projects; run `dotnet test` for the current count. Broker-touching tests skip when env vars are unset.
- **Diff philosophy** ([CONTRIBUTING.md](CONTRIBUTING.md)) — minimalism over complexity, composition over inheritance, predictability over magic, consistency across transports. New transport features should land on RabbitMQ, Kafka, and ASB together if they touch the abstraction layer.
- **Roadmap status** — [ROADMAP.md](ROADMAP.md) and [docs/task.md](docs/task.md) sometimes disagree on completed milestones (e.g. ASB hardening, schema registry). Treat `docs/task.md` as more current; reconcile when touching either doc. The active execution plan is [docs/office-hours-quarter-plan.md](docs/office-hours-quarter-plan.md).
- **CONTRIBUTING.md** — currently rendered with escaped Markdown (`\#`, `\*\*`, `&nbsp;`). If editing it, restore proper Markdown — don't preserve the escapes.

## Key references

- [README.md](README.md) — public-facing pitch + quick start (this is the canonical "how to use the library" entry point)
- [docs/architecture_analysis.md](docs/architecture_analysis.md) — design deep-dive, extension-point table, ADRs
- [docs/walkthrough.md](docs/walkthrough.md) and [docs/implementation_plan.md](docs/implementation_plan.md) — historical milestone notes (Type Registry, EF Saga Persistence)
- [docs/observability/dlq-dashboard.md](docs/observability/dlq-dashboard.md) and [autoscaling-signals.md](docs/observability/autoscaling-signals.md) — Prometheus + KEDA/HPA wiring
