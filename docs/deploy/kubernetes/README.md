# Kubernetes Autoscaling References

Starter manifests for scaling Syed.Messaging worker deployments from Prometheus metrics.
These are **reference templates** — tune thresholds using your baseline (see [autoscaling-signals.md](../../observability/autoscaling-signals.md)).

## Prerequisites

- Worker Deployment exposing Prometheus metrics (OpenTelemetry Prometheus exporter or equivalent).
- Prometheus reachable from the cluster (in-cluster `prometheus-server` or managed monitoring).
- Messaging metrics present: `messaging_messages_retried_total`, `messaging_messages_deadlettered_total`.

## KEDA path (recommended)

1. Install KEDA in the cluster.
2. Copy and edit `keda-scaledobject-prometheus.yaml`:
   - `metadata.name` / `scaleTargetRef.name` → your Deployment name
   - `serverAddress` → your Prometheus URL
   - `threshold` → from baseline table in autoscaling-signals doc
   - `maxReplicaCount` → cap at Kafka partition count when applicable
3. Apply:

```bash
kubectl apply -f keda-scaledobject-prometheus.yaml
```

4. Verify: `kubectl get scaledobject` and watch Deployment replicas under load.

## HPA + prometheus-adapter path

1. Install [prometheus-adapter](https://github.com/kubernetes-sigs/prometheus-adapter).
2. Merge `hpa-prometheus-adapter.yaml` rules into your adapter ConfigMap (do not blindly replace cluster config).
3. Apply the HPA manifest after the adapter reports the external metric.
4. Verify: `kubectl get --raw "/apis/external.metrics.k8s.io/v1beta1"` lists `messaging_pressure_score`.

## Tuning checklist

- [ ] Confirm PromQL in Grafana returns non-zero under normal load.
- [ ] Set scale-up threshold to ~3× baseline retry rate.
- [ ] Set `maxReplicaCount` ≤ partition count for single-topic Kafka consumers.
- [ ] Test scale-down only after retry rate stays low for 30m+.
- [ ] Ensure DLQ/poison alerts fire before autoscaler adds replicas during incidents.

## Files

| File | Description |
| --- | --- |
| `keda-scaledobject-prometheus.yaml` | KEDA ScaledObject with retry-pressure trigger |
| `hpa-prometheus-adapter.yaml` | Adapter rule + HPA on composite pressure metric |
