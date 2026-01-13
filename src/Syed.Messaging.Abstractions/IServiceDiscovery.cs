namespace Syed.Messaging;

/// <summary>
/// Interface for service discovery to resolve broker endpoints.
/// </summary>
public interface IServiceDiscovery
{
    /// <summary>
    /// Resolves the endpoints for a service.
    /// </summary>
    /// <param name="serviceName">The service name to resolve.</param>
    /// <returns>List of resolved endpoints (host:port).</returns>
    Task<IReadOnlyList<ServiceEndpoint>> ResolveAsync(string serviceName, CancellationToken ct = default);

    /// <summary>
    /// Subscribes to endpoint changes for a service.
    /// </summary>
    IAsyncEnumerable<IReadOnlyList<ServiceEndpoint>> WatchAsync(string serviceName, CancellationToken ct = default);
}

/// <summary>
/// Represents a resolved service endpoint.
/// </summary>
public record ServiceEndpoint
{
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; }
    public bool IsHealthy { get; init; } = true;
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }

    public override string ToString() => $"{Host}:{Port}";
}

/// <summary>
/// Service discovery options.
/// </summary>
public class ServiceDiscoveryOptions
{
    /// <summary>
    /// Whether to enable service discovery.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The provider to use (kubernetes, consul, dns).
    /// </summary>
    public string Provider { get; set; } = "kubernetes";

    /// <summary>
    /// Refresh interval for endpoint resolution.
    /// </summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Consul-specific options.
    /// </summary>
    public ConsulOptions? Consul { get; set; }
}

public class ConsulOptions
{
    public string Address { get; set; } = "http://localhost:8500";
    public string? Token { get; set; }
    public string? Datacenter { get; set; }
}
