namespace Syed.Messaging.Chaos;

/// <summary>
/// Controls how aggressively chaos is injected into consumed messages.
/// The default is <see cref="Off"/> — chaos never fires unless you opt in,
/// either via <c>EnableChaos(o =&gt; o.Level = ChaosLevel.Medium)</c> or the
/// <c>SYED_CHAOS_LEVEL</c> environment variable.
/// </summary>
public enum ChaosLevel
{
    /// <summary>No chaos. The middleware is a pass-through. This is the default.</summary>
    Off = 0,

    /// <summary>~1% of messages get a chaos shape. Light background pressure.</summary>
    Low = 1,

    /// <summary>~5% of messages get a chaos shape. The recommended dev/staging default.</summary>
    Medium = 2,

    /// <summary>~15% of messages get a chaos shape. CI / stress-test intensity.</summary>
    High = 3
}
