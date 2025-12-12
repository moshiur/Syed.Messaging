using Microsoft.EntityFrameworkCore;
using Syed.Messaging;
using Syed.Messaging.Outbox;

namespace Syed.Messaging.Outbox.Tests;

public class EfCoreOutboxStoreTests
{
    [Fact]
    public async Task SaveAsync_AddsMessageToDatabase()
    {
        // Arrange
        using var context = CreateContext();
        var store = new EfCoreOutboxStore<TestDbContext>(context);
        var message = CreateOutboxMessage();

        // Act
        await store.SaveAsync(message, CancellationToken.None);

        // Assert
        var saved = await context.Set<OutboxMessage>().FindAsync(message.Id);
        saved.Should().NotBeNull();
        saved!.MessageType.Should().Be(message.MessageType);
        saved.Destination.Should().Be(message.Destination);
    }

    [Fact]
    public async Task GetPendingAsync_ReturnsUnprocessedMessages()
    {
        // Arrange
        using var context = CreateContext();
        var store = new EfCoreOutboxStore<TestDbContext>(context);

        var pending1 = CreateOutboxMessage();
        var pending2 = CreateOutboxMessage();
        var processed = CreateOutboxMessage();
        processed.ProcessedAtUtc = DateTime.UtcNow;

        await store.SaveAsync(pending1, CancellationToken.None);
        await store.SaveAsync(pending2, CancellationToken.None);
        await store.SaveAsync(processed, CancellationToken.None);

        // Act
        var result = await store.GetPendingAsync(10, CancellationToken.None);

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(m => m.Id == pending1.Id);
        result.Should().Contain(m => m.Id == pending2.Id);
        result.Should().NotContain(m => m.Id == processed.Id);
    }

    [Fact]
    public async Task GetPendingAsync_RespectsBatchSize()
    {
        // Arrange
        using var context = CreateContext();
        var store = new EfCoreOutboxStore<TestDbContext>(context);

        for (int i = 0; i < 10; i++)
        {
            await store.SaveAsync(CreateOutboxMessage(), CancellationToken.None);
        }

        // Act
        var result = await store.GetPendingAsync(5, CancellationToken.None);

        // Assert
        result.Should().HaveCount(5);
    }

    [Fact]
    public async Task GetPendingAsync_OrdersByCreatedTime()
    {
        // Arrange
        using var context = CreateContext();
        var store = new EfCoreOutboxStore<TestDbContext>(context);

        var older = CreateOutboxMessage(DateTime.UtcNow.AddMinutes(-10));
        var newer = CreateOutboxMessage(DateTime.UtcNow);

        // Save in reverse order
        await store.SaveAsync(newer, CancellationToken.None);
        await store.SaveAsync(older, CancellationToken.None);

        // Act
        var result = await store.GetPendingAsync(10, CancellationToken.None);

        // Assert
        result.First().Id.Should().Be(older.Id);
    }

    [Fact]
    public async Task MarkAsProcessedAsync_SetsProcessedTime()
    {
        // Arrange
        using var context = CreateContext();
        var store = new EfCoreOutboxStore<TestDbContext>(context);
        var message = CreateOutboxMessage();
        await store.SaveAsync(message, CancellationToken.None);

        // Act
        await store.MarkAsProcessedAsync(message.Id, CancellationToken.None);

        // Assert
        var updated = await context.Set<OutboxMessage>().FindAsync(message.Id);
        updated!.ProcessedAtUtc.Should().NotBeNull();
        updated.ProcessedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task MarkAsProcessedAsync_NonExistentId_DoesNotThrow()
    {
        // Arrange
        using var context = CreateContext();
        var store = new EfCoreOutboxStore<TestDbContext>(context);

        // Act
        var act = async () => await store.MarkAsProcessedAsync(Guid.NewGuid(), CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task GetPendingAsync_ReturnsEmptyWhenNoPending()
    {
        // Arrange
        using var context = CreateContext();
        var store = new EfCoreOutboxStore<TestDbContext>(context);

        // Act
        var result = await store.GetPendingAsync(10, CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new TestDbContext(options);
    }

    private static OutboxMessage CreateOutboxMessage(DateTime? createdAt = null)
    {
        return new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Destination = "test-queue",
            MessageType = "TestMessage",
            Version = "v1",
            Payload = new byte[] { 1, 2, 3 },
            CreatedAtUtc = createdAt ?? DateTime.UtcNow,
            ProcessedAtUtc = null
        };
    }

    public class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options)
        {
        }

        public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<OutboxMessage>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.MessageType).IsRequired();
                e.Property(x => x.Destination).IsRequired();
            });
        }
    }
}
