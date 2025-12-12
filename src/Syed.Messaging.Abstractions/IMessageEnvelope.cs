namespace Syed.Messaging;

public interface IMessageEnvelope
{
    string MessageType { get; }
    string? MessageVersion { get; }
    string? MessageId { get; }
    string? CorrelationId { get; }
    string? CausationId { get; }
    DateTimeOffset Timestamp { get; }
    IDictionary<string, string> Headers { get; }
    byte[] Body { get; }
}

public sealed class MessageEnvelope : IMessageEnvelope
{
    public string MessageType { get; init; } = default!;
    public string? MessageVersion { get; init; }
    public string? MessageId { get; init; }
    public string? CorrelationId { get; init; }
    public string? CausationId { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public IDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();
    public byte[] Body { get; init; } = Array.Empty<byte>();
}

