namespace Syed.Messaging;

public interface IMessageBus
{
    Task PublishAsync<T>(string destination, T message, CancellationToken ct = default);
    Task SendAsync<T>(string destination, T message, CancellationToken ct = default);
    Task<TResponse> RequestAsync<TRequest, TResponse>(string destination, TRequest message, CancellationToken ct = default);
    
    /// <summary>
    /// Publishes raw bytes to the destination without type serialization.
    /// Useful for pre-serialized payloads or anonymous objects.
    /// </summary>
    Task PublishRawAsync(string destination, byte[] payload, string? messageType = null, Dictionary<string, string>? headers = null, CancellationToken ct = default);
}
