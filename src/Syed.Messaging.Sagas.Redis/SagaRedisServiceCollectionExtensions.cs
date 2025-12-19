using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;
using Syed.Messaging.Sagas;

namespace Syed.Messaging.Sagas.Redis;

/// <summary>
/// Extension methods for configuring Redis-based saga locking.
/// </summary>
public static class SagaRedisServiceCollectionExtensions
{
    /// <summary>
    /// Adds Redis-based distributed locking for sagas.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="connectionString">Redis connection string</param>
    /// <param name="lockExpiry">How long locks are held before auto-expiring</param>
    /// <param name="keyPrefix">Prefix for Redis lock keys</param>
    public static IServiceCollection AddRedisSagaLocking(
        this IServiceCollection services,
        string connectionString,
        TimeSpan? lockExpiry = null,
        string keyPrefix = "saga:lock:")
    {
        services.AddSingleton<IConnectionMultiplexer>(sp =>
            ConnectionMultiplexer.Connect(connectionString));

        services.Replace(ServiceDescriptor.Singleton<ISagaLockProvider>(sp =>
            new RedisSagaLockProvider(
                sp.GetRequiredService<IConnectionMultiplexer>(),
                lockExpiry,
                keyPrefix)));

        return services;
    }

    /// <summary>
    /// Adds Redis-based distributed locking for sagas using an existing connection.
    /// </summary>
    public static IServiceCollection AddRedisSagaLocking(
        this IServiceCollection services,
        IConnectionMultiplexer redis,
        TimeSpan? lockExpiry = null,
        string keyPrefix = "saga:lock:")
    {
        services.Replace(ServiceDescriptor.Singleton<ISagaLockProvider>(sp =>
            new RedisSagaLockProvider(redis, lockExpiry, keyPrefix)));

        return services;
    }
}
