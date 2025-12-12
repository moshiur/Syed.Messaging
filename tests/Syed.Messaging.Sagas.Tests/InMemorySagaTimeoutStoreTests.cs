using Syed.Messaging.Sagas;

namespace Syed.Messaging.Sagas.Tests;

public class InMemorySagaTimeoutStoreTests
{
    private readonly InMemorySagaTimeoutStore _store;

    public InMemorySagaTimeoutStoreTests()
    {
        _store = new InMemorySagaTimeoutStore();
    }

    [Fact]
    public async Task ScheduleAsync_AddsRequest()
    {
        // Arrange
        var request = CreateRequest(DateTime.UtcNow.AddMinutes(5));

        // Act
        await _store.ScheduleAsync(request, CancellationToken.None);
        var due = await _store.GetDueAsync(DateTime.UtcNow.AddMinutes(10), 100, CancellationToken.None);

        // Assert
        due.Should().ContainSingle(r => r.Id == request.Id);
    }

    [Fact]
    public async Task GetDueAsync_ReturnsOnlyDueRequests()
    {
        // Arrange
        var dueRequest = CreateRequest(DateTime.UtcNow.AddMinutes(-5));
        var futureRequest = CreateRequest(DateTime.UtcNow.AddMinutes(10));
        
        await _store.ScheduleAsync(dueRequest, CancellationToken.None);
        await _store.ScheduleAsync(futureRequest, CancellationToken.None);

        // Act
        var due = await _store.GetDueAsync(DateTime.UtcNow, 100, CancellationToken.None);

        // Assert
        due.Should().ContainSingle(r => r.Id == dueRequest.Id);
        due.Should().NotContain(r => r.Id == futureRequest.Id);
    }

    [Fact]
    public async Task GetDueAsync_ExcludesCancelledRequests()
    {
        // Arrange
        var request = CreateRequest(DateTime.UtcNow.AddMinutes(-5));
        request.Cancelled = true;
        
        await _store.ScheduleAsync(request, CancellationToken.None);

        // Act
        var due = await _store.GetDueAsync(DateTime.UtcNow, 100, CancellationToken.None);

        // Assert
        due.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDueAsync_RespectsBatchSize()
    {
        // Arrange
        for (int i = 0; i < 10; i++)
        {
            await _store.ScheduleAsync(CreateRequest(DateTime.UtcNow.AddMinutes(-5)), CancellationToken.None);
        }

        // Act
        var due = await _store.GetDueAsync(DateTime.UtcNow, 5, CancellationToken.None);

        // Assert
        due.Should().HaveCount(5);
    }

    [Fact]
    public async Task GetDueAsync_OrdersByDueTime()
    {
        // Arrange
        var later = CreateRequest(DateTime.UtcNow.AddMinutes(-1));
        var earlier = CreateRequest(DateTime.UtcNow.AddMinutes(-10));
        
        await _store.ScheduleAsync(later, CancellationToken.None);
        await _store.ScheduleAsync(earlier, CancellationToken.None);

        // Act
        var due = await _store.GetDueAsync(DateTime.UtcNow, 100, CancellationToken.None);

        // Assert
        due.First().Id.Should().Be(earlier.Id);
        due.Last().Id.Should().Be(later.Id);
    }

    [Fact]
    public async Task CancelAsync_MarksMatchingRequestAsCancelled()
    {
        // Arrange
        var request = CreateRequest(DateTime.UtcNow.AddMinutes(5));
        await _store.ScheduleAsync(request, CancellationToken.None);

        // Act
        await _store.CancelAsync(
            request.SagaTypeKey,
            request.CorrelationKey,
            request.TimeoutTypeKey,
            request.TimeoutTypeVersion,
            CancellationToken.None);

        var due = await _store.GetDueAsync(DateTime.UtcNow.AddMinutes(10), 100, CancellationToken.None);

        // Assert
        due.Should().BeEmpty();
    }

    [Fact]
    public async Task CancelAsync_DoesNotAffectOtherRequests()
    {
        // Arrange
        var request1 = CreateRequest(DateTime.UtcNow.AddMinutes(5), "saga1", "corr1");
        var request2 = CreateRequest(DateTime.UtcNow.AddMinutes(5), "saga2", "corr2");
        
        await _store.ScheduleAsync(request1, CancellationToken.None);
        await _store.ScheduleAsync(request2, CancellationToken.None);

        // Act - cancel only request1
        await _store.CancelAsync(
            request1.SagaTypeKey,
            request1.CorrelationKey,
            request1.TimeoutTypeKey,
            request1.TimeoutTypeVersion,
            CancellationToken.None);

        var due = await _store.GetDueAsync(DateTime.UtcNow.AddMinutes(10), 100, CancellationToken.None);

        // Assert
        due.Should().ContainSingle(r => r.Id == request2.Id);
    }

    [Fact]
    public async Task MarkProcessedAsync_RemovesRequest()
    {
        // Arrange
        var request = CreateRequest(DateTime.UtcNow.AddMinutes(-5));
        await _store.ScheduleAsync(request, CancellationToken.None);

        // Act
        await _store.MarkProcessedAsync(request.Id, CancellationToken.None);
        var due = await _store.GetDueAsync(DateTime.UtcNow, 100, CancellationToken.None);

        // Assert
        due.Should().BeEmpty();
    }

    [Fact]
    public async Task MarkProcessedAsync_NonExistentId_DoesNotThrow()
    {
        // Act
        var act = async () => await _store.MarkProcessedAsync(Guid.NewGuid(), CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }

    private static SagaTimeoutRequest CreateRequest(
        DateTime dueAt,
        string sagaTypeKey = "TestSaga",
        string correlationKey = "corr-123")
    {
        return new SagaTimeoutRequest
        {
            Id = Guid.NewGuid(),
            SagaTypeKey = sagaTypeKey,
            TimeoutTypeKey = "TestTimeout",
            TimeoutTypeVersion = "v1",
            CorrelationKey = correlationKey,
            Payload = new byte[] { 1, 2, 3 },
            DueAtUtc = dueAt,
            CreatedAtUtc = DateTime.UtcNow,
            Cancelled = false
        };
    }
}
