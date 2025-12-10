namespace Syed.Messaging;

public interface IMessageHandler<TMessage>
{
    Task HandleAsync(TMessage message, MessageContext context, CancellationToken ct);
}
