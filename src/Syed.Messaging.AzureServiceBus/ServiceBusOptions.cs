namespace Syed.Messaging.AzureServiceBus;

public sealed class ServiceBusOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public string QueueOrTopicName { get; set; } = "app-messages";
    public string SubscriptionName { get; set; } = "app-subscription";
}
