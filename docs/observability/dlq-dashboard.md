# DLQ Metrics Dashboard and Alerts

This guide defines a Prometheus-first dashboard for dead-letter queue behavior using `Syed.Messaging` metrics.

## Metric and tag model

Primary counter:

- `messaging_messages_deadlettered_total`

Required tags emitted by transports:

- `transport` (`kafka`, `rabbitmq`, `azureservicebus`)
- `destination` (normalized queue/topic path, bounded cardinality)
- `message_type`
- `reason` (`deserialization_failure`, `max_retry_exhausted`, `handler_exception`, `schema_validation_failed`, `transport_reject`)

Supporting counters:

- `messaging_messages_retried_total`
- `messaging_messages_failed_total`
- `messaging_messages_poisoned_total`

## Dashboard queries (PromQL)

### DLQ rate by transport

```promql
sum by (transport) (rate(messaging_messages_deadlettered_total[5m]))
```

### DLQ rate by reason

```promql
sum by (reason) (rate(messaging_messages_deadlettered_total[5m]))
```

### Top destinations by DLQ volume (15m)

```promql
topk(10, sum by (destination) (increase(messaging_messages_deadlettered_total[15m])))
```

### Retry spike detection (5m)

```promql
sum(rate(messaging_messages_retried_total[5m]))
```

### Retry-to-DLQ conversion ratio (15m)

```promql
sum(increase(messaging_messages_deadlettered_total[15m]))
/
clamp_min(sum(increase(messaging_messages_retried_total[15m])), 1)
```

### Poison ratio (15m)

```promql
sum(increase(messaging_messages_poisoned_total[15m]))
/
clamp_min(sum(increase(messaging_messages_deadlettered_total[15m])), 1)
```

## Alert defaults (starting point)

Tune these to workload profile and expected burst patterns.

### Alert: high DLQ rate

- Condition:

```promql
sum(rate(messaging_messages_deadlettered_total[5m])) > 1
```

- For: `10m`
- Severity: warning

### Alert: critical DLQ surge

- Condition:

```promql
sum(rate(messaging_messages_deadlettered_total[1m])) > 5
```

- For: `5m`
- Severity: critical

### Alert: retry pressure with poor recovery

- Condition:

```promql
(
  sum(increase(messaging_messages_deadlettered_total[15m]))
  /
  clamp_min(sum(increase(messaging_messages_retried_total[15m])), 1)
) > 0.4
```

- For: `15m`
- Severity: warning

## Runbook by DLQ reason

| Reason | Typical cause | First checks | Next action |
| --- | --- | --- | --- |
| `deserialization_failure` | payload/schema mismatch | producer version, serializer config, payload format | deploy compatibility fix or route incompatible producer versions |
| `max_retry_exhausted` | persistent business or infra failure | downstream dependency health, timeout logs, handler exceptions | fix root failure, then requeue from DLQ in batches |
| `handler_exception` | unhandled app exception | stack traces, recent deploys, feature flags | hotfix handler logic, add regression test |
| `schema_validation_failed` | schema contract violation | registry compatibility settings, schema evolution rules | revise schema or add upgrade mapping |
| `transport_reject` | broker/transport-level rejection | broker health, auth/ACL, queue/topic policy | correct transport configuration and replay safely |

## Cardinality guidance

- Keep `destination` stable and low-cardinality. Do not embed tenant IDs, GUIDs, or request IDs in destination names.
- Keep `reason` constrained to the approved taxonomy.
- Add custom tags only if they are bounded and operationally actionable.

## Sample usage

- Local sample guidance: `samples/KafkaWorker/README.md`
- Roadmap context: `ROADMAP.md`

