using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Syed.Messaging;

namespace Syed.Messaging.Kafka;

public static class KafkaMessagingBuilderExtensions
{
    public static MessagingBuilder UseKafka(
        this MessagingBuilder builder,
        Action<KafkaOptions> configure)
    {
        var services = builder.Services;

        var options = new KafkaOptions();
        configure(options);

        services.AddSingleton(options);
        services.TryAddSingleton<IMessageTransport, KafkaTransport>();
        services.TryAddSingleton<IMessageBus, KafkaBus>();

        return builder;
    }
}

internal sealed class KafkaBus : IMessageBus
{
    private readonly IMessageTransport _transport;
    private readonly ISerializer _serializer;

    public KafkaBus(IMessageTransport transport, ISerializer serializer)
    {
        _transport = transport;
        _serializer = serializer;
    }

    public Task PublishAsync<T>(string destination, T message, CancellationToken ct = default)
    {
        var headers = new Dictionary<string, string>
        {
            ["message-id"] = Guid.NewGuid().ToString(),
            ["correlation-id"] = Guid.NewGuid().ToString()
        };

        var envelope = new MessageEnvelope
        {
            MessageType = typeof(T).FullName ?? typeof(T).Name,
            MessageId = headers["message-id"],
            CorrelationId = headers["correlation-id"],
            Headers = headers,
            Body = _serializer.Serialize(message)
        };

        return _transport.PublishAsync(envelope, destination, ct);
    }

    public Task SendAsync<T>(string destination, T message, CancellationToken ct = default)
        => PublishAsync(destination, message, ct);

    public Task<TResponse> RequestAsync<TRequest, TResponse>(string destination, TRequest message, CancellationToken ct = default)
        => throw new NotSupportedException("RPC is not supported in Kafka transport");
}

