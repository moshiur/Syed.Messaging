using System.Diagnostics;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;

namespace Syed.Messaging.Kafka;

/// <summary>
/// Health check for Kafka transport.
/// </summary>
public class KafkaHealthCheck : IMessagingHealthCheck
{
    private readonly KafkaOptions _options;
    private readonly ILogger<KafkaHealthCheck> _logger;
    private bool _isStartupComplete;

    public string Name => "kafka";

    public KafkaHealthCheck(KafkaOptions options, ILogger<KafkaHealthCheck> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task<MessagingHealthResult> CheckHealthAsync(CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            var config = new AdminClientConfig
            {
                BootstrapServers = _options.BootstrapServers
            };

            using var adminClient = new AdminClientBuilder(config).Build();

            // Get cluster metadata to verify connectivity
            var metadata = adminClient.GetMetadata(TimeSpan.FromSeconds(5));

            sw.Stop();
            _isStartupComplete = true;

            var data = new Dictionary<string, object>
            {
                ["brokers"] = metadata.Brokers.Count,
                ["topics"] = metadata.Topics.Count,
                ["cluster_id"] = metadata.OriginatingBrokerId
            };

            return new MessagingHealthResult
            {
                IsHealthy = true,
                Component = Name,
                Description = $"Connected to {metadata.Brokers.Count} broker(s)",
                Duration = sw.Elapsed,
                Data = data
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "Kafka health check failed");

            return MessagingHealthResult.Unhealthy(Name, 
                ex.Message, 
                ex, 
                sw.Elapsed);
        }
    }

    public Task<MessagingHealthResult> CheckLivenessAsync(CancellationToken ct = default)
    {
        return Task.FromResult(MessagingHealthResult.Healthy(Name, "Process is alive"));
    }

    public Task<MessagingHealthResult> CheckReadinessAsync(CancellationToken ct = default)
    {
        return CheckHealthAsync(ct);
    }

    public Task<MessagingHealthResult> CheckStartupAsync(CancellationToken ct = default)
    {
        if (_isStartupComplete)
        {
            return Task.FromResult(MessagingHealthResult.Healthy(Name, "Startup complete"));
        }

        return Task.FromResult(MessagingHealthResult.Unhealthy(Name, "Still starting up"));
    }
}
