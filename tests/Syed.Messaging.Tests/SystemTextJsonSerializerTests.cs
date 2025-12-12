using Syed.Messaging;

namespace Syed.Messaging.Tests;

public class SystemTextJsonSerializerTests
{
    private readonly SystemTextJsonSerializer _serializer;

    public SystemTextJsonSerializerTests()
    {
        _serializer = new SystemTextJsonSerializer();
    }

    [Fact]
    public void Serialize_SimpleObject_ReturnsBytes()
    {
        // Arrange
        var obj = new TestDto { Name = "Test", Value = 42 };

        // Act
        var bytes = _serializer.Serialize(obj);

        // Assert
        bytes.Should().NotBeNull().And.NotBeEmpty();
    }

    [Fact]
    public void Deserialize_Generic_ReturnsObject()
    {
        // Arrange
        var obj = new TestDto { Name = "Test", Value = 42 };
        var bytes = _serializer.Serialize(obj);

        // Act
        var result = _serializer.Deserialize<TestDto>(bytes);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be("Test");
        result.Value.Should().Be(42);
    }

    [Fact]
    public void Deserialize_WithType_ReturnsObject()
    {
        // Arrange
        var obj = new TestDto { Name = "TypeBased", Value = 100 };
        var bytes = _serializer.Serialize(obj);

        // Act
        var result = _serializer.Deserialize(bytes, typeof(TestDto));

        // Assert
        result.Should().NotBeNull().And.BeOfType<TestDto>();
        var dto = (TestDto)result;
        dto.Name.Should().Be("TypeBased");
        dto.Value.Should().Be(100);
    }

    [Fact]
    public void Serialize_Null_ReturnsNullBytes()
    {
        // Act
        var bytes = _serializer.Serialize<TestDto?>(null);

        // Assert
        bytes.Should().NotBeNull();
        // "null" in JSON is 4 bytes
        System.Text.Encoding.UTF8.GetString(bytes).Should().Be("null");
    }

    [Fact]
    public void Serialize_ComplexObject_RoundTrips()
    {
        // Arrange
        var obj = new ComplexDto
        {
            Id = Guid.NewGuid(),
            Items = new List<string> { "a", "b", "c" },
            Nested = new TestDto { Name = "Nested", Value = 999 }
        };

        // Act
        var bytes = _serializer.Serialize(obj);
        var result = _serializer.Deserialize<ComplexDto>(bytes);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(obj.Id);
        result.Items.Should().BeEquivalentTo(obj.Items);
        result.Nested.Should().NotBeNull();
        result.Nested!.Name.Should().Be("Nested");
        result.Nested.Value.Should().Be(999);
    }

    [Fact]
    public void Deserialize_EmptyArray_Throws()
    {
        // Arrange
        var emptyBytes = Array.Empty<byte>();

        // Act & Assert
        var act = () => _serializer.Deserialize<TestDto>(emptyBytes);
        act.Should().Throw<System.Text.Json.JsonException>();
    }

    // Test DTOs
    public class TestDto
    {
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
    }

    public class ComplexDto
    {
        public Guid Id { get; set; }
        public List<string> Items { get; set; } = new();
        public TestDto? Nested { get; set; }
    }
}
