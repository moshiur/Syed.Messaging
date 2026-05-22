# Contributing to Syed.Messaging

Thank you for considering a contribution.

Syed.Messaging is an evolving, experimental framework — feedback and contributions are highly appreciated.

---

## 🧭 Project Philosophy

- **Minimalism over complexity.**
  Do the simplest thing that solves a real problem.

- **Composition over inheritance.**
  Most abstractions should be interfaces or small utilities.

- **Predictability over magic.**
  No hidden conventions. All behavior should be explicit.

- **Extensibility for real-world systems.**
  Make it easy for teams to plug in their own transports,
  serializers, envelopes, and policies.

- **Consistency across transports.**
  RabbitMQ, Kafka, Azure Service Bus should feel the same for developers.

---

## 🛠 How to Contribute

### 1. Discuss first (recommended)

Before implementing a feature, please open:

- an **Issue** (bug report or feature request), or
- a **Discussion** (design question, API proposal).

This avoids wasted effort and helps align on design goals.

### 2. Fork & Clone

```bash
git clone <your-fork-url>
cd Syed.Messaging
```

### 3. Build and test locally

The solution targets **.NET 10.0** (preview-track SDK).

```bash
dotnet build Syed.Messaging.sln -c Release
dotnet test  Syed.Messaging.sln -c Release
```

To run the samples, start the local broker stack:

```bash
docker compose up -d
dotnet run --project samples/OrderWorker/OrderWorker.csproj          # RabbitMQ
dotnet run --project samples/KafkaWorker/KafkaWorker.csproj          # Kafka
dotnet run --project samples/OrderSagaDemo/OrderSagaDemo.csproj      # Saga (SQLite)
```

The `ServiceBusWorker` sample requires a real Azure Service Bus namespace —
see [samples/ServiceBusWorker/README.md](samples/ServiceBusWorker/README.md).

### 4. Open a PR

- Keep diffs focused. One concern per PR.
- Add tests where it makes sense. The test suite runs in seconds — prefer broker-free unit tests; integration tests against real brokers belong in CI service containers (see [.github/workflows/publish.yml](.github/workflows/publish.yml)).
- Update [ROADMAP.md](ROADMAP.md) or [docs/task.md](docs/task.md) if your change closes a milestone.
- Cross-transport features (anything that touches `IMessageTransport` or the abstraction layer) should ideally land on RabbitMQ, Kafka, and Azure Service Bus together.

---

## 🧪 What we'd love help with

See [ROADMAP.md](ROADMAP.md) and the ongoing milestone tracking in
[docs/task.md](docs/task.md).

Particularly welcome:

- Aspire dashboard wiring (Lane A in the Phase 5 plan)
- Kubernetes deployment recipes (Lane B)
- Azure Service Bus hardening — scheduled retry, session handling (Lane C)
- Migration guides for users coming from MassTransit, NServiceBus, Rebus
- New transport implementations (Amazon SQS, Google Pub/Sub, NATS)
- In-memory transport for unit tests

---

## 📝 License

By contributing, you agree that your contributions will be licensed under
the project's [MIT License](LICENSE).
