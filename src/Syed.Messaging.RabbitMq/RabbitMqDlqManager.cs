using System.Diagnostics;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Syed.Messaging.RabbitMq;

/// <summary>
/// RabbitMQ implementation of IDlqManager for managing dead-lettered messages.
/// </summary>
public class RabbitMqDlqManager : IDlqManager
{
    private readonly IModel _channel;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqDlqManager> _logger;

    public RabbitMqDlqManager(IModel channel, RabbitMqOptions options, ILogger<RabbitMqDlqManager> logger)
    {
        _channel = channel;
        _options = options;
        _logger = logger;
    }

    public Task<long> GetCountAsync(CancellationToken ct = default)
    {
        var result = _channel.QueueDeclarePassive(_options.DeadLetterQueueName);
        return Task.FromResult((long)result.MessageCount);
    }

    public Task<IReadOnlyList<IMessageEnvelope>> PeekAsync(int limit = 10, CancellationToken ct = default)
    {
        var messages = new List<IMessageEnvelope>();
        
        for (int i = 0; i < limit; i++)
        {
            var result = _channel.BasicGet(_options.DeadLetterQueueName, autoAck: false);
            if (result == null) break;

            var envelope = ConvertToEnvelope(result);
            messages.Add(envelope);

            // Reject without requeue to put it back (we're just peeking)
            _channel.BasicNack(result.DeliveryTag, multiple: false, requeue: true);
        }

        return Task.FromResult<IReadOnlyList<IMessageEnvelope>>(messages);
    }

    public Task<bool> RequeueAsync(string messageId, CancellationToken ct = default)
    {
        // Get messages until we find the one with matching ID
        var found = false;
        var result = _channel.BasicGet(_options.DeadLetterQueueName, autoAck: false);
        
        while (result != null)
        {
            var envelope = ConvertToEnvelope(result);
            
            if (envelope.MessageId == messageId)
            {
                // Republish to main queue
                var props = _channel.CreateBasicProperties();
                props.Persistent = true;
                props.Headers = result.BasicProperties.Headers;
                props.MessageId = result.BasicProperties.MessageId;
                props.Type = result.BasicProperties.Type;

                _channel.BasicPublish(
                    exchange: _options.MainExchangeName,
                    routingKey: _options.RoutingKey,
                    basicProperties: props,
                    body: result.Body);

                // Ack the DLQ message
                _channel.BasicAck(result.DeliveryTag, multiple: false);
                
                _logger.LogInformation("Requeued message {MessageId} from DLQ to main queue", messageId);
                found = true;
                break;
            }
            else
            {
                // Not the one we're looking for, put it back
                _channel.BasicNack(result.DeliveryTag, multiple: false, requeue: true);
            }

            result = _channel.BasicGet(_options.DeadLetterQueueName, autoAck: false);
        }

        return Task.FromResult(found);
    }

    public Task<int> RequeueAllAsync(CancellationToken ct = default)
    {
        var count = 0;
        var result = _channel.BasicGet(_options.DeadLetterQueueName, autoAck: false);

        while (result != null)
        {
            var props = _channel.CreateBasicProperties();
            props.Persistent = true;
            props.Headers = result.BasicProperties.Headers;
            props.MessageId = result.BasicProperties.MessageId;
            props.Type = result.BasicProperties.Type;

            // Clear retry count header to start fresh
            if (props.Headers != null && props.Headers.ContainsKey("x-retry-count"))
            {
                props.Headers.Remove("x-retry-count");
            }

            _channel.BasicPublish(
                exchange: _options.MainExchangeName,
                routingKey: _options.RoutingKey,
                basicProperties: props,
                body: result.Body);

            _channel.BasicAck(result.DeliveryTag, multiple: false);
            count++;

            result = _channel.BasicGet(_options.DeadLetterQueueName, autoAck: false);
        }

        _logger.LogInformation("Requeued {Count} messages from DLQ to main queue", count);
        return Task.FromResult(count);
    }

    public Task<bool> DeleteAsync(string messageId, CancellationToken ct = default)
    {
        var found = false;
        var result = _channel.BasicGet(_options.DeadLetterQueueName, autoAck: false);

        while (result != null)
        {
            var envelope = ConvertToEnvelope(result);

            if (envelope.MessageId == messageId)
            {
                // Just ack it to remove from DLQ
                _channel.BasicAck(result.DeliveryTag, multiple: false);
                _logger.LogInformation("Deleted message {MessageId} from DLQ", messageId);
                found = true;
                break;
            }
            else
            {
                _channel.BasicNack(result.DeliveryTag, multiple: false, requeue: true);
            }

            result = _channel.BasicGet(_options.DeadLetterQueueName, autoAck: false);
        }

        return Task.FromResult(found);
    }

    public Task<int> PurgeAsync(CancellationToken ct = default)
    {
        var count = _channel.QueuePurge(_options.DeadLetterQueueName);
        _logger.LogInformation("Purged {Count} messages from DLQ", count);
        return Task.FromResult((int)count);
    }

    private MessageEnvelope ConvertToEnvelope(BasicGetResult result)
    {
        var props = result.BasicProperties;
        var headers = new Dictionary<string, string>();

        if (props.Headers != null)
        {
            foreach (var kv in props.Headers)
            {
                headers[kv.Key] = kv.Value switch
                {
                    byte[] bytes => System.Text.Encoding.UTF8.GetString(bytes),
                    string s => s,
                    _ => kv.Value?.ToString() ?? string.Empty
                };
            }
        }

        return new MessageEnvelope
        {
            MessageType = props.Type ?? string.Empty,
            MessageId = props.MessageId,
            CorrelationId = props.CorrelationId ?? Activity.Current?.Id,
            Headers = headers,
            Body = result.Body.ToArray(),
            Timestamp = props.Timestamp.UnixTime > 0 
                ? DateTimeOffset.FromUnixTimeSeconds(props.Timestamp.UnixTime) 
                : DateTimeOffset.UtcNow
        };
    }
}
