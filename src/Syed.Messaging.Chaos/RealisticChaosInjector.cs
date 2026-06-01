namespace Syed.Messaging.Chaos;

/// <summary>
/// Default <see cref="IChaosInjector"/>. Rolls a weighted die per message:
/// with probability <c>ChaosShapeWeights.TotalProbability(level)</c> it selects
/// a shape uniformly among the enabled shapes; otherwise returns
/// <see cref="ChaosOutcome.None"/>.
/// </summary>
/// <remarks>
/// <para>
/// Thread-safety: <see cref="Random"/> is not thread-safe, and this injector is
/// registered as a singleton shared across all consumer threads. Each thread
/// gets its own <see cref="Random"/> via <see cref="ThreadLocal{T}"/>. When a
/// seed is supplied, each thread's RNG is derived deterministically from
/// <c>seed + threadId</c>, so a single-threaded test with a fixed seed is fully
/// reproducible while concurrent production use stays race-free.
/// </para>
/// </remarks>
internal sealed class RealisticChaosInjector : IChaosInjector
{
    private readonly int? _seed;
    private readonly ThreadLocal<Random> _rng;

    public RealisticChaosInjector(int? seed)
    {
        _seed = seed;
        _rng = new ThreadLocal<Random>(() =>
            _seed is { } s
                ? new Random(unchecked(s + Environment.CurrentManagedThreadId))
                : new Random());
    }

    public ChaosOutcome Decide(IMessageEnvelope envelope, ChaosOptions options)
    {
        if (options.Level == ChaosLevel.Off)
        {
            return ChaosOutcome.None;
        }

        var enabled = EnabledShapeList(options.EnabledShapes);
        if (enabled.Count == 0)
        {
            return ChaosOutcome.None;
        }

        var rng = _rng.Value!;

        // Roll for "does any chaos fire this message?"
        if (rng.NextDouble() >= ChaosShapeWeights.TotalProbability(options.Level))
        {
            return ChaosOutcome.None;
        }

        // Pick one enabled shape uniformly.
        var shape = enabled[rng.Next(enabled.Count)];

        return shape switch
        {
            ChaosShape.Delay => new ChaosOutcome(shape, DescribeDelay(rng, options)),
            _ => new ChaosOutcome(shape, null)
        };
    }

    /// <summary>
    /// The delay duration is decided here (so it's deterministic under a seed)
    /// and encoded in the note as whole milliseconds. The middleware parses it
    /// back out to perform the actual <see cref="Task.Delay(int)"/>.
    /// </summary>
    private static string DescribeDelay(Random rng, ChaosOptions options)
    {
        var maxMs = (int)Math.Clamp(
            options.MaxDelayInjected.TotalMilliseconds, 1, int.MaxValue);
        var ms = rng.Next(1, maxMs + 1);
        return ms.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static List<ChaosShape> EnabledShapeList(ChaosShape mask)
    {
        var list = new List<ChaosShape>(5);
        if (mask.HasFlag(ChaosShape.Drop)) list.Add(ChaosShape.Drop);
        if (mask.HasFlag(ChaosShape.Duplicate)) list.Add(ChaosShape.Duplicate);
        if (mask.HasFlag(ChaosShape.Delay)) list.Add(ChaosShape.Delay);
        if (mask.HasFlag(ChaosShape.HeaderCorruption)) list.Add(ChaosShape.HeaderCorruption);
        if (mask.HasFlag(ChaosShape.AckTimeout)) list.Add(ChaosShape.AckTimeout);
        return list;
    }
}
