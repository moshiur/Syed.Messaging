namespace Syed.Messaging;

/// <summary>
/// Health check result for messaging components.
/// </summary>
public record MessagingHealthResult
{
    public bool IsHealthy { get; init; }
    public string Component { get; init; } = string.Empty;
    public string? Description { get; init; }
    public TimeSpan Duration { get; init; }
    public IReadOnlyDictionary<string, object>? Data { get; init; }
    public Exception? Exception { get; init; }

    public static MessagingHealthResult Healthy(string component, string? description = null, TimeSpan duration = default)
        => new() { IsHealthy = true, Component = component, Description = description ?? "Healthy", Duration = duration };

    public static MessagingHealthResult Unhealthy(string component, string description, Exception? exception = null, TimeSpan duration = default)
        => new() { IsHealthy = false, Component = component, Description = description, Exception = exception, Duration = duration };
}

/// <summary>
/// Interface for messaging component health checks.
/// </summary>
public interface IMessagingHealthCheck
{
    /// <summary>
    /// The name of the health check.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Checks if the messaging component is healthy.
    /// </summary>
    Task<MessagingHealthResult> CheckHealthAsync(CancellationToken ct = default);
}

/// <summary>
/// Health check types for Kubernetes probes.
/// </summary>
public enum HealthCheckType
{
    /// <summary>Is the process alive?</summary>
    Liveness,

    /// <summary>Can the service handle requests?</summary>
    Readiness,

    /// <summary>Has the service finished starting up?</summary>
    Startup
}
