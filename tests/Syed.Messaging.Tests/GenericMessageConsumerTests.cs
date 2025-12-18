using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Syed.Messaging.Core;
using Xunit;

namespace Syed.Messaging.Tests;

public class GenericMessageConsumerTests
{
    private readonly Mock<IMessageTransport> _transportMock;
    private readonly Mock<IServiceScopeFactory> _scopeFactoryMock;
    private readonly Mock<IServiceScope> _scopeMock;
    private readonly Mock<IServiceProvider> _serviceProviderMock;
    private readonly Mock<ISerializer> _serializerMock;
    private readonly Mock<ILogger<GenericMessageConsumer<TestMessage>>> _loggerMock;
    private readonly Mock<IInboxStore> _inboxStoreMock;
    private readonly Mock<IMessageHandler<TestMessage>> _handlerMock;

    public class TestMessage { public string Data { get; set; } }

    public GenericMessageConsumerTests()
    {
        _transportMock = new Mock<IMessageTransport>();
        _scopeFactoryMock = new Mock<IServiceScopeFactory>();
        _scopeMock = new Mock<IServiceScope>();
        _serviceProviderMock = new Mock<IServiceProvider>();
        _serializerMock = new Mock<ISerializer>();
        _loggerMock = new Mock<ILogger<GenericMessageConsumer<TestMessage>>>();
        
        _inboxStoreMock = new Mock<IInboxStore>();
        _handlerMock = new Mock<IMessageHandler<TestMessage>>();

        // Setup Scope Factory
        _scopeFactoryMock.Setup(x => x.CreateScope()).Returns(_scopeMock.Object);
        _scopeMock.Setup(x => x.ServiceProvider).Returns(_serviceProviderMock.Object);

        // Setup Service Provider to return Handler
        _serviceProviderMock.Setup(x => x.GetService(typeof(IMessageHandler<TestMessage>)))
            .Returns(_handlerMock.Object);
            
        // Setup Service Provider to return IInboxStore (optional)
        // By default returning null unless specific test sets it up
        _serviceProviderMock.Setup(x => x.GetService(typeof(IInboxStore)))
            .Returns(null); // Default no inbox
    }

    [Fact]
    public async Task HandleAsync_ShouldSkipProcessing_WhenInboxSaysProcessed()
    {
        // Arrange
        var messageId = "msg-123";
        _serviceProviderMock.Setup(x => x.GetService(typeof(IInboxStore)))
            .Returns(_inboxStoreMock.Object);

        _inboxStoreMock.Setup(x => x.HasBeenProcessedAsync(messageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var consumer = CreateConsumer();
        var envelope = new MessageEnvelope 
        { 
            MessageId = messageId, 
            MessageType = "TestMessage",
            Body = new byte[0] 
        };

        // Invoke private/protected method via reflection or just use the public Subscribe logic?
        // GenericMessageConsumer.ExecuteAsync calls SubscribeAsync which takes a delegate.
        // We can't access that delegate easily without starting the service or refactoring.
        // However, we can use reflection to invoke 'HandleEnvelopeAsync' if it's private.
        // Or we can mock the transport's SubscribeAsync and capture the delegate.
        
        Func<IMessageEnvelope, CancellationToken, Task<TransportAcknowledge>>? capturedHandler = null;
        _transportMock.Setup(x => x.SubscribeAsync(
                It.IsAny<string>(), 
                It.IsAny<string>(), 
                It.IsAny<Func<IMessageEnvelope, CancellationToken, Task<TransportAcknowledge>>>(), 
                It.IsAny<CancellationToken>()))
            .Callback<string, string, Func<IMessageEnvelope, CancellationToken, Task<TransportAcknowledge>>, CancellationToken>(
                (sub, dest, handler, ct) => capturedHandler = handler);

        // Act - Start to register subscription
        await consumer.StartAsync(CancellationToken.None);
        
        // Assert subscription happened
        capturedHandler.Should().NotBeNull();
        
        // Act - Invoke handler
        var result = await capturedHandler!(envelope, CancellationToken.None);

        // Assert
        result.Should().Be(TransportAcknowledge.Ack);
        _handlerMock.Verify(x => x.HandleAsync(It.IsAny<TestMessage>(), It.IsAny<MessageContext>(), It.IsAny<CancellationToken>()), Times.Never);
        _loggerMock.VerifyLog(LogLevel.Information, $"Message {messageId} (TestMessage) already processed. Skipping.");
    }

    [Fact]
    public async Task HandleAsync_ShouldProcessAndMarkInbox_WhenNotProcessed()
    {
        // Arrange
        var messageId = "msg-456";
        _serviceProviderMock.Setup(x => x.GetService(typeof(IInboxStore)))
           .Returns(_inboxStoreMock.Object);

        _inboxStoreMock.Setup(x => x.HasBeenProcessedAsync(messageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _serializerMock.Setup(x => x.Deserialize<TestMessage>(It.IsAny<byte[]>()))
            .Returns(new TestMessage());

        var consumer = CreateConsumer();
        var envelope = new MessageEnvelope 
        { 
            MessageId = messageId, 
            MessageType = "TestMessage",
            Body = new byte[0] 
        };

        Func<IMessageEnvelope, CancellationToken, Task<TransportAcknowledge>>? capturedHandler = null;
        _transportMock.Setup(x => x.SubscribeAsync(
                It.IsAny<string>(), 
                It.IsAny<string>(), 
                It.IsAny<Func<IMessageEnvelope, CancellationToken, Task<TransportAcknowledge>>>(), 
                It.IsAny<CancellationToken>()))
            .Callback<string, string, Func<IMessageEnvelope, CancellationToken, Task<TransportAcknowledge>>, CancellationToken>(
                (sub, dest, handler, ct) => capturedHandler = handler);

        await consumer.StartAsync(CancellationToken.None);

        // Act
        var result = await capturedHandler!(envelope, CancellationToken.None);

        // Assert
        result.Should().Be(TransportAcknowledge.Ack);
        _handlerMock.Verify(x => x.HandleAsync(It.IsAny<TestMessage>(), It.IsAny<MessageContext>(), It.IsAny<CancellationToken>()), Times.Once);
        _inboxStoreMock.Verify(x => x.MarkProcessedAsync(messageId, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    private GenericMessageConsumer<TestMessage> CreateConsumer()
    {
        return new GenericMessageConsumer<TestMessage>(
            _transportMock.Object,
            _scopeFactoryMock.Object,
            _serializerMock.Object,
            new ConsumerOptions<TestMessage> { SubscriptionName = "test", Destination = "test" },
            _loggerMock.Object);
    }
}

// Helper extension for logger mock verification
public static class LoggerMockExtensions
{
    public static void VerifyLog<T>(this Mock<ILogger<T>> logger, LogLevel level, string messageContains)
    {
        logger.Verify(x => x.Log(
            level,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString().Contains(messageContains)),
            It.IsAny<Exception>(),
            (Func<It.IsAnyType, Exception, string>)It.IsAny<object>()), 
            Times.AtLeastOnce,
            $"Expected log message containing '{messageContains}'");
    }
}
