namespace Syed.Messaging;

public sealed class ConsumerOptions<TMessage>
{
    public string Destination { get; set; } = default!;
    public string SubscriptionName { get; set; } = default!;
    public RetryPolicy RetryPolicy { get; set; } = new();
    public int MaxConcurrency { get; set; } = 1;
}
