using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Syed.Messaging.Sagas;

public static class SagaServiceCollectionExtensions
{
    public static IServiceCollection AddSagas(
        this IServiceCollection services,
        Action<SagaRegistryBuilder> configure)
    {
        var builder = new SagaRegistryBuilder(services);
        configure(builder);

        services.TryAddSingleton<ISagaRegistry>(sp => builder.Build());
        services.TryAddSingleton<ISagaRuntime, SagaRuntime>();

        // Timeout infrastructure
        services.TryAddSingleton<ISagaTimeoutStore, InMemorySagaTimeoutStore>();
        services.TryAddSingleton<ISagaTimeoutScheduler, SagaTimeoutScheduler>();
        services.AddHostedService<SagaTimeoutDispatcher>();

        return services;
    }
}
