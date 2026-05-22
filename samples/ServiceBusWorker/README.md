# ServiceBusWorker Sample

This sample shows the Azure Service Bus transport with one consumer (`OrderCreatedHandler`) wired into a generic-host worker. The shape mirrors [OrderWorker](../OrderWorker/) (RabbitMQ) and [KafkaWorker](../KafkaWorker/) — the only difference is the `UseAzureServiceBus` block in `Program.cs`.

## Prerequisite: a real Azure Service Bus namespace

There is no free local emulator for Azure Service Bus (unlike RabbitMQ and Kafka, which the repo's [docker-compose.yml](../../docker-compose.yml) covers). To run this sample you need:

1. **An Azure Service Bus namespace** (Standard or Premium tier).
2. **A SAS connection string** with `Manage`, `Send`, and `Listen` claims. From the Azure Portal: Namespace → Shared access policies → `RootManageSharedAccessKey` → Primary Connection String.
3. **A topic** named `orders.created` (the sample creates it on first publish if you grant `Manage`).

## Wiring the connection string

Don't paste the connection string into `appsettings.json` and commit it. Use `dotnet user-secrets` locally:

```bash
cd samples/ServiceBusWorker
dotnet user-secrets init
dotnet user-secrets set "AzureServiceBus:ConnectionString" "Endpoint=sb://<your-namespace>.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=<key>"
```

Or use an environment variable:

```bash
export AzureServiceBus__ConnectionString="Endpoint=sb://..."
```

`Program.cs` already reads from configuration in the `AzureServiceBus:ConnectionString` slot.

## Run

```bash
dotnet run --project samples/ServiceBusWorker/ServiceBusWorker.csproj
```

You should see a single `OrderCreated` published on startup and consumed by `OrderCreatedHandler` within a few seconds.

## Production hardening notes

- Replace `RootManageSharedAccessKey` with a scoped SAS policy per service. Grant only the claims the service needs (`Send` for publishers, `Listen` for consumers).
- For workloads running in Azure, prefer **`DefaultAzureCredential`** with a managed identity over a connection string. The underlying `Azure.Messaging.ServiceBus` client supports it natively.
- For session-aware sagas, set the `session-id` header when publishing. See [docs/migrating-from-masstransit.md](../../docs/migrating-from-masstransit.md) and the architecture deep-dive at [docs/architecture_analysis.md](../../docs/architecture_analysis.md).
