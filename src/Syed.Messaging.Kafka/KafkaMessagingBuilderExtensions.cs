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
        services.TryAddSingleton<IMessageBus, TransportMessageBus>();

        return builder;
    }
}

