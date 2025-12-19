using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Syed.Messaging;

public static class MessagingServiceCollectionExtensions
{
    public static IServiceCollection AddMessaging(
        this IServiceCollection services,
        Action<MessagingBuilder> configure)
    {
        // Register core services
        services.TryAddSingleton<ISerializer, SystemTextJsonSerializer>();
        services.TryAddSingleton<IMessageTypeRegistry, MessageTypeRegistry>();

        var builder = new MessagingBuilder(services);
        configure(builder);
        return services;
    }
}

