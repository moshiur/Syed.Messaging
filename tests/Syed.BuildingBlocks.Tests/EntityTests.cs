using FluentAssertions;
using Syed.BuildingBlocks;
using Xunit;

namespace Syed.BuildingBlocks.Tests;

public class EntityTests
{
    [Fact]
    public void DomainEvents_ShouldStartEmpty()
    {
        var entity = new TestEntity();

        entity.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void DomainEvents_ShouldSupportAddRemoveAndClear()
    {
        var entity = new TestEntity();
        var first = new TestDomainEvent();
        var second = new TestDomainEvent();

        entity.AddDomainEvent(first);
        entity.AddDomainEvent(second);
        entity.RemoveDomainEvent(first);

        entity.DomainEvents.Should().ContainSingle().Which.Should().BeSameAs(second);

        entity.ClearDomainEvents();

        entity.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void EntityEquality_ShouldHandleNulls()
    {
        TestEntity? left = null;
        TestEntity? right = null;

        (left == right).Should().BeTrue();
        (left != right).Should().BeFalse();
    }

    private sealed class TestEntity : Entity
    {
    }

    private sealed class TestDomainEvent : IDomainEvent
    {
    }
}
