using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Syed.Messaging.HealthChecks;

/// <summary>
/// Adapts IMessagingHealthCheck to ASP.NET Core IHealthCheck.
/// </summary>
public class MessagingHealthCheckAdapter : IHealthCheck
{
    private readonly IMessagingHealthCheck _healthCheck;
    private readonly HealthCheckType _checkType;

    public MessagingHealthCheckAdapter(IMessagingHealthCheck healthCheck, HealthCheckType checkType = HealthCheckType.Readiness)
    {
        _healthCheck = healthCheck;
        _checkType = checkType;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var result = await _healthCheck.CheckHealthAsync(cancellationToken);

        var data = new Dictionary<string, object>
        {
            ["component"] = result.Component,
            ["duration_ms"] = result.Duration.TotalMilliseconds,
            ["check_type"] = _checkType.ToString()
        };

        if (result.Data != null)
        {
            foreach (var kv in result.Data)
            {
                data[kv.Key] = kv.Value;
            }
        }

        if (result.IsHealthy)
        {
            return HealthCheckResult.Healthy(result.Description, data);
        }

        return HealthCheckResult.Unhealthy(result.Description, result.Exception, data);
    }
}
