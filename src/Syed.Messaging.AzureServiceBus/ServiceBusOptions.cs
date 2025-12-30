namespace Syed.Messaging.AzureServiceBus;

public sealed class ServiceBusOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public string QueueOrTopicName { get; set; } = "app-messages";
    public string SubscriptionName { get; set; } = "app-subscription";

    /// <summary>
    /// Delay intervals for scheduled message retry (in seconds).
    /// Messages will be scheduled for later redelivery using ScheduledEnqueueTime.
    /// </summary>
    public int[] RetryDelaysSeconds { get; set; } = { 30, 60, 300 };

    /// <summary>
    /// Whether to use session-based messaging for saga correlation.
    /// </summary>
    public bool EnableSessions { get; set; } = false;

    /// <summary>
    /// Maximum concurrent calls to the processor.
    /// </summary>
    public int MaxConcurrentCalls { get; set; } = 16;
}
