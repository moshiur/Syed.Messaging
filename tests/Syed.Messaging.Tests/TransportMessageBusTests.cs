using FluentAssertions;
using Moq;
using Xunit;

namespace Syed.Messaging.Tests;

public class TransportMessageBusTests
{
    private readonly Mock<IMessageTransport> _transport = new();
    private readonly Mock<ISerializer> _serializer = new();
    private readonly MessageTypeRegistry _registry = new();

    [Fact]
    public async Task PublishAsync_ShouldUseRegisteredMessageTypeMetadata()
    {
        var payload = new byte[] { 1, 2, 3 };
        var message = new BusTestMessage("hello");
        IMessageEnvelope? envelope = null;

        _registry.Register<BusTestMessage>("bus.test", "v2");
        _serializer.Setup(x => x.Serialize(message)).Returns(payload);
        _transport
            .Setup(x => x.PublishAsync(It.IsAny<IMessageEnvelope>(), "destination", It.IsAny<CancellationToken>()))
            .Callback<IMessageEnvelope, string, CancellationToken>((e, _, _) => envelope = e)
            .Returns(Task.CompletedTask);

        var bus = CreateBus();

        await bus.PublishAsync("destination", message);

        envelope.Should().NotBeNull();
        envelope!.MessageType.Should().Be("bus.test");
        envelope.MessageVersion.Should().Be("v2");
        envelope.Body.Should().BeSameAs(payload);
        envelope.MessageId.Should().NotBeNullOrWhiteSpace();
        envelope.CorrelationId.Should().NotBeNullOrWhiteSpace();
        envelope.Headers["message-id"].Should().Be(envelope.MessageId);
        envelope.Headers["correlation-id"].Should().Be(envelope.CorrelationId);
        envelope.Headers["message-type"].Should().Be("bus.test");
        envelope.Headers["message-version"].Should().Be("v2");
    }

    [Fact]
    public async Task PublishRawAsync_ShouldCopyHeadersAndAddEnvelopeMetadata()
    {
        var payload = new byte[] { 4, 5, 6 };
        var headers = new Dictionary<string, string>
        {
            ["tenant-id"] = "tenant-1",
            ["message-version"] = "v1"
        };
        IMessageEnvelope? envelope = null;

        _transport
            .Setup(x => x.PublishAsync(It.IsAny<IMessageEnvelope>(), "raw.destination", It.IsAny<CancellationToken>()))
            .Callback<IMessageEnvelope, string, CancellationToken>((e, _, _) => envelope = e)
            .Returns(Task.CompletedTask);

        var bus = CreateBus();

        await bus.PublishRawAsync("raw.destination", payload, "raw.type", headers);

        headers.Should().NotContainKey("message-id");
        headers.Should().NotContainKey("correlation-id");
        envelope.Should().NotBeNull();
        envelope!.MessageType.Should().Be("raw.type");
        envelope.MessageVersion.Should().Be("v1");
        envelope.Headers["tenant-id"].Should().Be("tenant-1");
        envelope.Headers["message-type"].Should().Be("raw.type");
        envelope.Headers["message-version"].Should().Be("v1");
        envelope.Headers["message-id"].Should().Be(envelope.MessageId);
        envelope.Headers["correlation-id"].Should().Be(envelope.CorrelationId);
    }

    [Fact]
    public async Task RequestAsync_ShouldSendEnvelopeAndDeserializeResponse()
    {
        var request = new BusTestRequest("request");
        var response = new BusTestResponse("response");
        var requestBody = new byte[] { 7 };
        var responseBody = new byte[] { 8 };
        IMessageEnvelope? envelope = null;

        _registry.Register<BusTestRequest>("bus.request", "v1");
        _serializer.Setup(x => x.Serialize(request)).Returns(requestBody);
        _serializer.Setup(x => x.Deserialize<BusTestResponse>(responseBody)).Returns(response);
        _transport
            .Setup(x => x.RequestAsync(It.IsAny<IMessageEnvelope>(), "rpc.destination", It.IsAny<CancellationToken>()))
            .Callback<IMessageEnvelope, string, CancellationToken>((e, _, _) => envelope = e)
            .ReturnsAsync(new MessageEnvelope { Body = responseBody });

        var bus = CreateBus();

        var result = await bus.RequestAsync<BusTestRequest, BusTestResponse>("rpc.destination", request);

        result.Should().Be(response);
        envelope.Should().NotBeNull();
        envelope!.MessageType.Should().Be("bus.request");
        envelope.MessageVersion.Should().Be("v1");
        envelope.CorrelationId.Should().NotBeNullOrWhiteSpace();
        envelope.Headers["correlation-id"].Should().Be(envelope.CorrelationId);
    }

    private TransportMessageBus CreateBus()
        => new(_transport.Object, _serializer.Object, _registry);

    private sealed record BusTestMessage(string Value);
    private sealed record BusTestRequest(string Value);
    private sealed record BusTestResponse(string Value);
}
