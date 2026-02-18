# Syed.Messaging — Roadmap

This roadmap outlines the planned evolution of the Syed.Messaging framework.
It follows a practical, incremental approach: only implement features that improve
real-world developer experience, operational resilience, or architectural clarity.

---

## 📌 Status Overview

| Area                         | Status       | Notes |
|-----------------------------|--------------|-------|
| Abstractions                | ✅ Stable     | Good conceptual clarity; low churn expected |
| RabbitMQ Transport          | ✅ Stable     | Publisher confirms, retry, DLQ, RPC support |
| Kafka Transport             | ⚠️ Minimal    | Needs partitions, tuning, advanced retry |
| Azure Service Bus           | ⚠️ Minimal    | Needs scheduling/backoff + full config |
| Outbox (EF Core)            | ✅ Stable     | Type registry + envelope + raw mode + multi-tenancy |
| Inbox (EF Core)             | ✅ Complete   | Idempotent consumer pattern |
| Versioning Helpers          | ✅ Stable     | `VersionedMessage<T>` + upgrade helpers |
| Message Type Registry       | ✅ Complete   | Safe type resolution with versioning |
| Schema Registry             | ✅ Complete   | Abstraction with validation + compatibility |
| Distributed Tracing         | ✅ Complete   | OpenTelemetry package with activity spans |
| Metrics                     | ✅ Complete   | 7 instruments via System.Diagnostics.Metrics |
| Saga Primitives             | ✅ Stable     | Correlation, timeouts, state management |
| Saga EF Core Persistence    | ✅ Complete   | State + Timeout stores |
| Saga Locking                | ✅ Complete   | InMemory, NoOp, Redis providers |
| RPC Support                 | ✅ Complete   | Request/response messaging pattern |
| SignalR Bridge              | ✅ Complete   | Bridge messaging events to SignalR hubs |
| Health Checks               | ✅ Complete   | ASP.NET Core integration + per-transport checks |
| DLQ Management              | ✅ Complete   | Peek, requeue, purge operations |
| Service Mesh                | ✅ Complete   | Istio/Linkerd/Envoy options, mTLS, traffic policy |
| Service Discovery           | ✅ Complete   | Kubernetes DNS, Consul, standard DNS |
| Resilience (Polly)          | ✅ Complete   | Configurable resilience pipelines |
| Aspire Integration          | ✅ Simple     | Expand with components |

---

# 🧭 Phase 1 — Core Stability ✅ COMPLETE

### 🎯 Goals
- Improve outbox correctness and flexibility
- Add diagnostics and metrics
- Provide comprehensive samples

### Deliverables
- [x] Outbox: configurable type resolver (`IMessageTypeRegistry` instead of `Type.GetType`)
- [x] Outbox: configurable envelope format for metadata + version (`MessageEnvelope` with `MessageVersion`, `Timestamp`)
- [x] Message Type Registry with attribute-based auto-registration
- [x] Type versioning support with fallback resolution
- [x] Unit test coverage (44+ tests)
- [x] Add structured logging scopes (MessageId, CorrelationId) — `GenericMessageConsumer` uses `BeginScope`
- [x] Add metrics counters (messages handled, retries, DLQ) — `MessagingMetrics` with 7 instruments
- [x] RabbitMQ publisher confirms — `ConfirmSelect` + `WaitForConfirmsOrDie`
- [x] Polly resilience pipeline integration — `ResiliencePipelineExtensions`
- [x] Inbox Pattern for idempotent consumers — `Syed.Messaging.Inbox.EfCore`
- [x] RabbitMQ DLQ manager — `RabbitMqDlqManager`
- [x] Poison message detection — auto-DLQ on deserialization failure + max retry threshold
- [ ] Extend RabbitMQ error-handling:
  - max TTL / exponential retry options
- [ ] Kafka: minimal retry-delay mechanism (retry topic per delay)
- [ ] Azure Service Bus: scheduled deferred messages for retries
- [ ] RabbitMQ/Kafka/ASB sample apps

---

## Milestone C: Per-Saga-Instance Locking ✅ COMPLETE
- [x] Design `ISagaLockProvider` interface
- [x] Implement `InMemorySagaLockProvider`
- [x] Implement `NoOpSagaLockProvider`
- [x] Integrate locking into `SagaRuntime`
- [x] Add 8 lock tests (65 total tests)
- [x] Implement `RedisSagaLockProvider` — `Syed.Messaging.Sagas.Redis`

# 🧭 Phase 2 — Saga Engine & State Management ✅ COMPLETE

### 🎯 Goals
Establish a simple orchestration engine for long-running workflows.

### Deliverables
- [x] Saga state store abstraction (`ISagaStateStore<T>`)
- [x] Saga timeout store abstraction (`ISagaTimeoutStore`)
- [x] Saga correlation rules (by payload field)
- [x] Saga timeouts + scheduling
- [x] In-memory stores for demos
- [x] Saga runtime with handler discovery
- [x] EF Core saga state persistence (`EfSagaStateStore`)
- [x] EF Core timeout persistence (`EfSagaTimeoutStore`)
- [x] Saga completion marking
- [x] Per-saga-instance locking (`ISagaLockProvider`)
- [x] Distributed lock for concurrency (Redis) — `RedisSagaLockProvider`
- [ ] Saga replay support
- [ ] End-to-end sample: OrderCreated → ReserveInventory → Payment → Shipping

---

# 🧭 Phase 3 — Distributed Tracing & Instrumentation

### 🎯 Goals
Turn Syed.Messaging into a first-class citizen in modern observability stacks.

### Deliverables
- [x] NuGet: `Syed.Messaging.OpenTelemetry` — package created
- [x] Activity spans:
  - publish/send — `MessagingDiagnostics.PublishActivityName`
  - receive/consume — `MessagingDiagnostics.ConsumeActivityName`
  - handler execution — spans in `GenericMessageConsumer`
- [x] Trace context propagation — `TraceContextPropagation` class
- [ ] Integration examples with:
  - Jaeger
  - Zipkin
  - OpenTelemetry Collector
  - Azure Monitor / AppInsights

---

# 🧭 Phase 4 — Message Protocol Evolution & Versioning

### 🎯 Goals
Support evolving domain models safely.

### Deliverables
- [x] Message type registry (`IMessageTypeRegistry`)
- [x] `MessageTypeAttribute` for declarative type keys
- [x] Add schema registry abstraction — `ISchemaRegistry` with validation + compatibility
- [x] Add `VersionedMessage<T>` helpers — `VersionedMessage<T>`, `Upgrade<TNew>()`, `NeedsUpgrade()`
- [x] Add compatibility rules — `SchemaCompatibilityResult`, `CompatibilityLevel` (Backward/Forward/Full/None)
- [ ] CLI tool (optional): generate schemas from message types

---

# 🧭 Phase 5 — High-Availability & Cloud-Ready Features

### 🎯 Goals
Increase operational robustness.

### Deliverables
- [ ] Partition-aware Kafka consumers
- [ ] Error queues with metrics dashboards
- [ ] Message throughput autoscaling helpers
- [ ] Aspire component for messaging (dashboard integration)
- [ ] Deployment recipes for Kubernetes

---

# 🧭 Phase 6 — Developer Experience Polish

### 🎯 Goals
Provide a premium developer experience.

### Deliverables
- [ ] Templates:
  - `dotnet new syed.worker`
  - `dotnet new syed.api`
- [ ] CLI tool: `syed msg publish`, `syed msg inspect`
- [ ] Visualizer for message flows in Aspire dashboard
- [ ] Official documentation site (mkdocs-material)

---

# 🧭 Long-Term Vision

Syed.Messaging aims to become a **clean, modern, extensible messaging layer** across all major .NET transports:

- RabbitMQ
- Kafka
- Azure Service Bus
- SQL-based transports (future)

With consistent:

- API contracts
- error handling
- message envelopes
- sagas
- and versioning

...so distributed system patterns are **straightforward and safe** for teams.

---

# 🙌 Feedback

Your ideas shape this roadmap.
Feel free to open Discussions / Issues / PRs.

