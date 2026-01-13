using Microsoft.Extensions.DependencyInjection;

namespace Syed.Messaging.ServiceDiscovery;

/// <summary>
/// Extension methods for configuring service discovery.
/// </summary>
public static class ServiceDiscoveryExtensions
{
    /// <summary>
    /// Adds Kubernetes DNS-based service discovery.
    /// </summary>
    public static IServiceCollection AddKubernetesServiceDiscovery(
        this IServiceCollection services,
        Action<ServiceDiscoveryOptions>? configure = null)
    {
        var options = new ServiceDiscoveryOptions { Provider = "kubernetes" };
        configure?.Invoke(options);

        services.Configure<ServiceDiscoveryOptions>(o =>
        {
            o.Provider = options.Provider;
            o.RefreshInterval = options.RefreshInterval;
            o.Enabled = options.Enabled;
        });

        services.AddSingleton<IServiceDiscovery, KubernetesDnsServiceDiscovery>();

        return services;
    }

    /// <summary>
    /// Adds Consul-based service discovery.
    /// </summary>
    public static IServiceCollection AddConsulServiceDiscovery(
        this IServiceCollection services,
        Action<ServiceDiscoveryOptions>? configure = null)
    {
        var options = new ServiceDiscoveryOptions { Provider = "consul" };
        configure?.Invoke(options);

        services.Configure<ServiceDiscoveryOptions>(o =>
        {
            o.Provider = options.Provider;
            o.RefreshInterval = options.RefreshInterval;
            o.Enabled = options.Enabled;
            o.Consul = options.Consul;
        });

        services.AddSingleton<IServiceDiscovery, ConsulServiceDiscovery>();

        return services;
    }

    /// <summary>
    /// Adds standard DNS-based service discovery (fallback).
    /// </summary>
    public static IServiceCollection AddDnsServiceDiscovery(
        this IServiceCollection services,
        Action<ServiceDiscoveryOptions>? configure = null)
    {
        var options = new ServiceDiscoveryOptions { Provider = "dns" };
        configure?.Invoke(options);

        services.Configure<ServiceDiscoveryOptions>(o =>
        {
            o.Provider = options.Provider;
            o.RefreshInterval = options.RefreshInterval;
            o.Enabled = options.Enabled;
        });

        services.AddSingleton<IServiceDiscovery, DnsServiceDiscovery>();

        return services;
    }
}
