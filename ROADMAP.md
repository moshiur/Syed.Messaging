\# Syed.Messaging — Roadmap



This roadmap outlines the planned evolution of the Syed.Messaging framework.  

It follows a practical, incremental approach: only implement features that improve

real-world developer experience, operational resilience, or architectural clarity.



---



\## 📌 Status Overview



| Area                         | Status       | Notes |

|-----------------------------|--------------|------|

| Abstractions                | ✔ Stable     | Good conceptual clarity; low churn expected |

| RabbitMQ Transport          | ✔ Stable     | Production-ready topology, retry, DLQ |

| Kafka Transport             | ⚠ Minimal    | Needs partitions, tuning, advanced retry |

| Azure Service Bus           | ⚠ Minimal    | Needs scheduling/backoff + full config |

| Outbox (EF Core)            | ⚠ Partial    | Needs type metadata config \& envelopes |

| Versioning Helpers          | ✔ Stable     | Good first implementation |

| Distributed Tracing         | ✔ Solid      | Full OTEL package TBD |

| Saga Primitives             | ⚠ Experimental | Needs orchestration engine |

| Aspire Integration          | ✔ Simple     | Expand with components |



---



\# 🧭 Phase 1 — Core Stability (Current)



\### 🎯 Goals

\- Improve outbox correctness and flexibility

\- Add diagnostics and metrics

\- Provide comprehensive samples



\### Deliverables

\- \[ ] Outbox: configurable type resolver (safe type map instead of `Type.GetType`)

\- \[ ] Outbox: configurable envelope format for metadata + version

\- \[ ] Extend RabbitMQ error-handling including:

&nbsp; - poison message detection

&nbsp; - max TTL / exponential retry options

\- \[ ] Kafka: minimal retry-delay mechanism (retry topic per delay)

\- \[ ] Azure Service Bus: scheduled deferred messages for retries

\- \[ ] Add structured logging scopes (MessageId, CorrelationId)

\- \[ ] Add metrics counters (messages handled, retries, DLQ)

\- \[ ] RabbitMQ/Kafka/ASB sample apps



---



\# 🧭 Phase 2 — Distributed Tracing \& Instrumentation



\### 🎯 Goals

Turn Syed.Messaging into a first-class citizen in modern observability stacks.



\### Deliverables

\- \[ ] NuGet: `Syed.Messaging.OpenTelemetry`

\- \[ ] Activity spans:

&nbsp; - publish/send

&nbsp; - receive/deserialize

&nbsp; - handler execution

\- \[ ] Automatic correlation propagation

\- \[ ] Integration examples with:

&nbsp; - Jaeger

&nbsp; - Zipkin

&nbsp; - OpenTelemetry Collector

&nbsp; - Azure Monitor / AppInsights



---



\# 🧭 Phase 3 — Message Protocol Evolution \& Versioning



\### 🎯 Goals

Support evolving domain models safely.



\### Deliverables

\- \[ ] Add schema registry abstraction

\- \[ ] Add `VersionedMessage<T>` helpers

\- \[ ] Add compatibility rules (e.g., breaking changes detection)

\- \[ ] CLI tool (optional): generate schemas from message types



---



\# 🧭 Phase 4 — Saga Engine \& State Management



\### 🎯 Goals

Establish a simple orchestration engine for long-running workflows.



\### Deliverables

\- \[ ] Saga persistence (EF Core + Redis options)

\- \[ ] Saga correlation rules:

&nbsp; - by header

&nbsp; - by payload field

\- \[ ] Saga timeouts + reminders

\- \[ ] Saga replay support

\- \[ ] Distributed lock for concurrency

\- \[ ] End-to-end sample for:

&nbsp; - OrderCreated → ReserveInventory → Payment → Shipping



---



\# 🧭 Phase 5 — High-Availability \& Cloud-Ready Features



\### 🎯 Goals

Increase operational robustness.



\### Deliverables

\- \[ ] Partition-aware Kafka consumers

\- \[ ] Error queues with metrics dashboards

\- \[ ] Message throughput autoscaling helpers

\- \[ ] Aspire component for messaging (dashboard integration)

\- \[ ] Deployment recipes for Kubernetes



---



\# 🧭 Phase 6 — Developer Experience Polish



\### 🎯 Goals

Provide a premium developer experience.



\### Deliverables

\- \[ ] Templates:

&nbsp; - `dotnet new syed.worker`

&nbsp; - `dotnet new syed.api`

\- \[ ] CLI tool: `syed msg publish`, `syed msg inspect`

\- \[ ] Visualizer for message flows in Aspire dashboard

\- \[ ] Official documentation site (mkdocs-material)



---



\# 🧭 Long-Term Vision



Syed.Messaging aims to become a \*\*clean, modern, extensible messaging layer\*\* across all major .NET transports:



\- RabbitMQ  

\- Kafka  

\- Azure Service Bus  

\- SQL-based transports (future)  



With consistent:



\- API contracts  

\- error handling  

\- message envelopes  

\- sagas  

\- and versioning  



...so distributed system patterns are \*\*straightforward and safe\*\* for teams.



---



\# 🙌 Feedback



Your ideas shape this roadmap.  

Feel free to open Discussions / Issues / PRs.





