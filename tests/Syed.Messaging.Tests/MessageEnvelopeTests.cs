using Syed.Messaging;

namespace Syed.Messaging.Tests;

public class MessageEnvelopeTests
{
    [Fact]
    public void MessageEnvelope_DefaultValues_AreCorrect()
    {
        // Act
        var envelope = new MessageEnvelope();

        // Assert
        envelope.MessageType.Should().BeNull();
        envelope.MessageVersion.Should().BeNull();
        envelope.MessageId.Should().BeNull();
        envelope.CorrelationId.Should().BeNull();
        envelope.CausationId.Should().BeNull();
        envelope.Timestamp.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
        envelope.Headers.Should().NotBeNull().And.BeEmpty();
        envelope.Body.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void MessageEnvelope_InitProperties_SetsValues()
    {
        // Arrange
        var timestamp = DateTimeOffset.UtcNow.AddMinutes(-5);
        var body = new byte[] { 1, 2, 3 };
        var headers = new Dictionary<string, string> { { "key", "value" } };

        // Act
        var envelope = new MessageEnvelope
        {
            MessageType = "TestMessage",
            MessageVersion = "v2",
            MessageId = "msg-123",
            CorrelationId = "corr-456",
            CausationId = "cause-789",
            Timestamp = timestamp,
            Headers = headers,
            Body = body
        };

        // Assert
        envelope.MessageType.Should().Be("TestMessage");
        envelope.MessageVersion.Should().Be("v2");
        envelope.MessageId.Should().Be("msg-123");
        envelope.CorrelationId.Should().Be("corr-456");
        envelope.CausationId.Should().Be("cause-789");
        envelope.Timestamp.Should().Be(timestamp);
        envelope.Headers.Should().ContainKey("key").WhoseValue.Should().Be("value");
        envelope.Body.Should().BeEquivalentTo(body);
    }

    [Fact]
    public void MessageEnvelope_ImplementsIMessageEnvelope()
    {
        // Arrange
        var envelope = new MessageEnvelope
        {
            MessageType = "Test",
            MessageVersion = "v1"
        };

        // Act
        IMessageEnvelope iface = envelope;

        // Assert
        iface.MessageType.Should().Be("Test");
        iface.MessageVersion.Should().Be("v1");
    }

    [Fact]
    public void MessageEnvelope_Headers_CanBeModified()
    {
        // Arrange
        var envelope = new MessageEnvelope();

        // Act
        envelope.Headers["custom-header"] = "custom-value";

        // Assert
        envelope.Headers.Should().ContainKey("custom-header");
        envelope.Headers["custom-header"].Should().Be("custom-value");
    }
}
