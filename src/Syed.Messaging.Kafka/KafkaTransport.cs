using System.Diagnostics;
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
            AutoOffsetReset = AutoOffsetReset.Earliest
        };

        using var consumer = new ConsumerBuilder<string, byte[]>(config).Build();
        consumer.Subscribe(topic);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var cr = consumer.Consume(ct);
                if (cr is null) continue;

                var envelope = ToEnvelope(cr);
                var stopwatch = Stopwatch.StartNew();
                
                MessagingMetrics.MessagesReceived.Add(1, new KeyValuePair<string, object?>("message_type", envelope.MessageType));

                // Structured logging scope
                using var logScope = _logger.BeginScope(new Dictionary<string, object?>
                {
                    ["MessageId"] = envelope.MessageId,
                    ["CorrelationId"] = envelope.CorrelationId,
                    ["MessageType"] = envelope.MessageType
                });

                var result = await handler(envelope, ct);
                stopwatch.Stop();
                
                MessagingMetrics.ProcessingDuration.Record(stopwatch.Elapsed.TotalMilliseconds, 
                    new KeyValuePair<string, object?>("message_type", envelope.MessageType));

                switch (result)
                {
                    case TransportAcknowledge.Ack:
                        MessagingMetrics.MessagesProcessed.Add(1, new KeyValuePair<string, object?>("message_type", envelope.MessageType));
                        consumer.Commit(cr);
                        break;

                    case TransportAcknowledge.Retry:
                        MessagingMetrics.MessagesRetried.Add(1, new KeyValuePair<string, object?>("message_type", envelope.MessageType));
                        await PublishToRetryTopicAsync(cr, envelope, ct);
                        consumer.Commit(cr);
                        break;

                    case TransportAcknowledge.DeadLetter:
                        MessagingMetrics.MessagesDeadLettered.Add(1, new KeyValuePair<string, object?>("message_type", envelope.MessageType));
                        await _producer.ProduceAsync(dlqTopic, new Message<string, byte[]>
                        {
                            Key = cr.Message.Key,
                            Value = cr.Message.Value,
                            Headers = cr.Message.Headers
                        }, ct);
                        consumer.Commit(cr);
                        break;
                }
            }
            catch (ConsumeException ex)
            {
                MessagingMetrics.MessagesFailed.Add(1);
                _logger.LogError(ex, "Kafka consume error.");
            }
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
        var headers = new Headers();
        headers.Add("message-type", System.Text.Encoding.UTF8.GetBytes(envelope.MessageType));
        if (envelope.MessageId is not null)
            headers.Add("message-id", System.Text.Encoding.UTF8.GetBytes(envelope.MessageId));
        if (envelope.CorrelationId is not null)
            headers.Add("correlation-id", System.Text.Encoding.UTF8.GetBytes(envelope.CorrelationId));

        foreach (var kv in envelope.Headers)
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
}
