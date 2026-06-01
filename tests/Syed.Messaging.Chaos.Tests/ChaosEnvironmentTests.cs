using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Syed.Messaging.Chaos;
using Xunit;

namespace Syed.Messaging.Chaos.Tests;

/// <summary>
/// ChaosEnvironment reads environment variables in its constructor, so every
/// test must isolate the env state it cares about. Tests run sequentially
/// where isolation is required (xUnit's default within a class).
/// </summary>
public class ChaosEnvironmentTests : IDisposable
{
    // Snapshot env vars we might mutate so we can restore them.
    private readonly string? _savedLevel;
    private readonly string? _savedProd;
    private readonly string? _savedAspNet;

    public ChaosEnvironmentTests()
    {
        _savedLevel = Environment.GetEnvironmentVariable(ChaosEnvironment.LevelEnvVar);
        _savedProd = Environment.GetEnvironmentVariable(ChaosEnvironment.ProdEnvVar);
        _savedAspNet = Environment.GetEnvironmentVariable(ChaosEnvironment.AspNetEnvVar);
    }

    public void Dispose()
    {
        Restore(ChaosEnvironment.LevelEnvVar, _savedLevel);
        Restore(ChaosEnvironment.ProdEnvVar, _savedProd);
        Restore(ChaosEnvironment.AspNetEnvVar, _savedAspNet);
    }

    private static void Restore(string key, string? value)
    {
        if (value is null) Environment.SetEnvironmentVariable(key, null);
        else Environment.SetEnvironmentVariable(key, value);
    }

    private static ChaosEnvironment Make(ChaosOptions? options = null)
        => new(options ?? new ChaosOptions(), NullLogger<ChaosEnvironment>.Instance);

    // ─── Default (no env var) reflects options.Level ─────────────────────────

    [Fact]
    public void EffectiveLevel_MatchesOptionsLevel_WhenNoEnvVar()
    {
        Environment.SetEnvironmentVariable(ChaosEnvironment.LevelEnvVar, null);
        var env = Make(new ChaosOptions { Level = ChaosLevel.Medium });
        env.EffectiveLevel.Should().Be(ChaosLevel.Medium);
    }

    [Fact]
    public void IsActive_FalseWhenOff()
    {
        Environment.SetEnvironmentVariable(ChaosEnvironment.LevelEnvVar, null);
        Make(new ChaosOptions { Level = ChaosLevel.Off }).IsActive.Should().BeFalse();
    }

    // ─── Env var overrides options.Level ─────────────────────────────────────

    [Theory]
    [InlineData("low", ChaosLevel.Low)]
    [InlineData("Medium", ChaosLevel.Medium)]
    [InlineData("HIGH", ChaosLevel.High)]
    [InlineData("off", ChaosLevel.Off)]
    public void EnvVar_OverridesOptionsLevel(string envValue, ChaosLevel expected)
    {
        Environment.SetEnvironmentVariable(ChaosEnvironment.LevelEnvVar, envValue);
        var env = Make(new ChaosOptions { Level = ChaosLevel.Off }); // code says Off; env overrides
        env.EffectiveLevel.Should().Be(expected);
    }

    [Fact]
    public void InvalidEnvVar_FallsBackToOptionsLevel()
    {
        Environment.SetEnvironmentVariable(ChaosEnvironment.LevelEnvVar, "banana");
        var env = Make(new ChaosOptions { Level = ChaosLevel.Low });
        env.EffectiveLevel.Should().Be(ChaosLevel.Low, "invalid env var should not change the configured level");
    }

    // ─── Production-safety gate ──────────────────────────────────────────────

    [Fact]
    public void ProductionWithoutProdFlag_ChaosIsRefused()
    {
        Environment.SetEnvironmentVariable(ChaosEnvironment.AspNetEnvVar, "Production");
        Environment.SetEnvironmentVariable(ChaosEnvironment.ProdEnvVar, null);
        var env = Make(new ChaosOptions { Level = ChaosLevel.Medium });
        env.EffectiveLevel.Should().Be(ChaosLevel.Off, "production must refuse chaos unless explicitly allowed");
        env.IsActive.Should().BeFalse();
    }

    [Fact]
    public void ProductionWithProdEnvVar_ChaosIsAllowed()
    {
        Environment.SetEnvironmentVariable(ChaosEnvironment.AspNetEnvVar, "Production");
        Environment.SetEnvironmentVariable(ChaosEnvironment.ProdEnvVar, "true");
        var env = Make(new ChaosOptions { Level = ChaosLevel.Low });
        env.EffectiveLevel.Should().Be(ChaosLevel.Low);
    }

    [Fact]
    public void ProductionWithProductionAllowedOption_ChaosIsAllowed()
    {
        Environment.SetEnvironmentVariable(ChaosEnvironment.AspNetEnvVar, "Production");
        Environment.SetEnvironmentVariable(ChaosEnvironment.ProdEnvVar, null);
        var env = Make(new ChaosOptions { Level = ChaosLevel.Low, ProductionAllowed = true });
        env.EffectiveLevel.Should().Be(ChaosLevel.Low);
    }

    [Fact]
    public void NonProductionEnvironment_ChaosNotGated()
    {
        Environment.SetEnvironmentVariable(ChaosEnvironment.AspNetEnvVar, "Staging");
        Environment.SetEnvironmentVariable(ChaosEnvironment.ProdEnvVar, null);
        var env = Make(new ChaosOptions { Level = ChaosLevel.High });
        env.EffectiveLevel.Should().Be(ChaosLevel.High);
    }

    [Fact]
    public void NoAspNetEnvVar_ChaosNotGated()
    {
        Environment.SetEnvironmentVariable(ChaosEnvironment.AspNetEnvVar, null);
        var env = Make(new ChaosOptions { Level = ChaosLevel.Medium });
        env.EffectiveLevel.Should().Be(ChaosLevel.Medium);
    }
}
