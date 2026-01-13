using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Syed.Messaging.HealthChecks;

/// <summary>
/// Extension methods for adding messaging health checks to ASP.NET Core.
/// </summary>
public static class HealthCheckBuilderExtensions
{
    /// <summary>
    /// Adds all registered IMessagingHealthCheck instances as ASP.NET Core health checks.
    /// </summary>
    public static IHealthChecksBuilder AddMessagingHealthChecks(
        this IHealthChecksBuilder builder,
        string? name = null,
        HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null)
    {
        builder.Services.AddSingleton<IHealthCheck>(sp =>
        {
            var healthChecks = sp.GetServices<IMessagingHealthCheck>();
            return new CompositeMessagingHealthCheck(healthChecks);
        });

        return builder.Add(new HealthCheckRegistration(
            name ?? "messaging",
            sp => sp.GetRequiredService<IHealthCheck>(),
            failureStatus,
            tags));
    }

    /// <summary>
    /// Adds a specific messaging health check.
    /// </summary>
    public static IHealthChecksBuilder AddMessagingHealthCheck<THealthCheck>(
        this IHealthChecksBuilder builder,
        string name,
        HealthCheckType checkType = HealthCheckType.Readiness,
        HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null)
        where THealthCheck : class, IMessagingHealthCheck
    {
        builder.Services.AddSingleton<THealthCheck>();

        return builder.Add(new HealthCheckRegistration(
            name,
            sp =>
            {
                var check = sp.GetRequiredService<THealthCheck>();
                return new MessagingHealthCheckAdapter(check, checkType);
            },
            failureStatus,
            tags));
    }

    /// <summary>
    /// Adds liveness probe endpoint for Kubernetes.
    /// </summary>
    public static IHealthChecksBuilder AddMessagingLiveness(
        this IHealthChecksBuilder builder,
        string name = "messaging-liveness")
    {
        return builder.Add(new HealthCheckRegistration(
            name,
            sp =>
            {
                var healthChecks = sp.GetServices<IMessagingHealthCheck>();
                return new CompositeMessagingHealthCheck(healthChecks, HealthCheckType.Liveness);
            },
            HealthStatus.Unhealthy,
            new[] { "liveness", "k8s" }));
    }

    /// <summary>
    /// Adds readiness probe endpoint for Kubernetes.
    /// </summary>
    public static IHealthChecksBuilder AddMessagingReadiness(
        this IHealthChecksBuilder builder,
        string name = "messaging-readiness")
    {
        return builder.Add(new HealthCheckRegistration(
            name,
            sp =>
            {
                var healthChecks = sp.GetServices<IMessagingHealthCheck>();
                return new CompositeMessagingHealthCheck(healthChecks, HealthCheckType.Readiness);
            },
            HealthStatus.Unhealthy,
            new[] { "readiness", "k8s" }));
    }

    /// <summary>
    /// Adds startup probe endpoint for Kubernetes.
    /// </summary>
    public static IHealthChecksBuilder AddMessagingStartup(
        this IHealthChecksBuilder builder,
        string name = "messaging-startup")
    {
        return builder.Add(new HealthCheckRegistration(
            name,
            sp =>
            {
                var healthChecks = sp.GetServices<IMessagingHealthCheck>();
                return new CompositeMessagingHealthCheck(healthChecks, HealthCheckType.Startup);
            },
            HealthStatus.Unhealthy,
            new[] { "startup", "k8s" }));
    }
}

/// <summary>
/// Composite health check that aggregates multiple messaging health checks.
/// </summary>
internal class CompositeMessagingHealthCheck : IHealthCheck
{
    private readonly IEnumerable<IMessagingHealthCheck> _healthChecks;
    private readonly HealthCheckType _checkType;

    public CompositeMessagingHealthCheck(IEnumerable<IMessagingHealthCheck> healthChecks, HealthCheckType checkType = HealthCheckType.Readiness)
    {
        _healthChecks = healthChecks;
        _checkType = checkType;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var results = new List<MessagingHealthResult>();
        var allHealthy = true;

        foreach (var check in _healthChecks)
        {
            var result = await check.CheckHealthAsync(cancellationToken);
            results.Add(result);

            if (!result.IsHealthy)
            {
                allHealthy = false;
            }
        }

        var data = new Dictionary<string, object>
        {
            ["check_type"] = _checkType.ToString(),
            ["checks"] = results.Select(r => new
            {
                r.Component,
                r.IsHealthy,
                r.Description,
                DurationMs = r.Duration.TotalMilliseconds
            }).ToList()
        };

        if (allHealthy)
        {
            return HealthCheckResult.Healthy($"All {results.Count} messaging components healthy", data);
        }

        var unhealthy = results.Where(r => !r.IsHealthy).Select(r => r.Component);
        return HealthCheckResult.Unhealthy($"Unhealthy components: {string.Join(", ", unhealthy)}", data: data);
    }
}
