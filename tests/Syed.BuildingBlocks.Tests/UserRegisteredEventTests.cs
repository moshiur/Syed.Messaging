using FluentAssertions;
using Syed.BuildingBlocks.IntegrationEvents;
using Xunit;

namespace Syed.BuildingBlocks.Tests;

public class UserRegisteredEventTests
{
    [Fact]
    public void DefaultConstructor_ShouldInitializeStringProperties()
    {
        var evt = new UserRegisteredEvent();

        evt.Email.Should().BeEmpty();
        evt.FirstName.Should().BeEmpty();
        evt.LastName.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_ShouldPopulateProperties()
    {
        var userId = Guid.NewGuid();

        var evt = new UserRegisteredEvent(userId, "user@example.com", "Ada", "Lovelace");

        evt.UserId.Should().Be(userId);
        evt.Email.Should().Be("user@example.com");
        evt.FirstName.Should().Be("Ada");
        evt.LastName.Should().Be("Lovelace");
    }
}
