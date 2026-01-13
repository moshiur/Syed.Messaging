using System.Diagnostics;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Microsoft.Extensions.Logging;

namespace Syed.Messaging.AzureServiceBus;

/// <summary>
/// Health check for Azure Service Bus transport.
/// </summary>
public class ServiceBusHealthCheck : IMessagingHealthCheck
{
    private readonly ServiceBusOptions _options;
    private readonly ILogger<ServiceBusHealthCheck> _logger;
    private bool _isStartupComplete;

    public string Name => "azureservicebus";

    public ServiceBusHealthCheck(ServiceBusOptions options, ILogger<ServiceBusHealthCheck> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task<MessagingHealthResult> CheckHealthAsync(CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            var adminClient = new ServiceBusAdministrationClient(_options.ConnectionString);

            // Check if the queue/topic exists
            var exists = await adminClient.QueueExistsAsync(_options.QueueOrTopicName, ct);

            sw.Stop();
            _isStartupComplete = true;

            if (exists)
            {
                var properties = await adminClient.GetQueueRuntimePropertiesAsync(_options.QueueOrTopicName, ct);

                var data = new Dictionary<string, object>
                {
                    ["active_messages"] = properties.Value.ActiveMessageCount,
                    ["dead_letter_messages"] = properties.Value.DeadLetterMessageCount,
                    ["scheduled_messages"] = properties.Value.ScheduledMessageCount
                };

                return new MessagingHealthResult
                {
                    IsHealthy = true,
                    Component = Name,
                    Description = $"Queue '{_options.QueueOrTopicName}' accessible",
                    Duration = sw.Elapsed,
                    Data = data
                };
            }

            // Try as topic
            var topicExists = await adminClient.TopicExistsAsync(_options.QueueOrTopicName, ct);
            if (topicExists)
            {
                return MessagingHealthResult.Healthy(Name, 
                    $"Topic '{_options.QueueOrTopicName}' accessible", 
                    sw.Elapsed);
            }

            return MessagingHealthResult.Unhealthy(Name, 
                $"Queue/Topic '{_options.QueueOrTopicName}' not found",
                duration: sw.Elapsed);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "Azure Service Bus health check failed");

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
