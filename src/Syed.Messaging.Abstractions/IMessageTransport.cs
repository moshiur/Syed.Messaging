namespace Syed.Messaging;

public interface IMessageTransport
{
    Task PublishAsync(IMessageEnvelope envelope, string destination, CancellationToken ct);
    Task SendAsync(IMessageEnvelope envelope, string destination, CancellationToken ct);

    Task SubscribeAsync(
        string subscriptionName,
        string destination,
        Func<IMessageEnvelope, CancellationToken, Task<TransportAcknowledge>> handler,
        CancellationToken ct);
}

public enum TransportAcknowledge
{
    Ack,
    Retry,
    DeadLetter
}
