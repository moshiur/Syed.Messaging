using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Polly;
using Polly.Retry;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Syed.Messaging.Core;
using Xunit;

namespace Syed.Messaging.RabbitMq.Tests;


public class RabbitMqTransportTests
{
    private readonly Mock<IConnectionFactory> _connectionFactoryMock;
    private readonly Mock<IConnection> _connectionMock;
    private readonly Mock<IModel> _modelMock;
    private readonly Mock<ILogger<RabbitMqTransport>> _loggerMock;
    private readonly RabbitMqOptions _options;
    private readonly IBasicProperties _basicProperties;

    // Subject Under Test
    private RabbitMqTransport _sut;

    public RabbitMqTransportTests()
    {
        _connectionFactoryMock = new Mock<IConnectionFactory>();
        _connectionMock = new Mock<IConnection>();
        _modelMock = new Mock<IModel>();
        _loggerMock = new Mock<ILogger<RabbitMqTransport>>();
        _basicProperties = Mock.Of<IBasicProperties>();

        _options = new RabbitMqOptions
        {
            ConnectionString = "amqp://localhost",
            MainExchangeName = "test-exchange",
            MainQueueName = "test-queue"
        };

        // Setup mock chain
        _connectionFactoryMock.Setup(x => x.CreateConnection()).Returns(_connectionMock.Object);
        _connectionMock.Setup(x => x.CreateModel()).Returns(_modelMock.Object);
        _modelMock.Setup(x => x.CreateBasicProperties()).Returns(_basicProperties);
        _modelMock.Setup(x => x.ConfirmSelect()); // Verify this is called

        // Initialize SUT
        _sut = new RabbitMqTransport(
            _options, 
            _loggerMock.Object, 
            resiliencePipeline: null, 
            connectionFactory: _connectionFactoryMock.Object);
    }

    [Fact]
    public void Constructor_ShouldEnablePublisherConfirms()
    {
        // Assert
        _modelMock.Verify(x => x.ConfirmSelect(), Times.Once, "ConfirmSelect should be called to enable publisher confirms");
    }

    [Fact]
    public async Task PublishAsync_ShouldWaitForConfirms()
    {
        // Arrange
        var envelope = new MessageEnvelope 
        { 
            MessageType = "TestMessage", 
            Body = Encoding.UTF8.GetBytes("test payload"),
            Timestamp = DateTimeOffset.UtcNow 
        };

        _modelMock.Setup(x => x.WaitForConfirmsOrDie(It.IsAny<TimeSpan>()));

        // Act
        await _sut.PublishAsync(envelope, "test-routing-key", CancellationToken.None);

        // Assert
        _modelMock.Verify(x => x.BasicPublish(
            _options.MainExchangeName, 
            "test-routing-key", 
            It.IsAny<bool>(), // mandatory
            It.IsAny<IBasicProperties>(), 
            It.IsAny<ReadOnlyMemory<byte>>()), Times.Once);

        _modelMock.Verify(x => x.WaitForConfirmsOrDie(It.IsAny<TimeSpan>()), Times.Once, "Should wait for broker confirmation");
    }

    [Fact]
    public async Task PublishAsync_ShouldThrow_WhenConfirmsFail()
    {
        // Arrange
        var envelope = new MessageEnvelope 
        { 
            MessageType = "TestMessage", 
            Body = Encoding.UTF8.GetBytes("test payload"),
            Timestamp = DateTimeOffset.UtcNow
        };

        _modelMock.Setup(x => x.WaitForConfirmsOrDie(It.IsAny<TimeSpan>()))
                  .Throws(new IOException("NACK received"));

        // Act
        var act = async () => await _sut.PublishAsync(envelope, "test-routing-key", CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<IOException>().WithMessage("NACK received");
    }
}
