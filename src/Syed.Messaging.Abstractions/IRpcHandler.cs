namespace Syed.Messaging;

/// <summary>
/// Handler interface for RPC-style request/response messaging.
/// Unlike IMessageHandler (fire-and-forget), this returns a response 
/// that will be sent back to the requester.
/// </summary>
public interface IRpcHandler<TRequest, TResponse>
{
    Task<TResponse> HandleAsync(TRequest request, MessageContext context, CancellationToken ct);
}
