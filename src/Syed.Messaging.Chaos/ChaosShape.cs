namespace Syed.Messaging.Chaos;

/// <summary>
/// The failure shapes chaos can inject into a consumed message. Combine with
/// <c>|</c> to enable a subset, e.g. <c>ChaosShape.Drop | ChaosShape.Delay</c>.
/// </summary>
/// <remarks>
/// <para>
/// v1.3.0 ships the five shapes that are cleanly implementable in the
/// <see cref="IMessageMiddleware"/> contract without mutating the shared
/// envelope or breaking the consumer's DI scope. Three further shapes
/// (partial-body truncation, out-of-order delivery, Kafka partition
/// rebalance) were deferred — they require either an envelope-replacement
/// change to the middleware contract or transport-specific hooks.
/// </para>
/// <para>
/// <b>Handler-safety note:</b> <see cref="Duplicate"/> re-invokes your handler
/// for the same message. It is only safe for idempotent handlers and is
/// automatically skipped when an <c>IInboxStore</c> is registered. See the
/// shape-safety matrix in the package README.
/// </para>
/// </remarks>
[Flags]
public enum ChaosShape
{
    /// <summary>No shape.</summary>
    None = 0,

    /// <summary>
    /// Silently drop the message — the handler never runs. Tests whether your
    /// system tolerates at-least-once gaps and whether upstream retries recover.
    /// </summary>
    Drop = 1 << 0,

    /// <summary>
    /// Re-invoke the handler a second time for the same message. Tests handler
    /// idempotency. Skipped automatically when an <c>IInboxStore</c> is
    /// registered (the inbox would dedupe it anyway, and double-invocation
    /// before the inbox mark is unsafe for non-idempotent handlers).
    /// </summary>
    Duplicate = 1 << 1,

    /// <summary>
    /// Delay delivery by a random interval (bounded by
    /// <see cref="ChaosOptions.MaxDelayInjected"/>) before invoking the handler.
    /// Tests whether slow consumers cause timeouts or backpressure issues.
    /// </summary>
    Delay = 1 << 2,

    /// <summary>
    /// Add a junk header to the envelope before the handler runs (additive
    /// only — existing headers are never mutated, so message identity and
    /// poison classification stay intact). Tests whether handlers assume a
    /// fixed header set.
    /// </summary>
    HeaderCorruption = 1 << 3,

    /// <summary>
    /// Invoke the handler successfully, then throw on the way out — simulating
    /// a broker ack that never lands. Tests whether your retry path safely
    /// replays an already-processed message.
    /// </summary>
    AckTimeout = 1 << 4,

    /// <summary>All v1.3.0 shapes.</summary>
    All = Drop | Duplicate | Delay | HeaderCorruption | AckTimeout
}
