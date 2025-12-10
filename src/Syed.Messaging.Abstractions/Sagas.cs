namespace Syed.Messaging;

public interface ISagaState
{
    Guid Id { get; set; }
    int Version { get; set; }
}

public interface ISagaHandler<TSagaState, TMessage>
    where TSagaState : class, ISagaState, new()
{
    Task HandleAsync(TSagaState state, TMessage message, MessageContext context, CancellationToken ct);
}
