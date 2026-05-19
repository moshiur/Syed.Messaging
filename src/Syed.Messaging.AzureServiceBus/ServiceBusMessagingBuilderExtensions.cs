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
        services.TryAddSingleton<IMessageBus, TransportMessageBus>();

        return builder;
    }
}

