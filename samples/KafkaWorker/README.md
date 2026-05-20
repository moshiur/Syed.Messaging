# KafkaWorker Sample

This sample demonstrates two important Kafka behaviors:

1. **Partition key strategy**
   - Publish with `partition-key` header so related events stay ordered.
   - Example: use `CustomerId` or `AggregateId` as partition key.

2. **Cross-partition scaling**
   - Configure `kafka.Consumer.MaxConcurrentPartitions` to process different partitions in parallel.
   - Messages in the same partition still execute sequentially.

## Run locally

1. Start Kafka locally (Docker Compose or your own broker).
2. From repository root:

```bash
dotnet run --project samples/KafkaWorker/KafkaWorker.csproj
```

## Expected behavior

- Events with the same `partition-key` are handled in order.
- Events from different partition keys can be processed concurrently.
- Rebalance/partition events are logged when `LogRebalanceEvents = true`.

## Metrics export wiring (Prometheus-first)

Syed.Messaging emits metrics from the `Syed.Messaging` meter. You can inspect locally first:

```bash
dotnet-counters monitor --counters Syed.Messaging --process-id <pid>
```

For Prometheus scraping in a worker process, add `OpenTelemetry.Exporter.Prometheus.HttpListener` and wire metrics:

```csharp
// Program.cs sketch
using OpenTelemetry.Metrics;

builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics.AddMeter("Syed.Messaging");
        metrics.AddPrometheusHttpListener(options => options.UriPrefixes = new[] { "http://localhost:9464/" });
    });
```

Then scrape `http://localhost:9464/metrics` and apply queries from:

- `docs/observability/dlq-dashboard.md`
- `docs/observability/autoscaling-signals.md` (scale workers from retry/DLQ pressure)
- `docs/deploy/kubernetes/` (KEDA and HPA reference manifests)

