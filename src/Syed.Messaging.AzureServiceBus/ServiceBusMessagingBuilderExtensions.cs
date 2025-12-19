using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Syed.Messaging;

namespace Syed.Messaging.AzureServiceBus;

public static class ServiceBusMessagingBuilderExtensions
{
    public static MessagingBuilder UseAzureServiceBus(
        this MessagingBuilder builder,
        Action<ServiceBusOptions> configure)
    {
        var services = builder.Services;

        var options = new ServiceBusOptions();
        configure(options);

        services.AddSingleton(options);
        services.TryAddSingleton<IMessageTransport, ServiceBusTransport>();
        services.TryAddSingleton<IMessageBus, ServiceBusBus>();

        return builder;
    }
}

internal sealed class ServiceBusBus : IMessageBus
{
    private readonly IMessageTransport _transport;
    private readonly ISerializer _serializer;

    public ServiceBusBus(IMessageTransport transport, ISerializer serializer)
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
}
