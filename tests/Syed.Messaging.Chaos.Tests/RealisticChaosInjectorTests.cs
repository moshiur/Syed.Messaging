using FluentAssertions;
using Syed.Messaging.Chaos;
using Xunit;

namespace Syed.Messaging.Chaos.Tests;

public class RealisticChaosInjectorTests
{
    private static readonly TestEnvelope Envelope = new();

    // ─── Off level → never fire ──────────────────────────────────────────────

    [Fact]
    public void Decide_WhenLevelOff_ReturnsNone()
    {
        var injector = new RealisticChaosInjector(seed: 42);
        var options = new ChaosOptions { Level = ChaosLevel.Off };

        for (int i = 0; i < 1000; i++)
        {
            injector.Decide(Envelope, options).IsChaos.Should().BeFalse();
        }
    }

    // ─── Probability is in the right ballpark ────────────────────────────────

    [Theory]
    [InlineData(ChaosLevel.Low, 0.001, 0.035)]    // expect ~1% ±2σ over 10k
    [InlineData(ChaosLevel.Medium, 0.025, 0.075)] // expect ~5% ±2σ
    [InlineData(ChaosLevel.High, 0.10, 0.22)]     // expect ~15% ±2σ
    public void Decide_ProbabilityIsInExpectedRange(ChaosLevel level, double low, double high)
    {
        var injector = new RealisticChaosInjector(seed: null); // non-deterministic intentionally
        var options = new ChaosOptions { Level = level };
        const int N = 10_000;
        var hits = Enumerable.Range(0, N).Count(_ => injector.Decide(Envelope, options).IsChaos);
        var rate = (double)hits / N;
        rate.Should().BeInRange(low, high,
            $"ChaosLevel.{level} should fire roughly {ChaosShapeWeights.TotalProbability(level):P0} of the time");
    }

    // ─── Deterministic seed produces the same sequence ───────────────────────

    [Fact]
    public void Decide_WithSameSeed_ProducesSameSequence()
    {
        var options = new ChaosOptions { Level = ChaosLevel.High, Seed = 99 };
        var a = new RealisticChaosInjector(seed: 99);
        var b = new RealisticChaosInjector(seed: 99);

        var seqA = Enumerable.Range(0, 200).Select(_ => a.Decide(Envelope, options)).ToList();
        var seqB = Enumerable.Range(0, 200).Select(_ => b.Decide(Envelope, options)).ToList();

        seqA.Should().BeEquivalentTo(seqB, opts => opts.WithStrictOrdering());
    }

    // ─── EnabledShapes mask is respected ─────────────────────────────────────

    [Fact]
    public void Decide_OnlyDropAndDelay_NeverYieldsOtherShapes()
    {
        var injector = new RealisticChaosInjector(seed: 7);
        var options = new ChaosOptions
        {
            Level = ChaosLevel.High,
            EnabledShapes = ChaosShape.Drop | ChaosShape.Delay
        };

        var outcomes = Enumerable.Range(0, 2000)
            .Select(_ => injector.Decide(Envelope, options))
            .Where(o => o.IsChaos)
            .Select(o => o.Applied)
            .Distinct()
            .ToList();

        outcomes.Should().OnlyContain(s => s == ChaosShape.Drop || s == ChaosShape.Delay);
    }

    [Fact]
    public void Decide_WhenAllShapesMaskedOut_ReturnsNone()
    {
        var injector = new RealisticChaosInjector(seed: 1);
        var options = new ChaosOptions { Level = ChaosLevel.High, EnabledShapes = ChaosShape.None };

        for (int i = 0; i < 500; i++)
        {
            injector.Decide(Envelope, options).IsChaos.Should().BeFalse();
        }
    }

    // ─── All 5 shapes are reachable at High level ────────────────────────────

    [Fact]
    public void Decide_AllFiveShapesOccurOverEnoughTrials()
    {
        var injector = new RealisticChaosInjector(seed: 13);
        var options = new ChaosOptions { Level = ChaosLevel.High };
        var seen = new HashSet<ChaosShape>();

        for (int i = 0; i < 10_000 && seen.Count < 5; i++)
        {
            var o = injector.Decide(Envelope, options);
            if (o.IsChaos) seen.Add(o.Applied);
        }

        seen.Should().Contain(ChaosShape.Drop);
        seen.Should().Contain(ChaosShape.Delay);
        seen.Should().Contain(ChaosShape.Duplicate);
        seen.Should().Contain(ChaosShape.HeaderCorruption);
        seen.Should().Contain(ChaosShape.AckTimeout);
    }

    // ─── Delay shape encodes duration in the Note ────────────────────────────

    [Fact]
    public void Decide_DelayShape_NoteContainsPositiveIntegerMs()
    {
        var injector = new RealisticChaosInjector(seed: 77);
        var options = new ChaosOptions
        {
            Level = ChaosLevel.High,
            EnabledShapes = ChaosShape.Delay,
            MaxDelayInjected = TimeSpan.FromSeconds(10)
        };

        ChaosOutcome? delay = null;
        for (int i = 0; i < 2000 && delay == null; i++)
        {
            var o = injector.Decide(Envelope, options);
            if (o.Applied == ChaosShape.Delay) delay = o;
        }

        delay.Should().NotBeNull();
        int.TryParse(delay!.Value.Note, out var ms).Should().BeTrue();
        ms.Should().BeInRange(1, 10_000);
    }

    // ─── Concurrent calls don't corrupt RNG state ────────────────────────────

    [Fact]
    public void Decide_ConcurrentCalls_DoNotThrow()
    {
        var injector = new RealisticChaosInjector(seed: 42);
        var options = new ChaosOptions { Level = ChaosLevel.High };

        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();
        var tasks = Enumerable.Range(0, 1000)
            .Select(_ => Task.Run(() =>
            {
                try { injector.Decide(Envelope, options); }
                catch (Exception ex) { exceptions.Add(ex); }
            }));

        Task.WhenAll(tasks).Wait(TimeSpan.FromSeconds(5));
        exceptions.Should().BeEmpty("concurrent calls must not corrupt Random state");
    }
}
