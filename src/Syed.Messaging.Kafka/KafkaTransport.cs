using System.Diagnostics;
using System.Collections.Concurrent;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Syed.Messaging;
using Syed.Messaging.Core;

namespace Syed.Messaging.Kafka;

public sealed class KafkaTransport : IMessageTransport, IDisposable
{
    private readonly KafkaOptions _options;
    private readonly ILogger<KafkaTransport> _logger;
    private readonly IProducer<string, byte[]> _producer;

    public KafkaTransport(KafkaOptions options, ILogger<KafkaTransport> logger)
    {
        _options = options;
        _logger = logger;

        var config = new ProducerConfig
        {
            BootstrapServers = _options.BootstrapServers
        };

        _producer = new ProducerBuilder<string, byte[]>(config).Build();
    }

    public async Task PublishAsync(IMessageEnvelope envelope, string destination, CancellationToken ct)
    {
        using var activity = MessagingDiagnostics.ActivitySource.StartActivity(
            MessagingDiagnostics.PublishActivityName,
            ActivityKind.Producer);

        var topic = _options.TopicPrefix + destination;
        
        // Support partition key from headers or MessageId
        var partitionKey = envelope.Headers.TryGetValue("partition-key", out var pk) 
            ? pk 
            : envelope.MessageId ?? Guid.NewGuid().ToString();

        if (activity is not null)
        {
            activity.SetTag("messaging.system", "kafka");
            activity.SetTag("messaging.destination", topic);
            activity.SetTag("messaging.message_type", envelope.MessageType);
            activity.SetTag("messaging.kafka.partition_key", partitionKey);
        }

        await _producer.ProduceAsync(topic,
            new Message<string, byte[]>
            {
                Key = partitionKey,
                Value = envelope.Body,
                Headers = BuildHeaders(envelope)
            },
            ct);

        MessagingMetrics.MessagesPublished.Add(1, new KeyValuePair<string, object?>("message_type", envelope.MessageType));
    }

    public Task SendAsync(IMessageEnvelope envelope, string destination, CancellationToken ct)
        => PublishAsync(envelope, destination, ct);

    public Task<IMessageEnvelope> RequestAsync(IMessageEnvelope envelope, string destination, CancellationToken ct)
        => throw new NotSupportedException("RPC is not supported in Kafka transport");

    public async Task SubscribeAsync(
        string subscriptionName,
        string destination,
        Func<IMessageEnvelope, CancellationToken, Task<TransportAcknowledge>> handler,
        CancellationToken ct)
    {
        var topic = _options.TopicPrefix + destination;
        var dlqTopic = topic + _options.DlqSuffix;

        var config = new ConsumerConfig
        {
            GroupId = _options.ConsumerGroupId,
            BootstrapServers = _options.BootstrapServers,
            AutoOffsetReset = _options.Consumer.AutoOffsetReset,
            EnableAutoCommit = _options.Consumer.EnableAutoCommit,
            EnableAutoOffsetStore = _options.Consumer.EnableAutoOffsetStore
        };

        if (_options.Consumer.MaxPollIntervalMs.HasValue)
            config.MaxPollIntervalMs = _options.Consumer.MaxPollIntervalMs.Value;

        if (_options.Consumer.SessionTimeoutMs.HasValue)
            config.SessionTimeoutMs = _options.Consumer.SessionTimeoutMs.Value;

        if (_options.Consumer.HeartbeatIntervalMs.HasValue)
            config.HeartbeatIntervalMs = _options.Consumer.HeartbeatIntervalMs.Value;

        if (_options.Consumer.UseStaticGroupMembership)
        {
            config.GroupInstanceId = string.IsNullOrWhiteSpace(_options.Consumer.GroupInstanceId)
                ? $"{subscriptionName}-{Environment.MachineName}".ToLowerInvariant()
                : _options.Consumer.GroupInstanceId;
        }

        config.Set("partition.assignment.strategy", GetPartitionAssignmentStrategy(_options.Consumer.PartitionAssignmentStrategy));

        var revokedPartitions = new HashSet<TopicPartition>();
        var partitionStateLock = new object();
        var pendingAcks = new ConcurrentQueue<PendingAcknowledge>();
        var dispatcher = new KafkaPartitionDispatcher<ConsumeResult<string, byte[]>>(
            _options.Consumer.MaxConcurrentPartitions,
            (partition, message, token) => ProcessPartitionMessageAsync(partition, message, handler, pendingAcks, dlqTopic, token),
            ct);

        var consumerBuilder = new ConsumerBuilder<string, byte[]>(config);

        consumerBuilder
            .SetPartitionsAssignedHandler((_, partitions) =>
            {
                lock (partitionStateLock)
                {
                    foreach (var partition in partitions)
                    {
                        revokedPartitions.Remove(partition);
                    }
                }

                if (_options.Consumer.LogRebalanceEvents)
                {
                    _logger.LogInformation("Kafka partitions assigned for {Topic}: {Partitions}",
                        topic, string.Join(", ", partitions.Select(p => $"{p.Topic}[{p.Partition.Value}]")));
                }
            })
            .SetPartitionsRevokedHandler((consumer, partitions) =>
            {
                lock (partitionStateLock)
                {
                    foreach (var partition in partitions)
                    {
                        revokedPartitions.Add(partition.TopicPartition);
                    }
                }

                TryCommitOnRevoke(consumer, partitions);
                dispatcher.Revoke(partitions.Select(p => p.TopicPartition));

                if (_options.Consumer.LogRebalanceEvents)
                {
                    _logger.LogInformation("Kafka partitions revoked for {Topic}: {Partitions}",
                        topic, string.Join(", ", partitions.Select(p => $"{p.Topic}[{p.Partition.Value}]")));
                }
            });

        using var consumer = consumerBuilder.Build();
        consumer.Subscribe(topic);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    DrainPendingAcks(consumer, pendingAcks, revokedPartitions, partitionStateLock);

                    var cr = consumer.Consume(TimeSpan.FromMilliseconds(200));
                    if (cr is null) continue;

                    if (!dispatcher.Enqueue(cr.TopicPartition, cr))
                    {
                        _logger.LogWarning("Unable to enqueue Kafka message for partition worker {Partition}.", cr.TopicPartition);
                    }
                }
                catch (ConsumeException ex)
                {
                    MessagingMetrics.MessagesFailed.Add(1);
                    _logger.LogError(ex, "Kafka consume error.");
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogDebug("Kafka consume loop cancelled for {Topic}.", topic);
        }
        finally
        {
            await dispatcher.CompleteAsync(TimeSpan.FromSeconds(10));
            DrainPendingAcks(consumer, pendingAcks, revokedPartitions, partitionStateLock);
            consumer.Close();
        }
    }

    private async Task PublishToRetryTopicAsync(ConsumeResult<string, byte[]> cr, IMessageEnvelope envelope, CancellationToken ct)
    {
        // Get current retry count
        var retryCountHeader = cr.Message.Headers.FirstOrDefault(h => h.Key == "x-retry-count");
        var retryCount = 0;
        if (retryCountHeader != null)
        {
            int.TryParse(System.Text.Encoding.UTF8.GetString(retryCountHeader.GetValueBytes()), out retryCount);
        }

        string retryTopic;
        if (_options.EnableDelayedRetry && _options.RetryDelaysSeconds.Length > 0)
        {
            // Pick delay based on retry count
            var delayIndex = Math.Min(retryCount, _options.RetryDelaysSeconds.Length - 1);
            var delaySeconds = _options.RetryDelaysSeconds[delayIndex];
            retryTopic = $"{_options.TopicPrefix}{envelope.MessageType}{_options.RetrySuffix}-{delaySeconds}s";
        }
        else
        {
            retryTopic = $"{_options.TopicPrefix}{envelope.MessageType}{_options.RetrySuffix}";
        }

        // Update retry count
        var newHeaders = new Headers();
        foreach (var h in cr.Message.Headers)
        {
            if (h.Key != "x-retry-count")
                newHeaders.Add(h.Key, h.GetValueBytes());
        }
        newHeaders.Add("x-retry-count", System.Text.Encoding.UTF8.GetBytes((retryCount + 1).ToString()));

        await _producer.ProduceAsync(retryTopic, new Message<string, byte[]>
        {
            Key = cr.Message.Key,
            Value = cr.Message.Value,
            Headers = newHeaders
        }, ct);

        _logger.LogInformation("Message {MessageId} sent to retry topic {RetryTopic} (attempt {RetryCount})",
            envelope.MessageId, retryTopic, retryCount + 1);
    }

    private Headers BuildHeaders(IMessageEnvelope envelope)
    {
        var values = new Dictionary<string, string>(envelope.Headers)
        {
            ["message-type"] = envelope.MessageType
        };

        if (!string.IsNullOrWhiteSpace(envelope.MessageVersion))
            values["message-version"] = envelope.MessageVersion;
        if (envelope.MessageId is not null)
            values["message-id"] = envelope.MessageId;
        if (envelope.CorrelationId is not null)
            values["correlation-id"] = envelope.CorrelationId;

        var headers = new Headers();
        foreach (var kv in values)
        {
            headers.Add(kv.Key, System.Text.Encoding.UTF8.GetBytes(kv.Value));
        }

        return headers;
    }

    private MessageEnvelope ToEnvelope(ConsumeResult<string, byte[]> cr)
    {
        var headers = new Dictionary<string, string>();
        string? type = null;
        string? messageId = cr.Message.Key;
        string? correlationId = null;

        foreach (var h in cr.Message.Headers)
        {
            var value = System.Text.Encoding.UTF8.GetString(h.GetValueBytes());
            if (h.Key == "message-type") type = value;
            else if (h.Key == "message-id") messageId = value;
            else if (h.Key == "correlation-id") correlationId = value;
            else headers[h.Key] = value;
        }

        return new MessageEnvelope
        {
            MessageType = type ?? "unknown",
            MessageId = messageId,
            CorrelationId = correlationId,
            CausationId = null,
            Headers = headers,
            Body = cr.Message.Value
        };
    }

    public void Dispose()
    {
        _producer.Flush();
        _producer.Dispose();
    }

    private static string GetPartitionAssignmentStrategy(KafkaPartitionAssignmentStrategy strategy)
        => strategy switch
        {
            KafkaPartitionAssignmentStrategy.Range => "range",
            KafkaPartitionAssignmentStrategy.RoundRobin => "roundrobin",
            _ => "cooperative-sticky"
        };

    private async Task ProcessPartitionMessageAsync(
        TopicPartition partition,
        ConsumeResult<string, byte[]> cr,
        Func<IMessageEnvelope, CancellationToken, Task<TransportAcknowledge>> handler,
        ConcurrentQueue<PendingAcknowledge> pendingAcks,
        string dlqTopic,
        CancellationToken ct)
    {
        var envelope = ToEnvelope(cr);
        var stopwatch = Stopwatch.StartNew();

        MessagingMetrics.MessagesReceived.Add(1, new KeyValuePair<string, object?>("message_type", envelope.MessageType));

        using var logScope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["MessageId"] = envelope.MessageId,
            ["CorrelationId"] = envelope.CorrelationId,
            ["MessageType"] = envelope.MessageType,
            ["Partition"] = $"{partition.Topic}[{partition.Partition.Value}]"
        });

        var result = await handler(envelope, ct);
        stopwatch.Stop();

        MessagingMetrics.ProcessingDuration.Record(stopwatch.Elapsed.TotalMilliseconds,
            new KeyValuePair<string, object?>("message_type", envelope.MessageType));

        switch (result)
        {
            case TransportAcknowledge.Ack:
                MessagingMetrics.MessagesProcessed.Add(1, new KeyValuePair<string, object?>("message_type", envelope.MessageType));
                pendingAcks.Enqueue(new PendingAcknowledge(cr, envelope.MessageId));
                break;

            case TransportAcknowledge.Retry:
                MessagingMetrics.MessagesRetried.Add(1, new KeyValuePair<string, object?>("message_type", envelope.MessageType));
                await PublishToRetryTopicAsync(cr, envelope, ct);
                pendingAcks.Enqueue(new PendingAcknowledge(cr, envelope.MessageId));
                break;

            case TransportAcknowledge.DeadLetter:
                var reason = envelope.Headers.TryGetValue("x-poison-reason", out var poisonReason)
                    ? poisonReason
                    : MessagingMetrics.DlqReasonTransportReject;
                MessagingMetrics.MessagesDeadLettered.Add(1, MessagingMetrics.BuildDeadLetterTags(
                    transport: "kafka",
                    destination: cr.Topic,
                    messageType: envelope.MessageType,
                    reason: reason));
                await _producer.ProduceAsync(dlqTopic, new Message<string, byte[]>
                {
                    Key = cr.Message.Key,
                    Value = cr.Message.Value,
                    Headers = cr.Message.Headers
                }, ct);
                pendingAcks.Enqueue(new PendingAcknowledge(cr, envelope.MessageId));
                break;
        }
    }

    private void DrainPendingAcks(
        IConsumer<string, byte[]> consumer,
        ConcurrentQueue<PendingAcknowledge> pendingAcks,
        HashSet<TopicPartition> revokedPartitions,
        object partitionStateLock)
    {
        while (pendingAcks.TryDequeue(out var ack))
        {
            AcknowledgeProcessedMessage(consumer, ack.Result, ack.MessageId, revokedPartitions, partitionStateLock);
        }
    }

    private void AcknowledgeProcessedMessage(
        IConsumer<string, byte[]> consumer,
        ConsumeResult<string, byte[]> result,
        string? messageId,
        HashSet<TopicPartition> revokedPartitions,
        object partitionStateLock)
    {
        var isRevoked = false;
        lock (partitionStateLock)
        {
            isRevoked = revokedPartitions.Contains(result.TopicPartition);
        }

        if (isRevoked)
        {
            _logger.LogWarning("Skipping offset commit for revoked partition {Partition} (MessageId: {MessageId}).",
                result.TopicPartition, messageId);
            return;
        }

        try
        {
            if (!_options.Consumer.EnableAutoOffsetStore)
            {
                consumer.StoreOffset(result);
            }

            if (!_options.Consumer.EnableAutoCommit)
            {
                consumer.Commit(result);
            }
        }
        catch (KafkaException ex) when (ex.Error.Code == ErrorCode.Local_State)
        {
            _logger.LogWarning(ex, "Partition ownership changed before commit/store (MessageId: {MessageId}, Partition: {Partition}).",
                messageId, result.TopicPartition);
        }
    }

    private void TryCommitOnRevoke(IConsumer<string, byte[]> consumer, List<TopicPartitionOffset> partitions)
    {
        if (_options.Consumer.EnableAutoCommit)
            return;

        try
        {
            consumer.Commit();
        }
        catch (KafkaException ex) when (ex.Error.Code == ErrorCode.Local_State)
        {
            _logger.LogDebug(ex, "Commit-on-revoke skipped due to local state change for partitions: {Partitions}",
                string.Join(", ", partitions.Select(p => $"{p.Topic}[{p.Partition.Value}]")));
        }
    }

    private sealed record PendingAcknowledge(ConsumeResult<string, byte[]> Result, string? MessageId);
}
