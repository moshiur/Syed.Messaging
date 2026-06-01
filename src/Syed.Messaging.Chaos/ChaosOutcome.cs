namespace Syed.Messaging.Chaos;

/// <summary>
/// The result of an <see cref="IChaosInjector"/> deciding what (if anything)
/// to do with a message. <see cref="Applied"/> is <see cref="ChaosShape.None"/>
/// when no chaos was injected.
/// </summary>
/// <param name="Applied">The shape that was chosen, or <see cref="ChaosShape.None"/>.</param>
/// <param name="Note">
/// Optional human-readable detail for the chaos log line (e.g. the injected
/// delay duration). Never include message body content or header values here —
/// the middleware logs this verbatim and the message may carry PII.
/// </param>
public readonly record struct ChaosOutcome(ChaosShape Applied, string? Note)
{
    /// <summary>A no-op outcome — no chaos injected.</summary>
    public static readonly ChaosOutcome None = new(ChaosShape.None, null);

    /// <summary>True when a chaos shape was selected.</summary>
    public bool IsChaos => Applied != ChaosShape.None;
}
