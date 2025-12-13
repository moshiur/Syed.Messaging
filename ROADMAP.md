# Syed.Messaging — Roadmap

This roadmap outlines the planned evolution of the Syed.Messaging framework.
It follows a practical, incremental approach: only implement features that improve
real-world developer experience, operational resilience, or architectural clarity.

---

## 📌 Status Overview

| Area                         | Status       | Notes |
|-----------------------------|--------------|-------|
| Abstractions                | ✅ Stable     | Good conceptual clarity; low churn expected |
| RabbitMQ Transport          | ✅ Stable     | Production-ready topology, retry, DLQ |
| Kafka Transport             | ⚠️ Minimal    | Needs partitions, tuning, advanced retry |
| Azure Service Bus           | ⚠️ Minimal    | Needs scheduling/backoff + full config |
| Outbox (EF Core)            | ✅ Stable     | Type registry + envelope support |
| Versioning Helpers          | ✅ Stable     | Good first implementation |
| Message Type Registry       | ✅ Complete   | Safe type resolution with versioning |
| Distributed Tracing         | ✅ Solid      | Full OTEL package TBD |
| Saga Primitives             | ✅ Stable     | Correlation, timeouts, state management |
| Saga EF Core Persistence    | 🚧 In Progress | State + Timeout stores |
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
- [ ] Extend RabbitMQ error-handling including:
  - poison message detection
  - max TTL / exponential retry options
- [ ] Kafka: minimal retry-delay mechanism (retry topic per delay)
- [ ] Azure Service Bus: scheduled deferred messages for retries
- [ ] Add structured logging scopes (MessageId, CorrelationId)
- [ ] Add metrics counters (messages handled, retries, DLQ)
- [ ] RabbitMQ/Kafka/ASB sample apps

---

## Milestone C: Per-Saga-Instance Locking ✅ COMPLETE
- [x] Design `ISagaLockProvider` interface
- [x] Implement `InMemorySagaLockProvider`
- [x] Implement `NoOpSagaLockProvider`
- [x] Integrate locking into `SagaRuntime`
- [x] Add 8 lock tests (65 total tests)
- [ ] (Optional) Implement `RedisSagaLockProvider`

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
- [ ] Distributed lock for concurrency (Redis - optional)
- [ ] Saga replay support
- [ ] End-to-end sample: OrderCreated → ReserveInventory → Payment → Shipping

---

# 🧭 Phase 3 — Distributed Tracing & Instrumentation

### 🎯 Goals
Turn Syed.Messaging into a first-class citizen in modern observability stacks.

### Deliverables
- [ ] NuGet: `Syed.Messaging.OpenTelemetry`
- [ ] Activity spans:
  - publish/send
  - receive/deserialize
  - handler execution
- [ ] Automatic correlation propagation
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
- [ ] Add schema registry abstraction
- [ ] Add `VersionedMessage<T>` helpers
- [ ] Add compatibility rules (e.g., breaking changes detection)
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

