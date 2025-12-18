# Saga Improvement Roadmap

## Milestone A: Type Registry + Envelope ✅ COMPLETE
- [x] `IMessageTypeRegistry` for safe type resolution
- [x] `MessageTypeAttribute` for declarative type keys
- [x] Updated Outbox/Timeouts/Transports to use registry
- [x] Test projects (44 tests)

## Milestone B: EF Core Saga Persistence ✅ COMPLETE
- [x] `Syed.Messaging.Sagas.EfCore` package
- [x] `EfSagaStateStore` with JSON serialization + optimistic concurrency
- [x] `EfSagaTimeoutStore` for persistent timeouts
- [x] OrderSagaDemo using SQLite

## Milestone C: Per-Saga-Instance Locking ✅ COMPLETE
- [x] `ISagaLockProvider` interface
- [x] `InMemorySagaLockProvider` (single-instance)
- [x] `NoOpSagaLockProvider` (bypass)
- [x] `RedisSagaLockProvider` (distributed)
- [x] Locking tests (8 tests)

## Milestone D: OpenTelemetry Instrumentation ✅ COMPLETE
- [x] `Syed.Messaging.OpenTelemetry` package
- [x] `MessagingActivitySource` with spans for publish/receive/process/saga
- [x] `TraceContextPropagation` for W3C trace context
- [x] `AddSyedMessagingInstrumentation()` extension

## Milestone E: Design Analysis & Future Functionalities ✅ COMPLETE
- [x] Analyse current architecture and patterns
- [x] Document extension points and customization options
- [x] Identify gaps and improvement opportunities
- [x] Create future roadmap with prioritized enhancements
- [x] Write architecture decision records (ADRs)

# Phase 3: Reliability & Resilience (Roadmap v1.1)

## Milestone F: Transport Hardening (Current) ✅ COMPLETE
- [x] Implement RabbitMQ Publisher Confirms
- [/] Improve connection recovery logic (Built-in to client)
- [ ] Add Dead Letter Queue (DLQ) management utilities

## Milestone H: Unit Tests ✅ COMPLETE
- [x] RabbitMQ Publisher Confirms & Resilience Tests
- [x] Full Solution Test Run (68/68 passed)

## Milestone G: Resilience Patterns ✅ COMPLETE
- [x] Integrate Polly for message handler retries
- [x] Implement Idempotent Consumer (Inbox) pattern

## Milestone I: Inbox Pattern ✅ COMPLETE
- [x] IInboxStore interface
- [x] EfInboxStore implementation
- [x] GenericMessageConsumer integration (De-duplication)
- [x] Unit Tests & Demo Integration

## Test Summary: 65 Tests Passing
