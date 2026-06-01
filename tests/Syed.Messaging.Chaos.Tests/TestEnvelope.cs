namespace Syed.Messaging.Chaos.Tests;

/// <summary>Minimal IMessageEnvelope for test use.</summary>
internal sealed class TestEnvelope : IMessageEnvelope
{
    public string MessageType { get; init; } = "test.event";
    public string? MessageVersion { get; init; }
    public string? MessageId { get; init; } = Guid.NewGuid().ToString();
    public string? CorrelationId { get; init; }
    public string? CausationId { get; init; }
    public string? ReplyTo { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public IDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();
    public byte[] Body { get; init; } = [1, 2, 3];
}
