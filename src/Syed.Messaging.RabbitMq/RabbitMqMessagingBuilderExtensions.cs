using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Syed.Messaging;

namespace Syed.Messaging.RabbitMq;

public static class RabbitMqMessagingBuilderExtensions
{
    public static MessagingBuilder UseRabbitMq(
        this MessagingBuilder builder,
        Action<RabbitMqOptions> configure)
    {
        var services = builder.Services;

        var options = new RabbitMqOptions();
        configure(options);

        services.AddSingleton(options);
        services.TryAddSingleton<IMessageTransport, RabbitMqTransport>();
        services.TryAddSingleton<IMessageBus, RabbitMqBus>();

        return builder;
    }
}

public sealed class RabbitMqBus : IMessageBus
{
    private readonly IMessageTransport _transport;
    private readonly ISerializer _serializer;

    public RabbitMqBus(IMessageTransport transport, ISerializer serializer)
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

    public async Task<TResponse> RequestAsync<TRequest, TResponse>(string destination, TRequest message, CancellationToken ct = default)
    {
        var correlationId = Guid.NewGuid().ToString();
        var headers = new Dictionary<string, string>
        {
            ["message-id"] = Guid.NewGuid().ToString(),
            ["correlation-id"] = correlationId
        };

        var envelope = new MessageEnvelope
        {
            MessageType = typeof(TRequest).FullName ?? typeof(TRequest).Name,
            MessageId = headers["message-id"],
            CorrelationId = correlationId,
            Headers = headers,
            Body = _serializer.Serialize(message)
        };

        var responseEnvelope = await _transport.RequestAsync(envelope, destination, ct);
        return _serializer.Deserialize<TResponse>(responseEnvelope.Body);
    }
}

