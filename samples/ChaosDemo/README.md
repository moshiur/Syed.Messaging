# ChaosDemo

A broker-free, zero-setup demonstration of [`Syed.Messaging.Chaos`](../../src/Syed.Messaging.Chaos). No RabbitMQ, no Kafka, no Docker — just run it.

```bash
dotnet run --project samples/ChaosDemo
```

## What it does

It pumps 150 orders through the **real** `ChaosMiddleware` against a deliberately **non-idempotent** payment handler — one that charges a customer on every call, with no dedup. A correct handler would be safe to call twice; this one isn't. That's the point.

Chaos exposes the bug in about five seconds:

```
══════════════════════════════════════════════════════════════
  CHAOS REPORT — what your non-idempotent handler actually did
══════════════════════════════════════════════════════════════
  Orders sent:              150
  Distinct orders charged:  146
  Total charge events:      150  ($7350.00)
  Expected if correct:      150 charges ($7350.00)
  ──────────────────────────────────────────────────────────
  🐞 DOUBLE-CHARGED orders:  4   ← Duplicate / AckTimeout exposed non-idempotency
  🐞 NEVER-CHARGED orders:   4   ← Drop exposed a lost-message gap
══════════════════════════════════════════════════════════════
```

Notice the trap: total revenue matches the expected $7350 exactly, so your dashboards look clean — yet four customers were double-charged and four orders silently vanished. The aggregate hid the bug. Chaos surfaced it in dev, where it's a 5-second fix instead of a 2am incident.

## Tune it

The demo defaults to `ChaosLevel.High` with a seeded RNG (reproducible output) and a 200ms delay cap (so it runs fast). Override the level without touching code:

```bash
SYED_CHAOS_LEVEL=medium dotnet run --project samples/ChaosDemo   # ~5% injection
SYED_CHAOS_LEVEL=off    dotnet run --project samples/ChaosDemo   # clean run, no chaos
```

## How a real consumer uses it

In a real worker you don't construct the middleware by hand — you opt in on the messaging builder:

```csharp
services.AddMessaging(m =>
{
    m.UseRabbitMq(o => o.ConnectionString = "amqp://localhost");
    m.AddConsumer<OrderCreated, OrderCreatedHandler>(c => c.Destination = "orders.created");
    m.EnableChaos();   // off until SYED_CHAOS_LEVEL is set; refused in Production by default
});
```

See the [package README](../../src/Syed.Messaging.Chaos/README.md) for the full shape-safety matrix and the production-safety gate.
