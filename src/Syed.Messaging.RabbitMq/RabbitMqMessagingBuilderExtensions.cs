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
        services.TryAddSingleton<IMessageBus, TransportMessageBus>();

        return builder;
    }
}

public sealed class RabbitMqBus : TransportMessageBus
{
    public RabbitMqBus(IMessageTransport transport, ISerializer serializer)
        : this(transport, serializer, new MessageTypeRegistry())
    {
    }

    public RabbitMqBus(
        IMessageTransport transport,
        ISerializer serializer,
        IMessageTypeRegistry messageTypeRegistry)
        : base(transport, serializer, messageTypeRegistry)
    {
    }
}

