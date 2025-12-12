using Moq;
using Syed.Messaging;
using Syed.Messaging.Sagas;

namespace Syed.Messaging.Sagas.Tests;

public class SagaTimeoutSchedulerTests
{
    private readonly Mock<ISagaTimeoutStore> _storeMock;
    private readonly Mock<ISerializer> _serializerMock;
    private readonly Mock<IMessageTypeRegistry> _registryMock;
    private readonly SagaTimeoutScheduler _scheduler;

    public SagaTimeoutSchedulerTests()
    {
        _storeMock = new Mock<ISagaTimeoutStore>();
        _serializerMock = new Mock<ISerializer>();
        _registryMock = new Mock<IMessageTypeRegistry>();
        
        _scheduler = new SagaTimeoutScheduler(
            _storeMock.Object,
            _serializerMock.Object,
            _registryMock.Object);
    }

    [Fact]
    public async Task ScheduleAsync_WithRegisteredType_UsesRegistryTypeKey()
    {
        // Arrange
        _registryMock
            .Setup(r => r.TryGetTypeKey(typeof(TestTimeout), out It.Ref<string?>.IsAny, out It.Ref<string?>.IsAny))
            .Callback(new TryGetTypeKeyCallback((Type t, out string? k, out string? v) =>
            {
                k = "test-timeout";
                v = "v1";
            }))
            .Returns(true);
        
        _serializerMock.Setup(s => s.Serialize(It.IsAny<TestTimeout>())).Returns(new byte[] { 1, 2, 3 });
        
        SagaTimeoutRequest? capturedRequest = null;
        _storeMock
            .Setup(s => s.ScheduleAsync(It.IsAny<SagaTimeoutRequest>(), It.IsAny<CancellationToken>()))
            .Callback<SagaTimeoutRequest, CancellationToken>((r, _) => capturedRequest = r)
            .Returns(Task.CompletedTask);

        // Act
        await _scheduler.ScheduleAsync(
            typeof(TestSaga),
            "order-123",
            TimeSpan.FromMinutes(30),
            new TestTimeout { Reason = "Payment" },
            CancellationToken.None);

        // Assert
        capturedRequest.Should().NotBeNull();
        capturedRequest!.TimeoutTypeKey.Should().Be("test-timeout");
        capturedRequest.TimeoutTypeVersion.Should().Be("v1");
        capturedRequest.SagaTypeKey.Should().Be(typeof(TestSaga).FullName);
        capturedRequest.CorrelationKey.Should().Be("order-123");
    }

    [Fact]
    public async Task ScheduleAsync_WithUnregisteredType_AutoRegisters()
    {
        // Arrange
        _registryMock
            .Setup(r => r.TryGetTypeKey(typeof(TestTimeout), out It.Ref<string?>.IsAny, out It.Ref<string?>.IsAny))
            .Returns(false);
        
        _registryMock
            .Setup(r => r.GetTypeKey<TestTimeout>())
            .Returns((typeof(TestTimeout).FullName!, (string?)null));
        
        _serializerMock.Setup(s => s.Serialize(It.IsAny<TestTimeout>())).Returns(new byte[] { 1, 2, 3 });
        _storeMock.Setup(s => s.ScheduleAsync(It.IsAny<SagaTimeoutRequest>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        // Act
        await _scheduler.ScheduleAsync(
            typeof(TestSaga),
            "order-123",
            TimeSpan.FromMinutes(30),
            new TestTimeout(),
            CancellationToken.None);

        // Assert
        _registryMock.Verify(r => r.Register<TestTimeout>(null, null), Times.Once);
    }

    [Fact]
    public async Task ScheduleAsync_SetsDueTimeCorrectly()
    {
        // Arrange
        var delay = TimeSpan.FromMinutes(30);
        var beforeSchedule = DateTime.UtcNow;
        
        _registryMock
            .Setup(r => r.TryGetTypeKey(typeof(TestTimeout), out It.Ref<string?>.IsAny, out It.Ref<string?>.IsAny))
            .Returns(false);
        _registryMock
            .Setup(r => r.GetTypeKey<TestTimeout>())
            .Returns(("TestTimeout", (string?)null));
        
        _serializerMock.Setup(s => s.Serialize(It.IsAny<TestTimeout>())).Returns(Array.Empty<byte>());
        
        SagaTimeoutRequest? capturedRequest = null;
        _storeMock
            .Setup(s => s.ScheduleAsync(It.IsAny<SagaTimeoutRequest>(), It.IsAny<CancellationToken>()))
            .Callback<SagaTimeoutRequest, CancellationToken>((r, _) => capturedRequest = r)
            .Returns(Task.CompletedTask);

        // Act
        await _scheduler.ScheduleAsync(typeof(TestSaga), "corr", delay, new TestTimeout(), CancellationToken.None);

        // Assert
        capturedRequest.Should().NotBeNull();
        capturedRequest!.DueAtUtc.Should().BeCloseTo(beforeSchedule + delay, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task CancelAsync_UsesRegistryTypeKey()
    {
        // Arrange
        _registryMock
            .Setup(r => r.TryGetTypeKey(typeof(TestTimeout), out It.Ref<string?>.IsAny, out It.Ref<string?>.IsAny))
            .Callback(new TryGetTypeKeyCallback((Type t, out string? k, out string? v) =>
            {
                k = "test-timeout";
                v = "v1";
            }))
            .Returns(true);

        // Act
        await _scheduler.CancelAsync<TestTimeout>(typeof(TestSaga), "order-123", CancellationToken.None);

        // Assert
        _storeMock.Verify(s => s.CancelAsync(
            typeof(TestSaga).FullName!,
            "order-123",
            "test-timeout",
            "v1",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CancelAsync_WithUnregisteredType_UsesTypeName()
    {
        // Arrange
        _registryMock
            .Setup(r => r.TryGetTypeKey(typeof(TestTimeout), out It.Ref<string?>.IsAny, out It.Ref<string?>.IsAny))
            .Returns(false);

        // Act
        await _scheduler.CancelAsync<TestTimeout>(typeof(TestSaga), "order-123", CancellationToken.None);

        // Assert
        _storeMock.Verify(s => s.CancelAsync(
            typeof(TestSaga).FullName!,
            "order-123",
            typeof(TestTimeout).FullName!,
            null,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // Delegate for out parameter callback
    private delegate void TryGetTypeKeyCallback(Type type, out string? typeKey, out string? version);

    // Test classes
    public class TestSaga { }
    public class TestTimeout { public string? Reason { get; set; } }
}
