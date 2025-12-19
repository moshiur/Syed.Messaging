using Moq;
using Syed.Messaging.RabbitMq;
using Xunit;

namespace Syed.Messaging.Tests;
public class RabbitMqBusTests
{
    private readonly Mock<IMessageTransport> _mockTransport;
    private readonly Mock<ISerializer> _mockSerializer;
    private readonly RabbitMqBus _bus;
    public RabbitMqBusTests()
    {
        _mockTransport = new Mock<IMessageTransport>();
        _mockSerializer = new Mock<ISerializer>();
        _bus = new RabbitMqBus(_mockTransport.Object, _mockSerializer.Object);
    }

    [Fact]
    public async Task RequestAsync_Should_Send_Request_And_Return_Response()
    {
        // Arrange
        var request = new TestRequest("Test");
        var response = new TestResponse("Response");
        var destination = "test-queue";
        var correlationId = Guid.NewGuid().ToString();
        var requestBody = System.Text.Encoding.UTF8.GetBytes("request-json");
        var responseBody = System.Text.Encoding.UTF8.GetBytes("response-json");

        _mockSerializer.Setup(s => s.Serialize(request)).Returns(requestBody);
        _mockSerializer.Setup(s => s.Deserialize<TestResponse>(responseBody)).Returns(response);

        _mockTransport.Setup(t => t.RequestAsync(It.IsAny<IMessageEnvelope>(), destination, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MessageEnvelope
            {
                Body = responseBody,
                CorrelationId = correlationId
            });

        // Act
        var result = await _bus.RequestAsync<TestRequest, TestResponse>(destination, request);

        // Assert
        Assert.Equal(response, result);
        _mockTransport.Verify(t => t.RequestAsync(It.Is<IMessageEnvelope>(e => 
            e.Body == requestBody && 
            e.Headers.ContainsKey("correlation-id") &&
            e.Headers.ContainsKey("message-id")), destination, It.IsAny<CancellationToken>()), Times.Once);
    }
}

public record TestRequest(string Data);
public record TestResponse(string Data);
