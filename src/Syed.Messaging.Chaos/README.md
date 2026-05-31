# Syed.Messaging.Chaos

**Chaos engineering for your message handlers — on by default in dev, refused in production.**

Most messaging libraries only ever show your handlers the happy path in dev. That's why your production incidents are always a surprise: the duplicate delivery, the dropped message, the slow consumer, the lost ack. `Syed.Messaging.Chaos` injects those failures into your consumed messages *in development*, so the bugs surface where they're a five-second fix instead of a 2am page.

```csharp
services.AddMessaging(m =>
{
    m.UseRabbitMq(o => o.ConnectionString = "amqp://localhost");
    m.AddConsumer<OrderCreated, OrderCreatedHandler>(c => c.Destination = "orders.created");
    m.EnableChaos();   // that's it
});
```

Then turn it on for a run:

```bash
SYED_CHAOS_LEVEL=medium dotnet run
```

```
warn: [CHAOS:duplicate] Invoking the handler for orders.created TWICE to test idempotency.
      If your handler isn't safe to call twice, that's a real bug production will eventually hit.
```

See it without any setup: [`samples/ChaosDemo`](../../samples/ChaosDemo) runs broker-free and reports the bugs chaos found.

## How it works

`EnableChaos()` adds a middleware to the existing Syed.Messaging consumer pipeline. For each message, it rolls a weighted die based on the configured level; if chaos fires, it applies one shape and logs a `[CHAOS:shape]` line explaining what it did and why.

| Level | Injection rate | Use for |
|:--|:--|:--|
| `Off` (default) | 0% | The default. Nothing happens until you opt in. |
| `Low` | ~1% | Light background pressure in a shared dev environment. |
| `Medium` | ~5% | The recommended dev/staging default. |
| `High` | ~15% | CI / stress runs. |

The level comes from `ChaosOptions.Level` in code **or** the `SYED_CHAOS_LEVEL` environment variable (which overrides code, so ops can dial chaos without a redeploy).

## The shapes

Five failure shapes ship in v1.3.0. Each maps to a real production failure mode.

| Shape | What it does | The bug it finds |
|:--|:--|:--|
| `Drop` | Silently drops the message; the handler never runs. | Lost-message gaps; missing upstream retry. |
| `Duplicate` | Invokes the handler twice for the same message. | Non-idempotent handlers (double-charge, double-insert). |
| `Delay` | Delays delivery by a random interval (≤ `MaxDelayInjected`). | Timeouts, backpressure, ordering assumptions. |
| `HeaderCorruption` | Adds a junk header (additive only — never mutates existing headers). | Handlers that assume a fixed header set. |
| `AckTimeout` | Runs the handler successfully, then throws — simulating a lost ack. | Retry paths that aren't safe to replay an already-processed message. |

Restrict the set with a mask:

```csharp
m.EnableChaos(o =>
{
    o.Level = ChaosLevel.Medium;
    o.EnabledShapes = ChaosShape.Drop | ChaosShape.Delay;  // skip the rest
});
```

### Shape-safety matrix — is each shape safe for *your* handler?

| Shape | Always safe? | Notes |
|:--|:--|:--|
| `Drop` | ✅ | Worst case is a message your system should already tolerate losing. |
| `Delay` | ✅ | Runs inside the consumer scope; just slower. |
| `HeaderCorruption` | ✅ | Additive only; existing headers (including `message-type`) are untouched. |
| `AckTimeout` | ⚠️ | Your handler runs, then chaos throws → your retry path replays it. Safe **iff** your handler is idempotent or you use the inbox. |
| `Duplicate` | ⚠️ | Invokes your handler twice. **Auto-skipped when an `IInboxStore` is registered** (the inbox would dedupe it, and double-invocation before the inbox mark is unsafe). For non-idempotent handlers without an inbox, this is exactly the bug it's designed to expose. |

If your handlers aren't idempotent and you haven't wired the inbox yet, that's *the point* — let `Duplicate` and `AckTimeout` show you before production does.

## Production safety

Chaos **refuses to run in production** unless you explicitly allow it. When `ASPNETCORE_ENVIRONMENT=Production`, chaos is disabled and logs a one-time `[CHAOS:refused]` line at error level — even if `SYED_CHAOS_LEVEL` is set. To run a deliberate game-day:

```bash
ASPNETCORE_ENVIRONMENT=Production SYED_CHAOS_PROD=true SYED_CHAOS_LEVEL=low dotnet run
```

or in code: `o.ProductionAllowed = true`. When chaos *is* active in production it logs a loud `[CHAOS:engaged]` warning so it's never a silent surprise.

## Observability

Chaos emits its own meter — **`Syed.Messaging.Chaos`**, separate from the core `Syed.Messaging` meter — so chaos injections never pollute the `messaging.messages.failed` counter your SRE alerts watch.

```csharp
services.AddOpenTelemetry().WithMetrics(b => b.AddMeter("Syed.Messaging.Chaos"));
```

The `messaging.chaos.injected` counter is tagged with `chaos.shape` and `message_type`.

## Determinism for tests

Set `ChaosOptions.Seed` to reproduce a chaos-found bug in a test — the same seed yields the same shape sequence. The injector is thread-safe (each consumer thread gets its own deterministic RNG).

## Custom injectors

Replace the default shape probabilities with your own:

```csharp
m.EnableChaos(o => o.UseInjector<MyChaosInjector>());   // implements IChaosInjector
```

## Configuration reference

| Option | Default | Meaning |
|:--|:--|:--|
| `Level` | `Off` | Injection intensity. `SYED_CHAOS_LEVEL` overrides. |
| `EnabledShapes` | `All` | Bitmask of eligible shapes. |
| `Seed` | `null` | Deterministic RNG seed for reproducible runs. |
| `MaxDelayInjected` | `30s` | Upper bound for the `Delay` shape. |
| `ProductionAllowed` | `false` | Allow chaos in `Production`. `SYED_CHAOS_PROD=true` does the same. |

## License

MIT, same as the rest of [Syed.Messaging](https://github.com/moshiur/Syed.Messaging).
