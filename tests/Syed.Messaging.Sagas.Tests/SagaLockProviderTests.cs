using Syed.Messaging.Sagas;

namespace Syed.Messaging.Sagas.Tests;

public class InMemorySagaLockProviderTests
{
    [Fact]
    public async Task TryAcquireAsync_UnlockedSaga_ReturnsHandle()
    {
        // Arrange
        var provider = new InMemorySagaLockProvider();

        // Act
        var handle = await provider.TryAcquireAsync("TestSaga", "order-123", TimeSpan.FromSeconds(5));

        // Assert
        handle.Should().NotBeNull();
        handle!.SagaType.Should().Be("TestSaga");
        handle.CorrelationKey.Should().Be("order-123");
        handle.IsHeld.Should().BeTrue();

        await handle.DisposeAsync();
    }

    [Fact]
    public async Task TryAcquireAsync_AlreadyLocked_WaitsAndReturnsNull()
    {
        // Arrange
        var provider = new InMemorySagaLockProvider();
        var firstLock = await provider.TryAcquireAsync("TestSaga", "order-456", TimeSpan.FromSeconds(5));
        firstLock.Should().NotBeNull();

        // Act - try to acquire same lock with very short timeout
        var secondLock = await provider.TryAcquireAsync("TestSaga", "order-456", TimeSpan.FromMilliseconds(50));

        // Assert
        secondLock.Should().BeNull();

        await firstLock!.DisposeAsync();
    }

    [Fact]
    public async Task TryAcquireAsync_AfterRelease_CanAcquireAgain()
    {
        // Arrange
        var provider = new InMemorySagaLockProvider();
        var firstLock = await provider.TryAcquireAsync("TestSaga", "order-789", TimeSpan.FromSeconds(5));
        firstLock.Should().NotBeNull();

        // Act - release first lock
        await firstLock!.DisposeAsync();
        firstLock.IsHeld.Should().BeFalse();

        // Try to acquire again
        var secondLock = await provider.TryAcquireAsync("TestSaga", "order-789", TimeSpan.FromSeconds(5));

        // Assert
        secondLock.Should().NotBeNull();
        secondLock!.IsHeld.Should().BeTrue();

        await secondLock.DisposeAsync();
    }

    [Fact]
    public async Task TryAcquireAsync_DifferentCorrelationKeys_CanAcquireBoth()
    {
        // Arrange
        var provider = new InMemorySagaLockProvider();

        // Act
        var lock1 = await provider.TryAcquireAsync("TestSaga", "order-1", TimeSpan.FromSeconds(5));
        var lock2 = await provider.TryAcquireAsync("TestSaga", "order-2", TimeSpan.FromSeconds(5));

        // Assert
        lock1.Should().NotBeNull();
        lock2.Should().NotBeNull();
        lock1!.IsHeld.Should().BeTrue();
        lock2!.IsHeld.Should().BeTrue();

        await lock1.DisposeAsync();
        await lock2.DisposeAsync();
    }

    [Fact]
    public async Task TryAcquireAsync_DifferentSagaTypes_CanAcquireBoth()
    {
        // Arrange
        var provider = new InMemorySagaLockProvider();

        // Act
        var lock1 = await provider.TryAcquireAsync("SagaA", "key-1", TimeSpan.FromSeconds(5));
        var lock2 = await provider.TryAcquireAsync("SagaB", "key-1", TimeSpan.FromSeconds(5));

        // Assert
        lock1.Should().NotBeNull();
        lock2.Should().NotBeNull();

        await lock1!.DisposeAsync();
        await lock2!.DisposeAsync();
    }

    [Fact]
    public async Task Handle_DoubleDispose_DoesNotThrow()
    {
        // Arrange
        var provider = new InMemorySagaLockProvider();
        var handle = await provider.TryAcquireAsync("TestSaga", "order-dbl", TimeSpan.FromSeconds(5));
        handle.Should().NotBeNull();

        // Act - dispose twice
        await handle!.DisposeAsync();
        var act = async () => await handle.DisposeAsync();

        // Assert
        await act.Should().NotThrowAsync();
    }
}

public class NoOpSagaLockProviderTests
{
    [Fact]
    public async Task TryAcquireAsync_AlwaysSucceeds()
    {
        // Arrange
        var provider = new NoOpSagaLockProvider();

        // Act
        var handle = await provider.TryAcquireAsync("AnySaga", "any-key", TimeSpan.Zero);

        // Assert
        handle.Should().NotBeNull();
        handle!.IsHeld.Should().BeTrue();

        await handle.DisposeAsync();
    }

    [Fact]
    public async Task TryAcquireAsync_ConcurrentCalls_AllSucceed()
    {
        // Arrange
        var provider = new NoOpSagaLockProvider();

        // Act
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => provider.TryAcquireAsync("TestSaga", "same-key", TimeSpan.Zero))
            .ToArray();
        
        var handles = await Task.WhenAll(tasks);

        // Assert
        handles.Should().AllSatisfy(h => h.Should().NotBeNull());
    }
}
