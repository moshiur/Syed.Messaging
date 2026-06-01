namespace Syed.Messaging.Chaos;

/// <summary>
/// Thrown by the <see cref="ChaosShape.AckTimeout"/> shape after the handler
/// has run successfully, to simulate a broker acknowledgement that never
/// landed. The consumer's normal retry/DLQ path handles it — which is exactly
/// the behavior the shape is testing.
/// </summary>
public sealed class ChaosAckTimeoutException : Exception
{
    public ChaosAckTimeoutException(string messageType)
        : base($"[CHAOS:ack-timeout] Simulated lost ack after successfully processing '{messageType}'. " +
               "This is injected chaos, not a real failure — your retry path is being exercised.")
    {
    }
}
