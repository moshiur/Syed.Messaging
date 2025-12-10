namespace Syed.Messaging;

public sealed class MessageContext
{
    public string MessageId { get; init; } = Guid.NewGuid().ToString();
    public string? CorrelationId { get; init; }
    public int RetryCount { get; init; }
    public IDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();
}
