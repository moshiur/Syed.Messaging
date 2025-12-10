using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Syed.Messaging;
using Syed.Messaging.RabbitMq;

namespace Syed.Messaging.Aspire;

public static class AspireMessagingExtensions
{
    /// <summary>
    /// Configures messaging using standard Aspire connection string naming conventions.
    /// For example, if your AppHost defines a RabbitMQ resource named "rabbit", you can
    /// bind its connection string to RabbitMQ using this helper.
    /// </summary>
    public static IServiceCollection AddRabbitMqFromAspire(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionStringName = "rabbit",
        Action<RabbitMqOptions>? configure = null)
    {
        services.AddMessaging(m =>
        {
            m.UseRabbitMq(options =>
            {
                options.ConnectionString = configuration.GetConnectionString(connectionStringName) ??
                                           throw new InvalidOperationException($"Missing connection string '{connectionStringName}'.");

                configure?.Invoke(options);
            });
        });

        return services;
    }
}
