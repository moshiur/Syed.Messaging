namespace Syed.Messaging;

public interface IMessageBus
{
    Task PublishAsync<T>(string destination, T message, CancellationToken ct = default);
    Task SendAsync<T>(string destination, T message, CancellationToken ct = default);
}
