namespace Syed.Messaging.Chaos;

/// <summary>
/// Maps a <see cref="ChaosLevel"/> to the total probability that any chaos
/// shape fires for a given message. The selected shape is then chosen
/// uniformly among the enabled shapes.
/// </summary>
internal static class ChaosShapeWeights
{
    /// <summary>Total injection probability per level (0.0 - 1.0).</summary>
    public static double TotalProbability(ChaosLevel level) => level switch
    {
        ChaosLevel.Off    => 0.00,
        ChaosLevel.Low    => 0.01,   // ~1%
        ChaosLevel.Medium => 0.05,   // ~5% — the recommended dev default
        ChaosLevel.High   => 0.15,   // ~15% — CI / stress intensity
        _                 => 0.00
    };
}
