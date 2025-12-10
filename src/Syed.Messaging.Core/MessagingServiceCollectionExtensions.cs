using Microsoft.Extensions.DependencyInjection;

namespace Syed.Messaging;

public static class MessagingServiceCollectionExtensions
{
    public static IServiceCollection AddMessaging(
        this IServiceCollection services,
        Action<MessagingBuilder> configure)
    {
        services.AddSingleton<ISerializer, SystemTextJsonSerializer>();
        var builder = new MessagingBuilder(services);
        configure(builder);
        return services;
    }
}
