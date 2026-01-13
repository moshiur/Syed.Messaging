using System.Diagnostics;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Syed.Messaging.RabbitMq;

/// <summary>
/// Health check for RabbitMQ transport.
/// </summary>
public class RabbitMqHealthCheck : IMessagingHealthCheck
{
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqHealthCheck> _logger;
    private IConnection? _connection;
    private bool _isStartupComplete;

    public string Name => "rabbitmq";

    public RabbitMqHealthCheck(RabbitMqOptions options, ILogger<RabbitMqHealthCheck> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task<MessagingHealthResult> CheckHealthAsync(CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            // Attempt to create or verify connection
            if (_connection == null || !_connection.IsOpen)
            {
                var factory = new ConnectionFactory
                {
                    Uri = new Uri(_options.ConnectionString),
                    RequestedHeartbeat = TimeSpan.FromSeconds(30)
                };

                _connection = factory.CreateConnection();
            }

            if (_connection.IsOpen)
            {
                _isStartupComplete = true;
                sw.Stop();

                return MessagingHealthResult.Healthy(Name, 
                    $"Connected to {_options.ConnectionString}", 
                    sw.Elapsed);
            }

            sw.Stop();
            return MessagingHealthResult.Unhealthy(Name, 
                "Connection not open", 
                duration: sw.Elapsed);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "RabbitMQ health check failed");

            return MessagingHealthResult.Unhealthy(Name, 
                ex.Message, 
                ex, 
                sw.Elapsed);
        }
    }

    /// <summary>
    /// Check for liveness - is the process running and not deadlocked?
    /// </summary>
    public Task<MessagingHealthResult> CheckLivenessAsync(CancellationToken ct = default)
    {
        // For liveness, we just need to respond - if we can execute this, we're alive
        return Task.FromResult(MessagingHealthResult.Healthy(Name, "Process is alive"));
    }

    /// <summary>
    /// Check for readiness - can we accept work?
    /// </summary>
    public Task<MessagingHealthResult> CheckReadinessAsync(CancellationToken ct = default)
    {
        return CheckHealthAsync(ct);
    }

    /// <summary>
    /// Check for startup - have we completed initialization?
    /// </summary>
    public Task<MessagingHealthResult> CheckStartupAsync(CancellationToken ct = default)
    {
        if (_isStartupComplete)
        {
            return Task.FromResult(MessagingHealthResult.Healthy(Name, "Startup complete"));
        }

        return Task.FromResult(MessagingHealthResult.Unhealthy(Name, "Still starting up"));
    }
}
