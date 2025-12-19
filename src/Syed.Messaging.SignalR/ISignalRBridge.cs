namespace Syed.Messaging.SignalR;

/// <summary>
/// Defines how a messaging event should be bridged to SignalR.
/// </summary>
/// <typeparam name="TEvent">The event type consumed from the message bus.</typeparam>
public interface ISignalRBridge<TEvent>
{
    /// <summary>
    /// Gets the SignalR group name to send the notification to.
    /// </summary>
    string GetGroupName(TEvent evt);

    /// <summary>
    /// Gets the payload to send to SignalR clients.
    /// </summary>
    object GetPayload(TEvent evt);

    /// <summary>
    /// Gets the SignalR client method name to invoke.
    /// </summary>
    string MethodName { get; }
}
