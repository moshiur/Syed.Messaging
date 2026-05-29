namespace Syed.Messaging.Chaos;

/// <summary>
/// Configuration for chaos injection. All defaults are safe: <see cref="Level"/>
/// is <see cref="ChaosLevel.Off"/>, so installing the package and calling
/// <c>EnableChaos()</c> with no arguments does nothing until you opt in via
/// code or the <c>SYED_CHAOS_LEVEL</c> environment variable.
/// </summary>
public sealed class ChaosOptions
{
    /// <summary>
    /// How aggressively chaos fires. Default <see cref="ChaosLevel.Off"/>.
    /// An explicit <c>SYED_CHAOS_LEVEL</c> environment variable overrides this
    /// when set (so ops can dial chaos without a redeploy).
    /// </summary>
    public ChaosLevel Level { get; set; } = ChaosLevel.Off;

    /// <summary>
    /// Which shapes are eligible to fire. Default <see cref="ChaosShape.All"/>.
    /// Mask this to restrict chaos, e.g. <c>ChaosShape.Delay | ChaosShape.Drop</c>
    /// for a strict-ordering consumer that can't tolerate <see cref="ChaosShape.Duplicate"/>.
    /// </summary>
    public ChaosShape EnabledShapes { get; set; } = ChaosShape.All;

    /// <summary>
    /// Seed for deterministic chaos. When set, the same seed produces the same
    /// shape sequence — useful for reproducing a chaos-found bug in a test.
    /// When null (default), chaos is non-deterministic per process.
    /// </summary>
    public int? Seed { get; set; }

    /// <summary>
    /// Upper bound on the delay injected by <see cref="ChaosShape.Delay"/>.
    /// Default 30 seconds.
    /// </summary>
    public TimeSpan MaxDelayInjected { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Explicit override of the production-safety gate. Chaos refuses to run
    /// when <c>ASPNETCORE_ENVIRONMENT=Production</c> unless either this is
    /// <c>true</c> or the <c>SYED_CHAOS_PROD</c> environment variable is
    /// <c>true</c>. Intended for deliberate game-day exercises only.
    /// </summary>
    public bool ProductionAllowed { get; set; } = false;

    /// <summary>
    /// Optional custom injector type. When set via
    /// <see cref="ChaosMessagingBuilderExtensions"/>'s configuration, replaces
    /// the default <c>RealisticChaosInjector</c>. Lets advanced users define
    /// their own shape probabilities or add bespoke shapes.
    /// </summary>
    public Type? CustomInjectorType { get; internal set; }
}
