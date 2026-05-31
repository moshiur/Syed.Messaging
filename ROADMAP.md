# Syed.Messaging — Roadmap

This roadmap outlines the planned evolution of the Syed.Messaging framework.
It follows a practical, incremental approach: only implement features that improve
real-world developer experience, operational resilience, or architectural clarity.

---

## 📌 Status Overview


| Area                     | Status     | Notes                                                            |
| ------------------------ | ---------- | ---------------------------------------------------------------- |
| Abstractions             | ✅ Stable   | Good conceptual clarity; low churn expected                      |
| RabbitMQ Transport       | ✅ Stable   | Publisher confirms, retry, DLQ, RPC support                      |
| Kafka Transport          | ✅ Improved | Partition-aware dispatch, rebalance safety, sample + tests added |
| Azure Service Bus        | ✅ Stable   | Scheduled retry via `ScheduledEnqueueTime`, session support, structured logging, metrics |
| Outbox (EF Core)         | ✅ Stable   | Type registry + envelope + raw mode + multi-tenancy              |
| Inbox (EF Core)          | ✅ Complete | Idempotent consumer pattern                                      |
| Versioning Helpers       | ✅ Stable   | `VersionedMessage<T>` + upgrade helpers                          |
| Message Type Registry    | ✅ Complete | Safe type resolution with versioning                             |
| Schema Registry          | 🟡 In Progress | Abstraction shipped (`ISchemaRegistry`); compat + validation work tracked in [docs/task.md](docs/task.md) Milestone O |
| Distributed Tracing      | ✅ Complete | OpenTelemetry package with activity spans                        |
| Metrics                  | ✅ Complete | 7 instruments via System.Diagnostics.Metrics                     |
| Saga Primitives          | ✅ Stable   | Correlation, timeouts, state management                          |
| Saga EF Core Persistence | ✅ Complete | State + Timeout stores                                           |
| Saga Locking             | ✅ Complete | InMemory, NoOp, Redis providers                                  |
| RPC Support              | ✅ Complete | Request/response messaging pattern                               |
| SignalR Bridge           | ✅ Complete | Bridge messaging events to SignalR hubs                          |
| Health Checks            | ✅ Complete | ASP.NET Core integration + per-transport checks                  |
| DLQ Management           | ✅ Complete | Peek, requeue, purge operations                                  |
| Service Mesh             | ✅ Complete | Istio/Linkerd/Envoy options, mTLS, traffic policy                |
| Service Discovery        | ✅ Complete | Kubernetes DNS, Consul, standard DNS                             |
| Resilience (Polly)       | ✅ Complete | Configurable resilience pipelines                                |
| Aspire Integration       | ✅ Simple   | Expand with components                                           |
| Chaos Engineering        | ✅ New      | `Syed.Messaging.Chaos` — 5 failure shapes, env-gated, prod-safe (v1.3.0) |


---

# 🧭 Phase 1 — Core Stability ✅ COMPLETE

### 🎯 Goals

- Improve outbox correctness and flexibility
- Add diagnostics and metrics
- Provide comprehensive samples

### Deliverables

- Outbox: configurable type resolver (`IMessageTypeRegistry` instead of `Type.GetType`)
- Outbox: configurable envelope format for metadata + version (`MessageEnvelope` with `MessageVersion`, `Timestamp`)
- Message Type Registry with attribute-based auto-registration
- Type versioning support with fallback resolution
- Unit test coverage (44+ tests)
- Add structured logging scopes (MessageId, CorrelationId) — `GenericMessageConsumer` uses `BeginScope`
- Add metrics counters (messages handled, retries, DLQ) — `MessagingMetrics` with 7 instruments
- RabbitMQ publisher confirms — `ConfirmSelect` + `WaitForConfirmsOrDie`
- Polly resilience pipeline integration — `ResiliencePipelineExtensions`
- Inbox Pattern for idempotent consumers — `Syed.Messaging.Inbox.EfCore`
- RabbitMQ DLQ manager — `RabbitMqDlqManager`
- Poison message detection — auto-DLQ on deserialization failure + max retry threshold
- Extend RabbitMQ error-handling:
  - max TTL / exponential retry options
- Kafka: minimal retry-delay mechanism (retry topic per delay)
- Azure Service Bus: scheduled deferred messages for retries
- RabbitMQ/Kafka/ASB sample apps

---

## Milestone C: Per-Saga-Instance Locking ✅ COMPLETE

- Design `ISagaLockProvider` interface
- Implement `InMemorySagaLockProvider`
- Implement `NoOpSagaLockProvider`
- Integrate locking into `SagaRuntime`
- Add 8 lock tests (65 total tests)
- Implement `RedisSagaLockProvider` — `Syed.Messaging.Sagas.Redis`

# 🧭 Phase 2 — Saga Engine & State Management ✅ COMPLETE

### 🎯 Goals

Establish a simple orchestration engine for long-running workflows.

### Deliverables

- Saga state store abstraction (`ISagaStateStore<T>`)
- Saga timeout store abstraction (`ISagaTimeoutStore`)
- Saga correlation rules (by payload field)
- Saga timeouts + scheduling
- In-memory stores for demos
- Saga runtime with handler discovery
- EF Core saga state persistence (`EfSagaStateStore`)
- EF Core timeout persistence (`EfSagaTimeoutStore`)
- Saga completion marking
- Per-saga-instance locking (`ISagaLockProvider`)
- Distributed lock for concurrency (Redis) — `RedisSagaLockProvider`
- Saga replay support
- End-to-end sample: OrderCreated → ReserveInventory → Payment → Shipping

---

# 🧭 Phase 3 — Distributed Tracing & Instrumentation

### 🎯 Goals

Turn Syed.Messaging into a first-class citizen in modern observability stacks.

### Deliverables

- NuGet: `Syed.Messaging.OpenTelemetry` — package created
- Activity spans:
  - publish/send — `MessagingDiagnostics.PublishActivityName`
  - receive/consume — `MessagingDiagnostics.ConsumeActivityName`
  - handler execution — spans in `GenericMessageConsumer`
- Trace context propagation — `TraceContextPropagation` class
- Integration examples with:
  - Jaeger
  - Zipkin
  - OpenTelemetry Collector
  - Azure Monitor / AppInsights

---

# 🧭 Phase 4 — Message Protocol Evolution & Versioning

### 🎯 Goals

Support evolving domain models safely.

### Deliverables

- Message type registry (`IMessageTypeRegistry`)
- `MessageTypeAttribute` for declarative type keys
- Add schema registry abstraction — `ISchemaRegistry` with validation + compatibility
- Add `VersionedMessage<T>` helpers — `VersionedMessage<T>`, `Upgrade<TNew>()`, `NeedsUpgrade()`
- Add compatibility rules — `SchemaCompatibilityResult`, `CompatibilityLevel` (Backward/Forward/Full/None)
- CLI tool (optional): generate schemas from message types

---

# 🧭 Phase 5 — High-Availability & Cloud-Ready Features

### 🎯 Goals

Increase operational robustness.

### Deliverables

- ✅ Partition-aware Kafka consumers
- ✅ Rebalance-safe Kafka consumer flow (revoked partition handling + guarded commit/store)
- ✅ Per-partition ordering with bounded cross-partition concurrency (`MaxConcurrentPartitions`)
- ✅ Kafka partition dispatcher tests (ordering, parallelism, revoke behavior)
- ✅ Kafka sample + docs for partition-key strategy and scaling behavior
- ✅ Error queues with metrics dashboards (standard DLQ tags + Prometheus dashboard/alerts guide)
- ✅ Message throughput autoscaling helpers (retry/DLQ pressure signals + KEDA/HPA reference manifests)
- Aspire component for messaging (dashboard integration)
- Deployment recipes for Kubernetes (broader than autoscaling references)

### Next Concrete Issue

- Expand Aspire component for messaging dashboard integration (metrics, health, DLQ visibility).

---

# 🧭 Phase 6 — Developer Experience Polish

### 🎯 Goals

Provide a premium developer experience.

### Deliverables

- Templates:
  - `dotnet new syed.worker`
  - `dotnet new syed.api`
- CLI tool: `syed msg publish`, `syed msg inspect`
- Visualizer for message flows in Aspire dashboard
- Official documentation site (mkdocs-material)

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