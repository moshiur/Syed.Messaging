namespace Syed.Messaging.Chaos;

/// <summary>
/// Decides whether — and which — chaos shape to apply to a given message.
/// The default implementation rolls a weighted die per message based on the
/// configured <see cref="ChaosLevel"/>. Register a custom implementation via
/// <c>EnableChaos(o =&gt; o.UseInjector&lt;MyInjector&gt;())</c> to define your own
/// probabilities or shapes.
/// </summary>
/// <remarks>
/// The injector only <i>decides</i>; the <c>ChaosMiddleware</c> <i>applies</i>
/// the chosen shape. This keeps the decision logic pure and testable
/// (deterministic given a seed) while the side effects live in the middleware.
/// Implementations must be safe to call concurrently from multiple consumer
/// threads.
/// </remarks>
public interface IChaosInjector
{
    /// <summary>
    /// Decide what chaos (if any) to apply to <paramref name="envelope"/>.
    /// Must not throw; must not mutate the envelope. Returns
    /// <see cref="ChaosOutcome.None"/> when no chaos is selected.
    /// </summary>
    ChaosOutcome Decide(IMessageEnvelope envelope, ChaosOptions options);
}
