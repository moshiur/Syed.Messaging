using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Syed.Messaging.Chaos;
using Xunit;

namespace Syed.Messaging.Chaos.Tests;

/// <summary>
/// Tests for ChaosMiddleware — exercised via a helper that builds a minimal
/// DI container and wires up the middleware the same way the real pipeline does.
/// </summary>
public class ChaosMiddlewareTests
{
    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static (ChaosMiddleware middleware, IServiceProvider sp) Build(
        ChaosLevel level,
        ChaosShape enabledShapes = ChaosShape.All,
        bool inboxRegistered = false,
        IChaosInjector? injector = null,
        bool productionAllowed = false)
    {
        // Clear production env vars so the prod-safety gate doesn't fire in tests.
        Environment.SetEnvironmentVariable(ChaosEnvironment.AspNetEnvVar, null);
        Environment.SetEnvironmentVariable(ChaosEnvironment.LevelEnvVar, null);

        var options = new ChaosOptions
        {
            Level = level,
            EnabledShapes = enabledShapes,
            ProductionAllowed = productionAllowed
        };

        var env = new ChaosEnvironment(options, NullLogger<ChaosEnvironment>.Instance);

        var services = new ServiceCollection();
        if (inboxRegistered) services.AddSingleton<IInboxStore>(Mock.Of<IInboxStore>());
        var sp = services.BuildServiceProvider();

        var chosenInjector = injector ?? new RealisticChaosInjector(seed: 0);
        // ChaosMiddleware is internal; InternalsVisibleTo gives test access.
        var mw = new ChaosMiddleware(env, options, chosenInjector,
            NullLogger<ChaosMiddleware>.Instance);

        return (mw, sp);
    }

    private static IChaosInjector AlwaysReturn(ChaosShape shape, string? note = null)
    {
        var mock = new Mock<IChaosInjector>();
        mock.Setup(i => i.Decide(It.IsAny<IMessageEnvelope>(), It.IsAny<ChaosOptions>()))
            .Returns(new ChaosOutcome(shape, note));
        return mock.Object;
    }

    private static IChaosInjector NeverChaos()
    {
        var mock = new Mock<IChaosInjector>();
        mock.Setup(i => i.Decide(It.IsAny<IMessageEnvelope>(), It.IsAny<ChaosOptions>()))
            .Returns(ChaosOutcome.None);
        return mock.Object;
    }

    private static IChaosInjector AlwaysThrow()
    {
        var mock = new Mock<IChaosInjector>();
        mock.Setup(i => i.Decide(It.IsAny<IMessageEnvelope>(), It.IsAny<ChaosOptions>()))
            .Throws(new InvalidOperationException("injector exploded"));
        return mock.Object;
    }

    // ─── Off level = pure pass-through ───────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_WhenLevelOff_CallsNextAndDoesNothing()
    {
        var (mw, sp) = Build(ChaosLevel.Off);
        var envelope = new TestEnvelope();
        var called = false;

        await mw.InvokeAsync(envelope, sp, () => { called = true; return Task.CompletedTask; });

        called.Should().BeTrue();
        envelope.Headers.Should().BeEmpty(); // no mutations
    }

    // ─── Drop shape ──────────────────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_Drop_HandlerNeverRuns()
    {
        var (mw, sp) = Build(ChaosLevel.Medium, injector: AlwaysReturn(ChaosShape.Drop));
        var called = false;

        await mw.InvokeAsync(new TestEnvelope(), sp,
            () => { called = true; return Task.CompletedTask; });

        called.Should().BeFalse();
    }

    // ─── Delay shape ─────────────────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_Delay_HandlerRunsAfterDelay()
    {
        // note = "50" → 50ms delay
        var (mw, sp) = Build(ChaosLevel.Medium, injector: AlwaysReturn(ChaosShape.Delay, note: "50"));
        var called = false;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        await mw.InvokeAsync(new TestEnvelope(), sp,
            () => { called = true; return Task.CompletedTask; });

        sw.Stop();
        called.Should().BeTrue();
        sw.ElapsedMilliseconds.Should().BeGreaterThanOrEqualTo(40); // 50ms ± scheduling slack
    }

    // ─── Duplicate shape ─────────────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_Duplicate_HandlerCalledTwice_WhenNoInboxStore()
    {
        var (mw, sp) = Build(ChaosLevel.Medium, inboxRegistered: false,
            injector: AlwaysReturn(ChaosShape.Duplicate));
        var count = 0;

        await mw.InvokeAsync(new TestEnvelope(), sp,
            () => { count++; return Task.CompletedTask; });

        count.Should().Be(2);
    }

    [Fact]
    public async Task InvokeAsync_Duplicate_SkippedWhenInboxStoreRegistered()
    {
        var (mw, sp) = Build(ChaosLevel.Medium, inboxRegistered: true,
            injector: AlwaysReturn(ChaosShape.Duplicate));
        var count = 0;

        await mw.InvokeAsync(new TestEnvelope(), sp,
            () => { count++; return Task.CompletedTask; });

        count.Should().Be(1, "inbox-registered consumers must not see double-invocation");
    }

    // ─── HeaderCorruption shape ──────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_HeaderCorruption_AddsJunkHeaderButLeavesExistingIntact()
    {
        var (mw, sp) = Build(ChaosLevel.Medium, injector: AlwaysReturn(ChaosShape.HeaderCorruption));
        var envelope = new TestEnvelope();
        envelope.Headers["message-type"] = "orders.created";
        var called = false;

        await mw.InvokeAsync(envelope, sp,
            () => { called = true; return Task.CompletedTask; });

        called.Should().BeTrue();
        envelope.Headers.Should().ContainKey("x-syed-chaos");
        envelope.Headers["message-type"].Should().Be("orders.created", "existing headers must not be mutated");
    }

    [Fact]
    public async Task InvokeAsync_HeaderCorruption_DoesNotMutateExistingHeaders()
    {
        var (mw, sp) = Build(ChaosLevel.Medium, injector: AlwaysReturn(ChaosShape.HeaderCorruption));
        var envelope = new TestEnvelope();
        var originalHeaders = new Dictionary<string, string>(envelope.Headers);

        await mw.InvokeAsync(envelope, sp, () => Task.CompletedTask);

        // Only the junk header should have been added
        foreach (var kv in originalHeaders)
        {
            envelope.Headers.Should().Contain(kv.Key, kv.Value);
        }
    }

    // ─── AckTimeout shape ────────────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_AckTimeout_HandlerRunsButThrowsAfterward()
    {
        var (mw, sp) = Build(ChaosLevel.Medium, injector: AlwaysReturn(ChaosShape.AckTimeout));
        var called = false;

        var act = async () => await mw.InvokeAsync(new TestEnvelope(), sp,
            () => { called = true; return Task.CompletedTask; });

        await act.Should().ThrowAsync<ChaosAckTimeoutException>();
        called.Should().BeTrue("handler must run even when AckTimeout fires");
    }

    // ─── Injector throws → resilience ────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_WhenInjectorThrows_HandlerStillRuns()
    {
        var (mw, sp) = Build(ChaosLevel.Medium, injector: AlwaysThrow());
        var called = false;

        await mw.InvokeAsync(new TestEnvelope(), sp,
            () => { called = true; return Task.CompletedTask; });

        called.Should().BeTrue("a buggy injector must never break message processing");
    }

    // ─── No chaos when injector returns None ─────────────────────────────────

    [Fact]
    public async Task InvokeAsync_WhenInjectorReturnsNone_EnvelopeUnchanged()
    {
        var (mw, sp) = Build(ChaosLevel.Medium, injector: NeverChaos());
        var envelope = new TestEnvelope();
        var originalHeaderCount = envelope.Headers.Count;

        await mw.InvokeAsync(envelope, sp, () => Task.CompletedTask);

        envelope.Headers.Count.Should().Be(originalHeaderCount, "no-chaos should not mutate the envelope");
    }
}
