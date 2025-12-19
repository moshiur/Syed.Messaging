using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;
using Syed.Messaging;
using Syed.Messaging.SignalR;
using Xunit;

namespace Syed.Messaging.Tests;

public class SignalRBridgeConsumerTests
{
    private readonly Mock<IHubContext<TestHub>> _mockHubContext;
    private readonly Mock<IHubClients> _mockClients;
    private readonly Mock<IClientProxy> _mockClientProxy;
    private readonly Mock<ISignalRBridge<TestEvent>> _mockBridge;
    private readonly Mock<ILogger<SignalRBridgeConsumer<TestEvent, TestHub>>> _mockLogger;
    private readonly SignalRBridgeConsumer<TestEvent, TestHub> _consumer;

    public SignalRBridgeConsumerTests()
    {
        _mockHubContext = new Mock<IHubContext<TestHub>>();
        _mockClients = new Mock<IHubClients>();
        _mockClientProxy = new Mock<IClientProxy>();
        _mockBridge = new Mock<ISignalRBridge<TestEvent>>();
        _mockLogger = new Mock<ILogger<SignalRBridgeConsumer<TestEvent, TestHub>>>();

        _mockHubContext.Setup(h => h.Clients).Returns(_mockClients.Object);
        _mockClients.Setup(c => c.Group(It.IsAny<string>())).Returns(_mockClientProxy.Object);

        _consumer = new SignalRBridgeConsumer<TestEvent, TestHub>(
            _mockHubContext.Object,
            _mockBridge.Object,
            _mockLogger.Object);
    }

    [Fact]
    public async Task HandleAsync_Should_Forward_Event_To_Correct_SignalR_Group()
    {
        // Arrange
        var evt = new TestEvent(Guid.NewGuid(), "test-content");
        var groupName = "user-123";
        var methodName = "ReceiveNotification";
        var payload = new { Content = evt.Content };
        var context = new MessageContext { MessageId = Guid.NewGuid().ToString() };

        _mockBridge.Setup(b => b.GetGroupName(evt)).Returns(groupName);
        _mockBridge.Setup(b => b.GetPayload(evt)).Returns(payload);
        _mockBridge.Setup(b => b.MethodName).Returns(methodName);

        // Act
        await _consumer.HandleAsync(evt, context, CancellationToken.None);

        // Assert
        _mockClients.Verify(c => c.Group(groupName), Times.Once);
        _mockClientProxy.Verify(p => p.SendCoreAsync(
            methodName, 
            It.Is<object[]>(args => args.Length == 1 && args[0] == payload), 
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_Use_Bridge_To_Determine_Group_And_Payload()
    {
        // Arrange
        var recipientId = Guid.NewGuid();
        var evt = new TestEvent(recipientId, "hello world");
        var context = new MessageContext { MessageId = Guid.NewGuid().ToString() };

        _mockBridge.Setup(b => b.GetGroupName(evt)).Returns(recipientId.ToString());
        _mockBridge.Setup(b => b.GetPayload(evt)).Returns(evt);
        _mockBridge.Setup(b => b.MethodName).Returns("TestMethod");

        // Act
        await _consumer.HandleAsync(evt, context, CancellationToken.None);

        // Assert
        _mockBridge.Verify(b => b.GetGroupName(evt), Times.Once);
        _mockBridge.Verify(b => b.GetPayload(evt), Times.Once);
        _mockBridge.VerifyGet(b => b.MethodName, Times.Once);
    }
}

// Test types
public record TestEvent(Guid RecipientId, string Content);

public class TestHub : Hub { }
