# Architecture Analysis & Future Roadmap

## 1. Current Architecture Overview

**Syed.Messaging** is a modular, message-driven framework designed for building distributed systems with .NET. It embraces patterns like Outbox, Sagas (Process Manager), and specific message types (Events, Commands) to ensure reliability and loose coupling.

### Key Components

*   **Abstractions (`Syed.Messaging.Abstractions`)**: Defines the core contracts (`IMessageBus`, `IMessageHandler`, `IMessageEnvelope`, `IMessageTypeRegistry`). This layer has zero dependencies on concrete implementations.
*   **Core (`Syed.Messaging.Core`)**: Provides the default implementations and plumbing (`MessageBus`, `MessageTypeRegistry`, `SystemTextJsonSerializer`).
*   **Transport (`Syed.Messaging.RabbitMq`)**: Implements `IMessageTransport` for RabbitMQ.
*   **Outbox (`Syed.Messaging.Outbox.EfCore`)**: Provides reliable message publishing using the Transactional Outbox pattern with EF Core.
*   **Sagas (`Syed.Messaging.Sagas`)**: A lightweight saga orchestration engine.
    *   **Persistence**: `Syed.Messaging.Sagas.EfCore` (Sqlite/SQL Server via EF Core).
    *   **Locking**: `Syed.Messaging.Sagas.Redis` (Distributed locking).
*   **Observability (`Syed.Messaging.OpenTelemetry`)**: tailored OpenTelemetry instrumentation.

## 2. Design Patterns & Principles

*   **Transactional Outbox**: Decouples the database transaction from the message broker publishing to ensure atomicity.
*   **Saga Pattern (Orchestration)**: Manages long-running business transactions using a state machine approach.
*   **Optimistic Concurrency**: specific to Sagas, creating a versioned state to prevent lost updates.
*   **Distributed Locking**: Ensures only one worker processes a specific saga instance at a time.
*   **Type Registry & Versioning**: Decouples the .NET type name from the message "topic" or contract, allowing safe refactoring and versioning of messages.

## 3. Extension Points

The framework is designed to be extensible via Dependency Injection.

| Extension Point | Interface | Description |
| :--- | :--- | :--- |
| **Serialization** | `ISerializer` | Replace `System.Text.Json` with Protobuf, MessagePack, etc. |
| **Transport** | `IMessageTransport` | Add support for Azure Service Bus, Kafka, Amazon SQS. |
| **Outbox Storage** | `IOutboxStore` | Implement for Dapper, MongoDB, or other databases. |
| **Saga Storage** | `ISagaStateStore<T>` | Store saga state in Redis, CosmosDB, Mongo, etc. |
| **Saga Locking** | `ISagaLockProvider` | Implement locking via ZooKeeper, Postgres Advisory Locks, etc. |
| **Type Resolution** | `IMessageTypeRegistry` | Customize how message types are resolved (e.g., centralized schema registry). |

## 4. Gaps & Limitations

1.  **Transport Features**:
    *   RabbitMQ implementation is basic. No support for publisher confirms or advanced topology configuration (e.g. topic exchanges vs fanout).
    *   No Dead Letter Queue (DLQ) automated management or replay mechanism.
2.  **Saga Features**:
    *   **Replay/Compensation**: No built-in support for compensating transactions (undoing steps) if a saga fails.
    *   **Visualizer**: No UI to visualize the saga flow or current state of running sagas.
    *   **Timeouts**: Timeout processing is currently polling-based.
3.  **Outbox**:
    *   The "relay" is likely a background worker polling the DB. This can be inefficient at scale (busy wait or high latency). Needs support for CDC (Change Data Capture) or immediate triggers.
4.  **Error Handling**:
    *   Retry policies are rudimentary. Needs a more robust solution like Polly integration for exponential backoff at the handler level.
    *   "Poison message" handling is manual.

## 5. Future Roadmap

### Short Term (v1.1)
*   **[ ] Transport Hardening**: Implement RabbitMQ Publisher Confirms and better connection recovery.
*   **[ ] Polly Integration**: Add `AddResiliencePipeline` support to message handlers.
*   **[ ] Inbox Pattern**: Implement Idempotent Consumer (Inbox) pattern to deduplicate messages.

### Medium Term (v1.2)
*   **[ ] Azure Service Bus Transport**: Add support for ASB.
*   **[ ] Saga Compensation**: Add `CompensateAsync` to `ISagaHandler` and state machine logic for rolling back.
*   **[ ] CLI Tool**: A generic CLI to inspect the Type Registry, replay messages, or manage lock releases manually.

### Long Term (v2.0)
*   **[ ] Multi-Tenancy**: Native support for tenant isolation in Transport and Stores.
*   **[ ] Visual Dashboard**: A Blazor/Aspire dashboard to view Outbox lag, Saga states, and Transport stats.

## 6. Architecture Decision Records (ADRs)

*   **ADR-001**: **Use of `IMessageTypeRegistry`**.
    *   *Context*: .NET Types are brittle contracts. Renaming a class breaks deserialization for consumers.
    *   *Decision*: Use logical string keys (`orders.created:v1`) mapped to CLR types.
    *   *Consequences*: Requires registration step (Attribute or Fluent API), but enables safe refactoring.

*   **ADR-002**: **EF Core for Saga Persistence**.
    *   *Decision*: Use EF Core with a generic JSON column for State.
    *   *Rationale*: Flexibility. Saga state schemas change frequently. JSON allows evolution without complex EF migrations for every property. concurrency token is lifted to a column.

*   **ADR-003**: **Distributed Locking for Sagas**.
    *   *Decision*: Implement explicit `ISagaLockProvider` in the runtime execution pipeline.
    *   *Rationale*: Prevents race conditions where two events trigger the same saga instance simultaneously, leading to version conflicts or state corruption.
