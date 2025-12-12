using Microsoft.EntityFrameworkCore;
using Syed.Messaging.Sagas;
using Syed.Messaging.Sagas.EfCore;

namespace Syed.Messaging.Sagas.EfCore.Tests;

public class EfSagaTimeoutStoreTests
{
    [Fact]
    public async Task ScheduleAsync_AddsTimeoutEntity()
    {
        // Arrange
        using var context = CreateContext();
        var store = new EfSagaTimeoutStore<TestDbContext>(context);
        var request = CreateRequest(DateTime.UtcNow.AddMinutes(5));

        // Act
        await store.ScheduleAsync(request, CancellationToken.None);

        // Assert
        var entity = await context.Set<SagaTimeoutEntity>().FirstOrDefaultAsync();
        entity.Should().NotBeNull();
        entity!.SagaTypeKey.Should().Be(request.SagaTypeKey);
        entity.TimeoutTypeKey.Should().Be(request.TimeoutTypeKey);
    }

    [Fact]
    public async Task GetDueAsync_ReturnsOnlyDueTimeouts()
    {
        // Arrange
        using var context = CreateContext();
        var store = new EfSagaTimeoutStore<TestDbContext>(context);
        var due = CreateRequest(DateTime.UtcNow.AddMinutes(-5));
        var future = CreateRequest(DateTime.UtcNow.AddMinutes(10));

        await store.ScheduleAsync(due, CancellationToken.None);
        await store.ScheduleAsync(future, CancellationToken.None);

        // Act
        var result = await store.GetDueAsync(DateTime.UtcNow, 100, CancellationToken.None);

        // Assert
        result.Should().ContainSingle(r => r.Id == due.Id);
    }

    [Fact]
    public async Task CancelAsync_MarksTimeoutCancelled()
    {
        // Arrange
        using var context = CreateContext();
        var store = new EfSagaTimeoutStore<TestDbContext>(context);
        var request = CreateRequest(DateTime.UtcNow.AddMinutes(-5));
        await store.ScheduleAsync(request, CancellationToken.None);

        // Act
        await store.CancelAsync(
            request.SagaTypeKey,
            request.CorrelationKey,
            request.TimeoutTypeKey,
            request.TimeoutTypeVersion,
            CancellationToken.None);

        // Assert
        var due = await store.GetDueAsync(DateTime.UtcNow, 100, CancellationToken.None);
        due.Should().BeEmpty();
    }

    [Fact]
    public async Task MarkProcessedAsync_SetsProcessedFlag()
    {
        // Arrange
        using var context = CreateContext();
        var store = new EfSagaTimeoutStore<TestDbContext>(context);
        var request = CreateRequest(DateTime.UtcNow.AddMinutes(-5));
        await store.ScheduleAsync(request, CancellationToken.None);

        // Act
        await store.MarkProcessedAsync(request.Id, CancellationToken.None);

        // Assert
        var entity = await context.Set<SagaTimeoutEntity>().FirstAsync();
        entity.Processed.Should().BeTrue();
        entity.ProcessedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task GetDueAsync_ExcludesProcessedTimeouts()
    {
        // Arrange
        using var context = CreateContext();
        var store = new EfSagaTimeoutStore<TestDbContext>(context);
        var request = CreateRequest(DateTime.UtcNow.AddMinutes(-5));
        await store.ScheduleAsync(request, CancellationToken.None);
        await store.MarkProcessedAsync(request.Id, CancellationToken.None);

        // Act
        var due = await store.GetDueAsync(DateTime.UtcNow, 100, CancellationToken.None);

        // Assert
        due.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDueAsync_RespectsBatchSize()
    {
        // Arrange
        using var context = CreateContext();
        var store = new EfSagaTimeoutStore<TestDbContext>(context);
        for (int i = 0; i < 10; i++)
        {
            await store.ScheduleAsync(CreateRequest(DateTime.UtcNow.AddMinutes(-5)), CancellationToken.None);
        }

        // Act
        var due = await store.GetDueAsync(DateTime.UtcNow, 5, CancellationToken.None);

        // Assert
        due.Should().HaveCount(5);
    }

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TestDbContext(options);
    }

    private static SagaTimeoutRequest CreateRequest(DateTime dueAt)
    {
        return new SagaTimeoutRequest
        {
            Id = Guid.NewGuid(),
            SagaTypeKey = "TestSaga",
            TimeoutTypeKey = "TestTimeout",
            TimeoutTypeVersion = "v1",
            CorrelationKey = $"corr-{Guid.NewGuid():N}",
            Payload = new byte[] { 1, 2, 3 },
            DueAtUtc = dueAt,
            CreatedAtUtc = DateTime.UtcNow,
            Cancelled = false
        };
    }

    public class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ConfigureSagaEntities();
        }
    }
}
