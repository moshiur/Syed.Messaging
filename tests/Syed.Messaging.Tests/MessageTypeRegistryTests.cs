using Syed.Messaging;

namespace Syed.Messaging.Tests;

public class MessageTypeRegistryTests
{
    private readonly MessageTypeRegistry _registry;

    public MessageTypeRegistryTests()
    {
        _registry = new MessageTypeRegistry();
    }

    [Fact]
    public void Register_WithExplicitTypeKey_CanResolve()
    {
        // Arrange & Act
        _registry.Register<TestMessage>("test-message", "v1");

        // Assert
        var resolved = _registry.Resolve("test-message", "v1");
        resolved.Should().Be(typeof(TestMessage));
    }

    [Fact]
    public void Register_WithoutTypeKey_UsesFullTypeName()
    {
        // Arrange & Act
        _registry.Register<TestMessage>();

        // Assert
        var (typeKey, version) = _registry.GetTypeKey<TestMessage>();
        typeKey.Should().Be(typeof(TestMessage).FullName);
        version.Should().BeNull();
    }

    [Fact]
    public void Register_WithAttribute_UsesAttributeValues()
    {
        // Arrange & Act
        _registry.Register<AttributedMessage>();

        // Assert
        var (typeKey, version) = _registry.GetTypeKey<AttributedMessage>();
        typeKey.Should().Be("attributed-message");
        version.Should().Be("v2");
    }

    [Fact]
    public void Resolve_UnregisteredType_ReturnsNull()
    {
        // Act
        var resolved = _registry.Resolve("non-existent");

        // Assert
        resolved.Should().BeNull();
    }

    [Fact]
    public void Resolve_WithVersionFallback_FindsUnversionedType()
    {
        // Arrange
        _registry.Register<TestMessage>("test-message");

        // Act - request with version, but type was registered without version
        var resolved = _registry.Resolve("test-message", "v1");

        // Assert - should fall back to unversioned
        resolved.Should().Be(typeof(TestMessage));
    }

    [Fact]
    public void Register_DuplicateTypeKey_DifferentType_Throws()
    {
        // Arrange
        _registry.Register<TestMessage>("shared-key");

        // Act & Assert
        var act = () => _registry.Register<AnotherMessage>("shared-key");
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already registered*");
    }

    [Fact]
    public void Register_SameTypeTwice_DoesNotThrow()
    {
        // Arrange & Act
        _registry.Register<TestMessage>("test-key");
        var act = () => _registry.Register<TestMessage>("test-key");

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void GetTypeKey_UnregisteredType_Throws()
    {
        // Act & Assert
        var act = () => _registry.GetTypeKey<TestMessage>();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not registered*");
    }

    [Fact]
    public void TryResolve_RegisteredType_ReturnsTrue()
    {
        // Arrange
        _registry.Register<TestMessage>("test-message");

        // Act
        var result = _registry.TryResolve("test-message", null, out var type);

        // Assert
        result.Should().BeTrue();
        type.Should().Be(typeof(TestMessage));
    }

    [Fact]
    public void TryResolve_UnregisteredType_ReturnsFalse()
    {
        // Act
        var result = _registry.TryResolve("non-existent", null, out var type);

        // Assert
        result.Should().BeFalse();
        type.Should().BeNull();
    }

    [Fact]
    public void TryGetTypeKey_RegisteredType_ReturnsTrue()
    {
        // Arrange
        _registry.Register<TestMessage>("test-message", "v1");

        // Act
        var result = _registry.TryGetTypeKey(typeof(TestMessage), out var typeKey, out var version);

        // Assert
        result.Should().BeTrue();
        typeKey.Should().Be("test-message");
        version.Should().Be("v1");
    }

    [Fact]
    public void TryGetTypeKey_UnregisteredType_ReturnsFalse()
    {
        // Act
        var result = _registry.TryGetTypeKey(typeof(TestMessage), out var typeKey, out var version);

        // Assert
        result.Should().BeFalse();
        typeKey.Should().BeNull();
        version.Should().BeNull();
    }

    [Fact]
    public void RegisterFromAssembly_RegistersAttributedTypes()
    {
        // Act
        _registry.RegisterFromAssembly(typeof(AttributedMessage).Assembly);

        // Assert
        var resolved = _registry.Resolve("attributed-message", "v2");
        resolved.Should().Be(typeof(AttributedMessage));
    }

    // Test helper classes
    public class TestMessage { }
    public class AnotherMessage { }

    [MessageType("attributed-message", "v2")]
    public class AttributedMessage { }
}
