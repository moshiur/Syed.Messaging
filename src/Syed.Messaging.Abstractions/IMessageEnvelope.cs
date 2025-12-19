namespace Syed.Messaging;

public interface IMessageEnvelope
{
    string MessageType { get; }
    string? MessageId { get; }
    string? CorrelationId { get; }
    string? CausationId { get; }
    string? ReplyTo { get; }
    IDictionary<string, string> Headers { get; }
    byte[] Body { get; }
}

public sealed class MessageEnvelope : IMessageEnvelope
{
    public string MessageType { get; init; } = default!;
    public string? MessageId { get; init; }
    public string? CorrelationId { get; init; }
    public string? CausationId { get; init; }
    public string? ReplyTo { get; init; }
    public IDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();
    public byte[] Body { get; init; } = Array.Empty<byte>();
}
