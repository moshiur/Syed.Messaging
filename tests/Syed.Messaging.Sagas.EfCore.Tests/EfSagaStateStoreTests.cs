using Microsoft.EntityFrameworkCore;
using Syed.Messaging;
using Syed.Messaging.Sagas;
using Syed.Messaging.Sagas.EfCore;

namespace Syed.Messaging.Sagas.EfCore.Tests;

public class EfSagaStateStoreTests
{
    [Fact]
    public async Task SaveAsync_NewSaga_CreatesEntity()
    {
        // Arrange
        using var context = CreateContext();
        var store = CreateStore<TestSagaState>(context);
        var state = new TestSagaState { Status = "New" };

        // Act
        await store.SaveAsync(state, "order-123", CancellationToken.None);

        // Assert
        var entity = await context.Set<SagaStateEntity>().FirstOrDefaultAsync();
        entity.Should().NotBeNull();
        entity!.SagaType.Should().Be("TestSagaState");
        entity.CorrelationKey.Should().Be("order-123");
        entity.Version.Should().Be(1);
        state.Id.Should().NotBe(Guid.Empty);
        state.Version.Should().Be(1);
    }

    [Fact]
    public async Task LoadAsync_ExistingSaga_ReturnsState()
    {
        // Arrange
        using var context = CreateContext();
        var store = CreateStore<TestSagaState>(context);
        var state = new TestSagaState { Status = "Started" };
        await store.SaveAsync(state, "order-456", CancellationToken.None);

        // Act
        var loaded = await store.LoadAsync("order-456", CancellationToken.None);

        // Assert
        loaded.Should().NotBeNull();
        loaded!.Status.Should().Be("Started");
        loaded.Id.Should().Be(state.Id);
    }

    [Fact]
    public async Task LoadAsync_NonExistent_ReturnsNull()
    {
        // Arrange
        using var context = CreateContext();
        var store = CreateStore<TestSagaState>(context);

        // Act
        var loaded = await store.LoadAsync("non-existent", CancellationToken.None);

        // Assert
        loaded.Should().BeNull();
    }

    [Fact]
    public async Task SaveAsync_UpdateExisting_IncrementsVersion()
    {
        // Arrange
        using var context = CreateContext();
        var store = CreateStore<TestSagaState>(context);
        var state = new TestSagaState { Status = "Started" };
        await store.SaveAsync(state, "order-789", CancellationToken.None);
        state.Version.Should().Be(1);

        // Act
        state.Status = "Completed";
        await store.SaveAsync(state, "order-789", CancellationToken.None);

        // Assert
        state.Version.Should().Be(2);
        var entity = await context.Set<SagaStateEntity>().FirstAsync();
        entity.Version.Should().Be(2);
    }

    [Fact]
    public async Task SaveAsync_ConcurrencyConflict_Throws()
    {
        // Arrange
        using var context = CreateContext();
        var store = CreateStore<TestSagaState>(context);
        var state1 = new TestSagaState { Status = "Started" };
        await store.SaveAsync(state1, "order-conflict", CancellationToken.None);

        // Simulate another process updating
        var entity = await context.Set<SagaStateEntity>().FirstAsync();
        entity.Version = 999;
        await context.SaveChangesAsync();

        // Act & Assert
        state1.Status = "Updated";
        var act = async () => await store.SaveAsync(state1, "order-conflict", CancellationToken.None);
        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Fact]
    public async Task MarkCompletedAsync_ExistingSaga_MarksCompleted()
    {
        // Arrange
        using var context = CreateContext();
        var store = CreateStore<TestSagaState>(context);
        var state = new TestSagaState { Status = "Started" };
        await store.SaveAsync(state, "order-complete", CancellationToken.None);

        // Act
        await store.MarkCompletedAsync("order-complete", CancellationToken.None);

        // Assert
        var entity = await context.Set<SagaStateEntity>().FirstAsync();
        entity.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task LoadAsync_CompletedSaga_ReturnsNull()
    {
        // Arrange
        using var context = CreateContext();
        var store = CreateStore<TestSagaState>(context);
        var state = new TestSagaState { Status = "Done" };
        await store.SaveAsync(state, "order-done", CancellationToken.None);
        await store.MarkCompletedAsync("order-done", CancellationToken.None);

        // Act
        var loaded = await store.LoadAsync("order-done", CancellationToken.None);

        // Assert
        loaded.Should().BeNull();
    }

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TestDbContext(options);
    }

    private static EfSagaStateStore<TestDbContext, TSagaState> CreateStore<TSagaState>(TestDbContext context)
        where TSagaState : class, ISagaState, new()
    {
        return new EfSagaStateStore<TestDbContext, TSagaState>(context, new SystemTextJsonSerializer());
    }

    public class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ConfigureSagaEntities();
        }
    }

    public class TestSagaState : ISagaState
    {
        public Guid Id { get; set; }
        public int Version { get; set; }
        public string Status { get; set; } = string.Empty;
    }
}
