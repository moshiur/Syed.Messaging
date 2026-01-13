using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Syed.Messaging.Configuration;

/// <summary>
/// Extension methods for mesh-compatible configuration.
/// </summary>
public static class MeshConfigurationExtensions
{
    /// <summary>
    /// Adds mesh configuration from IConfiguration (ConfigMap/Secrets support).
    /// </summary>
    public static IServiceCollection AddMeshConfiguration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<MeshOptions>(configuration.GetSection("Syed:Mesh"));
        services.Configure<ServiceDiscoveryOptions>(configuration.GetSection("Syed:ServiceDiscovery"));

        return services;
    }

    /// <summary>
    /// Adds mesh configuration with custom options.
    /// </summary>
    public static IServiceCollection AddMeshConfiguration(
        this IServiceCollection services,
        Action<MeshOptions> configure)
    {
        services.Configure(configure);
        return services;
    }

    /// <summary>
    /// Enables hot reload of configuration (for ConfigMap changes without restart).
    /// </summary>
    public static IServiceCollection AddMeshConfigurationWithHotReload(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Configure with change token support for hot reload
        services.Configure<MeshOptions>(configuration.GetSection("Syed:Mesh"));
        services.Configure<ServiceDiscoveryOptions>(configuration.GetSection("Syed:ServiceDiscovery"));
        services.Configure<MessagingFeatureFlags>(configuration.GetSection("Syed:FeatureFlags"));

        return services;
    }
}

/// <summary>
/// Feature flags for message routing and processing.
/// </summary>
public class MessagingFeatureFlags
{
    /// <summary>
    /// Enable/disable specific message handlers by type.
    /// </summary>
    public Dictionary<string, bool> HandlerEnabled { get; set; } = new();

    /// <summary>
    /// Route messages to specific destinations based on type.
    /// </summary>
    public Dictionary<string, string> MessageRouting { get; set; } = new();

    /// <summary>
    /// Enable shadow/canary traffic for specific message types.
    /// </summary>
    public Dictionary<string, ShadowTrafficOptions> ShadowTraffic { get; set; } = new();
}

public class ShadowTrafficOptions
{
    /// <summary>
    /// Percentage of traffic to shadow (0-100).
    /// </summary>
    public int Percentage { get; set; } = 0;

    /// <summary>
    /// Target destination for shadow traffic.
    /// </summary>
    public string? TargetDestination { get; set; }
}

/// <summary>
/// Extension methods for feature flags.
/// </summary>
public static class FeatureFlagExtensions
{
    public static IServiceCollection AddMessagingFeatureFlags(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<MessagingFeatureFlags>(configuration.GetSection("Syed:FeatureFlags"));
        return services;
    }
}
