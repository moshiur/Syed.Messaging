using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Syed.Messaging;

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
        var topic = _options.TopicPrefix + destination;
        var key = envelope.MessageId ?? Guid.NewGuid().ToString();

        await _producer.ProduceAsync(topic,
            new Message<string, byte[]>
            {
                Key = key,
                Value = envelope.Body,
                Headers = BuildHeaders(envelope)
            },
            ct);
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
        var retryTopic = topic + _options.RetrySuffix;
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
                var result = await handler(envelope, ct);

                switch (result)
                {
                    case TransportAcknowledge.Ack:
                        consumer.Commit(cr);
                        break;

                    case TransportAcknowledge.Retry:
                        await _producer.ProduceAsync(retryTopic, new Message<string, byte[]>
                        {
                            Key = cr.Message.Key,
                            Value = cr.Message.Value,
                            Headers = cr.Message.Headers
                        }, ct);
                        consumer.Commit(cr);
                        break;

                    case TransportAcknowledge.DeadLetter:
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
                _logger.LogError(ex, "Kafka consume error.");
            }
        }
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
