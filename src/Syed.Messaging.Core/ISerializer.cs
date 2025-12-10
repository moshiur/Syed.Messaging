namespace Syed.Messaging;

public interface ISerializer
{
    byte[] Serialize<T>(T value);
    T Deserialize<T>(byte[] data);
    object Deserialize(byte[] data, Type type);
}

public sealed class SystemTextJsonSerializer : ISerializer
{
    public byte[] Serialize<T>(T value) =>
        System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value);

    public T Deserialize<T>(byte[] data) =>
        System.Text.Json.JsonSerializer.Deserialize<T>(data)!;

    public object Deserialize(byte[] data, Type type) =>
        System.Text.Json.JsonSerializer.Deserialize(data, type)!;
}
