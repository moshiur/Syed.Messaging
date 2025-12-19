# Milestone A: Type Registry + Envelope

Introduce a safe, versioned message type registry to replace `Type.GetType()` and `AssemblyQualifiedName` usage throughout the codebase. This enables safe deserialization for Outbox, Saga Timeouts, and future cross-service messaging.

## Problem Statement

The current implementation has several safety and portability issues:

1. **`SagaTimeoutDispatcher`** (line 177) uses `Type.GetType(t.TimeoutMessageType)` which:
   - Fails silently if assemblies are refactored
   - Is a security vector (loading arbitrary types)
   - Breaks across service boundaries

2. **`SagaTimeoutScheduler`** (lines 111-112) stores `AssemblyQualifiedName` which:
   - Is fragile across deployments
   - Contains assembly version info that changes

3. **`OutboxMessage`** already has `MessageType` + `Version` fields but they're not used consistently

4. **Transports** set `MessageType` on publish but the value varies (sometimes CLR name, sometimes simple name)

---

## User Review Required

> [!IMPORTANT]
> **Breaking Change**: The `SagaTimeoutRequest.SagaType` and `TimeoutMessageType` will change from `AssemblyQualifiedName` to type keys. Existing persisted timeouts will need migration or will fail to dispatch.

> [!IMPORTANT]
> **Design Decision**: Should `IMessageTypeRegistry` be in `Syed.Messaging.Abstractions` or `Syed.Messaging.Core`? I recommend **Abstractions** since transports and outbox need it.

---

## Proposed Changes

### Syed.Messaging.Abstractions

#### [NEW] [IMessageTypeRegistry.cs](file:///d:/git/syed-messaging/Syed.Messaging/src/Syed.Messaging.Abstractions/IMessageTypeRegistry.cs)

```csharp
namespace Syed.Messaging;

/// <summary>
/// Safe registry mapping message type keys to CLR types.
/// Replaces Type.GetType() for safe, versionable deserialization.
/// </summary>
public interface IMessageTypeRegistry
{
    void Register<TMessage>(string typeKey, string? version = null);
    void Register(Type messageType, string typeKey, string? version = null);
    
    Type? Resolve(string typeKey, string? version = null);
    (string TypeKey, string? Version) GetTypeKey(Type messageType);
    (string TypeKey, string? Version) GetTypeKey<TMessage>();
    
    bool TryResolve(string typeKey, string? version, out Type? type);
}
```

#### [MODIFY] [IMessageEnvelope.cs](file:///d:/git/syed-messaging/Syed.Messaging/src/Syed.Messaging.Abstractions/IMessageEnvelope.cs)

Add `MessageVersion` property and `Timestamp` to the interface:
```diff
 public interface IMessageEnvelope
 {
     string MessageType { get; }
+    string? MessageVersion { get; }
     string? MessageId { get; }
     string? CorrelationId { get; }
     string? CausationId { get; }
+    DateTimeOffset Timestamp { get; }
     IDictionary<string, string> Headers { get; }
     byte[] Body { get; }
 }
```

---

### Syed.Messaging.Core

#### [NEW] [MessageTypeRegistry.cs](file:///d:/git/syed-messaging/Syed.Messaging/src/Syed.Messaging.Core/MessageTypeRegistry.cs)

Default implementation with:
- Dictionary-based lookup: `(typeKey, version) -> Type`
- Reverse lookup: `Type -> (typeKey, version)`
- Thread-safe reads, mutable during startup
- Auto-registration support via `[MessageType]` attribute

#### [NEW] [MessageTypeAttribute.cs](file:///d:/git/syed-messaging/Syed.Messaging/src/Syed.Messaging.Core/MessageTypeAttribute.cs)

```csharp
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class MessageTypeAttribute : Attribute
{
    public string TypeKey { get; }
    public string? Version { get; }
    
    public MessageTypeAttribute(string typeKey, string? version = null)
    {
        TypeKey = typeKey;
        Version = version;
    }
}
```

#### [MODIFY] [ISerializer.cs](file:///d:/git/syed-messaging/Syed.Messaging/src/Syed.Messaging.Core/ISerializer.cs)

No changes needed - already has `Deserialize(byte[], Type)`.

#### [MODIFY] [MessagingServiceCollectionExtensions.cs](file:///d:/git/syed-messaging/Syed.Messaging/src/Syed.Messaging.Core/MessagingServiceCollectionExtensions.cs)

Register `IMessageTypeRegistry` as singleton.

---

### Syed.Messaging.Sagas

#### [MODIFY] [SagaTimeouts.cs](file:///d:/git/syed-messaging/Syed.Messaging/src/Syed.Messaging.Sagas/SagaTimeouts.cs)

**`SagaTimeoutRequest`** - change fields to use type keys:
```diff
 public sealed class SagaTimeoutRequest
 {
     public Guid Id { get; set; }
-    public string SagaType { get; set; } = default!;
-    public string TimeoutMessageType { get; set; } = default!;
+    public string SagaTypeKey { get; set; } = default!;
+    public string TimeoutTypeKey { get; set; } = default!;
+    public string? TimeoutTypeVersion { get; set; }
     // ... rest unchanged
 }
```

**`SagaTimeoutScheduler`** - use registry to get type keys:
```diff
-SagaType = sagaType.AssemblyQualifiedName ?? sagaType.FullName ?? sagaType.Name,
-TimeoutMessageType = typeof(TTimeout).AssemblyQualifiedName ?? typeof(TTimeout).FullName ?? typeof(TTimeout).Name,
+var (typeKey, version) = _registry.GetTypeKey<TTimeout>();
+SagaTypeKey = sagaType.FullName!,  // saga types don't need registry for now
+TimeoutTypeKey = typeKey,
+TimeoutTypeVersion = version,
```

**`SagaTimeoutDispatcher`** - use registry to resolve types:
```diff
-var type = Type.GetType(t.TimeoutMessageType);
+var type = _registry.Resolve(t.TimeoutTypeKey, t.TimeoutTypeVersion);
```

---

### Syed.Messaging.Outbox.EfCore

#### [MODIFY] [OutboxPublisherService.cs](file:///d:/git/syed-messaging/Syed.Messaging/src/Syed.Messaging.Outbox.EfCore/OutboxPublisherService.cs)

Currently publishes from outbox - ensure `MessageType` and `Version` headers are set consistently.

---

### Transports (RabbitMQ, Kafka, Azure Service Bus)

#### [MODIFY] Transport publish methods

Set `message-type` and `message-version` headers consistently using registry:
```csharp
var (typeKey, version) = _registry.GetTypeKey(message.GetType());
props.Type = typeKey;
headers["message-type"] = typeKey;
headers["message-version"] = version ?? "1";
```

---

## Test Projects (NEW)

Create comprehensive unit test projects to cover existing and new functionality.

### [NEW] Syed.Messaging.Tests

New xUnit test project in `tests/Syed.Messaging.Tests/`.

**Dependencies**:
- xUnit 2.6+
- Moq 4.20+
- FluentAssertions 6.12+
- Microsoft.NET.Test.Sdk

#### Test Classes:

| Test Class | Covers | Key Tests |
|------------|--------|----------|
| `MessageTypeRegistryTests` | `MessageTypeRegistry` | Register/resolve types, versioning, missing types, duplicate registration |
| `MessageEnvelopeTests` | `MessageEnvelope` | Property initialization, header manipulation |
| `SystemTextJsonSerializerTests` | `SystemTextJsonSerializer` | Serialize/deserialize, type-based deserialization |
| `MessageVersioningTests` | `MessageVersioning` | Set/get version headers, defaults |
| `GenericMessageConsumerTests` | `GenericMessageConsumer` | Envelope handling, context building, retry logic, DLQ |
| `RetryPolicyTests` | `RetryPolicy` | Max retries behavior |

---

### [NEW] Syed.Messaging.Sagas.Tests

New xUnit test project in `tests/Syed.Messaging.Sagas.Tests/`.

#### Test Classes:

| Test Class | Covers | Key Tests |
|------------|--------|----------|
| `SagaRuntimeTests` | `SagaRuntime` | Handle message, correlation, state load/save, missing saga |
| `SagaTimeoutSchedulerTests` | `SagaTimeoutScheduler` | Schedule timeout with type key, cancel timeout |
| `SagaTimeoutDispatcherTests` | `SagaTimeoutDispatcher` | Dispatch due timeouts, resolve via registry, handle cancelled |
| `InMemorySagaTimeoutStoreTests` | `InMemorySagaTimeoutStore` | Schedule, cancel, get due, mark processed |
| `InMemorySagaStateStoreTests` | `InMemorySagaStateStore` | Load, save, missing state |
| `SagaBuilderTests` | `SagaBuilder` | Correlation config, starts new saga flag |

---

### [NEW] Syed.Messaging.Outbox.Tests

New xUnit test project in `tests/Syed.Messaging.Outbox.Tests/`.

#### Test Classes:

| Test Class | Covers | Key Tests |
|------------|--------|----------|
| `EfCoreOutboxStoreTests` | `EfCoreOutboxStore` | Save, get pending, mark processed (with in-memory EF) |
| `OutboxPublisherServiceTests` | `OutboxPublisherService` | Publish pending, set headers, handle errors |

---

### [NEW] Syed.Messaging.RabbitMq.Tests

New xUnit test project in `tests/Syed.Messaging.RabbitMq.Tests/`.

#### Test Classes:

| Test Class | Covers | Key Tests |
|------------|--------|----------|
| `RabbitMqTransportTests` | `RabbitMqTransport` | Publish sets headers, envelope conversion |

---

### Test Coverage Matrix

```
┌─────────────────────────────────────┬──────────────┬─────────────┐
│ Component                           │ Unit Tests   │ Integration │
├─────────────────────────────────────┼──────────────┼─────────────┤
│ IMessageTypeRegistry                │ ✅ Full      │ -           │
│ MessageEnvelope                     │ ✅ Full      │ -           │
│ ISerializer                         │ ✅ Full      │ -           │
│ GenericMessageConsumer              │ ✅ Mocked    │ Demo        │
│ SagaRuntime                         │ ✅ Mocked    │ Demo        │
│ SagaTimeoutScheduler/Dispatcher     │ ✅ Full      │ Demo        │
│ InMemorySagaTimeoutStore            │ ✅ Full      │ -           │
│ EfCoreOutboxStore                   │ ✅ InMemory  │ -           │
│ RabbitMqTransport                   │ ⚠️ Partial   │ Manual      │
│ KafkaTransport                      │ ⚠️ Partial   │ Manual      │
│ AzureServiceBusTransport            │ ⚠️ Partial   │ Manual      │
└─────────────────────────────────────┴──────────────┴─────────────┘
```

> [!NOTE]
> Transport tests are partial because they require mocking broker clients. Full integration tests require running brokers.

---

## Verification Plan

### Automated Tests

1. **Run all unit tests**:
   ```powershell
   dotnet test d:\git\syed-messaging\Syed.Messaging\Syed.Messaging.sln --verbosity normal
   ```
   Expect: All tests pass.

2. **Code coverage report**:
   ```powershell
   dotnet test --collect:"XPlat Code Coverage" --results-directory ./coverage
   ```
   Target: >80% coverage on Core, Abstractions, and Sagas.

### Build Verification

```powershell
dotnet build d:\git\syed-messaging\Syed.Messaging\Syed.Messaging.sln -c Release
```
Expect: Build succeeds with no errors.

### Integration Test (Manual)

- Run `samples/OrderSagaDemo`
- Verify messages have `message-type` and `message-version` headers
- Verify saga timeouts dispatch correctly using type keys

---

## File Summary

| Component | File | Change |
|-----------|------|--------|
| **Abstractions** | `IMessageTypeRegistry.cs` | **NEW** - Registry interface |
| Abstractions | `IMessageEnvelope.cs` | Add `MessageVersion`, `Timestamp` |
| **Core** | `MessageTypeRegistry.cs` | **NEW** - Default implementation |
| Core | `MessageTypeAttribute.cs` | **NEW** - Attribute for auto-registration |
| Core | `MessagingServiceCollectionExtensions.cs` | Register registry |
| **Sagas** | `SagaTimeouts.cs` | Use registry for type resolution |
| **Outbox** | `OutboxPublisherService.cs` | Set version headers consistently |
| **RabbitMQ** | `RabbitMqTransport.cs` | Use registry for type keys |
| Kafka | `KafkaTransport.cs` | Use registry for type keys |
| Azure | `AzureServiceBusTransport.cs` | Use registry for type keys |
| **Tests** | `tests/Syed.Messaging.Tests/` | **NEW** - Core unit tests |
| Tests | `tests/Syed.Messaging.Sagas.Tests/` | **NEW** - Saga unit tests |
| Tests | `tests/Syed.Messaging.Outbox.Tests/` | **NEW** - Outbox unit tests |
| Tests | `tests/Syed.Messaging.RabbitMq.Tests/` | **NEW** - RabbitMQ unit tests |
