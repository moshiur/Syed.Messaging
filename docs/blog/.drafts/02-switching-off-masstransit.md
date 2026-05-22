---
title: "Switching off MassTransit ahead of v9: what it cost us, what we got back"
date: [FILL IN: YYYY-MM-DD — publish 1-2 weeks AFTER the chaos-by-default post]
status: DRAFT — DO NOT PUBLISH UNTIL FILL-IN MARKERS RESOLVED
author: [FILL IN: your name / handle]
sequence: Phase 1 post #2 (after the chaos-by-default post)
---

> **Draft note for the maintainer:** this is the *second* post of the Phase 1
> launch arc. The first post is "Why my .NET messaging library breaks 5% of your
> messages by default" — publish that first, then this one 1-2 weeks later as the
> follow-on traffic wave. Fix every `[FILL IN]` marker and remove every `[VERIFY]`
> note before shipping. Do not let this go public with the placeholders intact.

MassTransit v8 is still Apache 2.0. v9 isn't — it ships commercially under [Massient](https://massient.com). That gave my team a runway: migrate on our schedule, or wait until v8 stops getting updates and negotiate a commercial license under deadline pressure. We picked the runway. This is what it cost.

I am not here to dunk on Chris Patterson. MassTransit is a serious piece of software and the v9 commercial direction is a reasonable business decision — Apache 2.0 OSS infrastructure work has been historically under-monetized. It just was not a path my team wanted to be locked into, and "wait and see" is a worse posture than "have a plan."

## Why we left

The math was simple. We run [FILL IN: number of services, e.g. "11 services across 3 product lines"] that publish or consume on RabbitMQ and Azure Service Bus. The MassTransit v9 commercial tier we would have eventually landed on was about [FILL IN: $/year tier you projected, e.g. "$X,000/yr"] once you account for [FILL IN: team size / production usage / support tier]. Not catastrophic. Just enough that it triggered the "what else is out there" conversation, and once that conversation starts it does not stop.

What MassTransit does well, in fairness:

- Deep feature set. Automatonymous, scheduled messages, courier, request/response, the works.
- Big community. If you have a weird Rabbit topology question, somebody has filed it.
- Polished docs and a stable API surface that has held up for years.

Why we still moved:

- MIT matters when you ship a library that other teams build on. Telling internal customers "by the way, our messaging stack is now $X/year per service" is not a fun meeting.
- Vendor risk is not theoretical anymore. If one OSS .NET messaging library changed its license, others can. We wanted something with a smaller blast radius.
- We did not use 60% of what MassTransit ships. We use pubsub, retry, DLQ, outbox, OTel, and one saga. That is it.

## What we evaluated

We looked at every active MIT-licensed option and one commercial one:

**Rebus.** Mature, MIT, fluent saga model that has been refined over years. The API surface is broad because the project has been quietly solving real problems since 2011. Honest reason we didn't pick it: [FILL IN: a concrete technical reason — e.g., "our event stream shape lined up more naturally with the per-destination queue model in Syed.Messaging"]. Rebus is a strong option for many teams.

**NServiceBus.** Commercial. We were trying to move away from a commercial dependency. Next.

**Wolverine.** Mature codegen-based approach, GA since 2022, well-documented. Jeremy Miller has been doing this work for a long time. We didn't pick it because [FILL IN: concrete technical reason — e.g., "the codegen model conflicted with our existing source-generator pipeline" or "our shared-transport-config pattern didn't fit the per-endpoint Wolverine convention"]. Worth a serious look if you're starting from scratch.

**EasyNetQ.** RabbitMQ only. We have Service Bus in two services and Kafka in our event stream. Disqualified on transport coverage.

**Syed.Messaging.** MIT, .NET 10, transport-agnostic across RabbitMQ, Kafka, and Azure Service Bus with one narrow API. Two things in the repo nobody else has, and they're the reason this post exists at all:

- **Chaos-by-default middleware** — see the companion post [Why my .NET messaging library breaks 5% of your messages by default]([FILL IN: link to chaos post]). That alone changed how we thought about distributed-systems testing in dev.
- **DLQ-driven autoscaling playbook + KEDA/HPA reference manifests** — concrete PromQL, a documented signal model, a worked example with 7 days of baselines. We were planning to build something like this internally.

Smaller community, fewer Stack Overflow answers, single-maintainer at the time of writing. Real tradeoffs that we weighed carefully. We tried it on the lowest-stakes service first and it stuck.

## The 30-minute migration

I will caveat this section. The first service was fast. The last service was not. But the per-service shape did not change much, and once you have done two of them the third is muscle memory.

The consumer shape barely moves. MassTransit:

```csharp
public class OrderCreatedConsumer : IConsumer<OrderCreated>
{
    public Task Consume(ConsumeContext<OrderCreated> context)
    {
        var msg = context.Message;
        // ...
        return Task.CompletedTask;
    }
}
```

Syed.Messaging:

```csharp
public class OrderCreatedHandler : IMessageHandler<OrderCreated>
{
    public async Task HandleAsync(OrderCreated msg, MessageContext ctx, CancellationToken ct)
    {
        // ...
    }
}
```

The shape is the same. `ConsumeContext` becomes `MessageContext`. You get a `CancellationToken` for free, which I prefer.

Registration moves from `AddMassTransit(...)` with its endpoint config DSL to a flatter fluent chain:

```csharp
services.AddMessaging(m =>
{
    m.UseRabbitMq(o =>
    {
        o.ConnectionString = "amqp://guest:guest@localhost:5672/";
        o.MainExchangeName = "orders.exchange";
    });

    m.AddConsumer<OrderCreated, OrderCreatedHandler>(c =>
    {
        c.Destination = "orders.created";
        c.SubscriptionName = "orders-worker";
        c.MaxConcurrency = 4;
        c.RetryPolicy = new RetryPolicy { MaxRetries = 5 };
    });
});
```

The mental model shift is the part to internalize. MassTransit thinks in **consumers + endpoints**, where the endpoint is the queue and you bind consumers to it. Syed.Messaging thinks in **consumers + destinations**, where each `AddConsumer<T>()` declares its own per-destination queue bound by routing key (this is the v1.2.0 change, and I would not have wanted to do this migration before it landed). You stop reasoning about shared queues. You stop debugging cross-talk. Each consumer owns its queue. Done.

The one gotcha that bit us: [FILL IN: real anecdote, e.g. "we had a consumer that depended on MassTransit's automatic header propagation for our correlation ID, and Syed.Messaging propagates W3C trace context but not arbitrary headers by default. We wrote a 12-line `IMessageMiddleware` and moved on, but it was a half-day of confusion before we found it."]

Per-service time was roughly [FILL IN: e.g. "2-4 hours for a simple pubsub service, a full day for the saga service"].

## What we kept

Outbox worked the same or better. Syed.Messaging's `OutboxPublisherService` polls an EF Core-backed `IOutboxStore`. The transactional shape is concrete: stage your domain entity on the `DbContext`, call `outbox.SaveAsync(...)` (which internally commits both the entity and the outbox row in one `SaveChangesAsync`), and the background publisher drains the outbox table. One useful feature we did not have in our MT setup is **raw mode** — anonymous payloads when the consumer side does not have your CLR type. We use it for one cross-team integration where the other team is on Node.

Retries with exponential backoff, DLQ routing, OpenTelemetry spans, structured logging scopes: all in the box, all worked on day one. The OTel integration is one line (`AddSource("Syed.Messaging")`) and the activity names are stable.

Health checks were a non-event. The `Syed.Messaging.HealthChecks` package wires per-transport checks into ASP.NET Core.

## What we had to give up

Be honest with yourself about this before you start.

**Automatonymous.** MassTransit's state machine DSL is the best in the .NET space and it is not close. Syed.Messaging has `ISagaHandler<TState, TMessage>` with correlation, timeouts, and pluggable persistence (EF Core or in-memory) and locking (Redis, in-memory, or no-op). It is enough for our one saga. If you have five sagas with complex state graphs, scope this carefully — you will be writing more handler code and less DSL.

**In-memory test transport.** MassTransit's harness was something we leaned on for integration tests. Syed.Messaging does not ship an in-memory transport in v1.2.0, so we refactored to handler-level tests with mocks plus a thin integration layer that hits a real Rabbit in CI via Testcontainers. Cleaner, but it was real work.

**Niche transports.** We do not use ActiveMQ, Amazon SQS, or NATS so this was not a blocker for us. If you do, this is not your library yet. File an issue or contribute the transport — the `IMessageTransport` abstraction is small.

## The two things nobody else has

This is the part that actually justifies the migration cost, and the part I want to call out specifically.

**1. Chaos-by-default middleware.** Set `SYED_CHAOS_LEVEL=medium` and 5% of your messages in dev get realistic failure injection — drops, duplicates, delays, header corruption, out-of-order delivery, ack timeouts. On by default in dev/staging; a separate, deliberate `SYED_CHAOS_PROD=true` is required for production. We covered the full story in [Why my .NET messaging library breaks 5% of your messages by default]([FILL IN: link to chaos post]). If you read one post about this library, read that one.

**2. DLQ-driven autoscaling playbook + KEDA / HPA reference manifests.** The repo at [docs/observability/autoscaling-signals.md](../../observability/autoscaling-signals.md) walks through turning the retry, DLQ, and poison counters into KEDA and HPA decisions. Actual PromQL for retry pressure, the retry-to-DLQ conversion ratio as a scale-up blocker (because scaling up a broken pipeline just deadletters faster), poison ratio as an incident guard, a composite score for a single KEDA trigger, and a worked example with 7 days of baselines. Reference Kubernetes manifests in [docs/deploy/kubernetes/](../../deploy/kubernetes/) that you can `kubectl apply` and tune.

The companion doc, [dlq-dashboard.md](../../observability/dlq-dashboard.md), has the Grafana queries and alert thresholds. The Kafka-specific cap (do not scale replicas past partition count) is the kind of detail that comes from running this in production. [FILL IN: optional anecdote — only if you have actually load-tested the autoscaling. If you have not, delete this sentence.]

## The bill

Engineering time: roughly [FILL IN: ~X hours of engineering across Y services, e.g. "~60 hours across 11 services, spread over 3 weeks"]. Most of that was the saga service and rebuilding the test harness around real-broker integration tests instead of MT's in-memory harness. The pubsub services were [FILL IN: e.g. "an afternoon each"].

Versus the projected v9 commercial subscription: payback inside [FILL IN: e.g. "the first quarter once v9 was in our procurement pipeline"].

Ongoing cost: we now own the integration. If something breaks at 2am we read source instead of filing a support ticket. The codebase is small and the abstractions are narrow enough that I have not been scared by anything I have read.

## Should you do this?

A rough tier list based on what I have seen so far.

- **Pubsub only, simple retries, no sagas.** Trivial migration. Yes, do it.
- **Heavy retry and DLQ requirements.** Easy migration, and you will like the autoscaling docs more than you expect.
- **Production sagas built on Automatonymous.** Harder. Scope it. You are rewriting state graphs as handlers and you will miss the DSL. The migration is achievable; it is just not free.
- **You need transports beyond RabbitMQ, Kafka, and Azure Service Bus.** Not yet. File an issue, contribute the transport, or stay where you are.
- **You are already paying for MassTransit and it is working.** Then keep paying. This post is not "everyone must switch." It is "here is what switching looked like."

## What I would tell my past self

Four things.

1. **Start with the smallest service.** Not the most representative one. The smallest. You need to feel the API in your hands before you make any architecture calls.
2. **Write the test strategy before the second service.** The harness you build for service two is the one you will use for the rest. If it is wrong, you find out cheaply.
3. **Wire OpenTelemetry on day one, before you migrate any handlers.** Spans across the cutover are how you prove behavior is preserved. The `Syed.Messaging` source name is stable; it just works.
4. **Read [autoscaling-signals.md](../../observability/autoscaling-signals.md) before you size your cluster.** [FILL IN: optional concrete sizing anecdote — only include if it actually happened to you. Otherwise: "We picked initial replica bounds straight from the worked example in that doc and tuned from there."]

---

If you have migrated off MassTransit (to anything), I would like to hear how it went. [FILL IN: contact / mastodon / twitter / email]. And if you are the maintainer of one of the libraries I name-checked above and I got something wrong, send a correction; I will edit the post.
