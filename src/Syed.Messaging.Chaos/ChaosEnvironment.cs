using Microsoft.Extensions.Logging;

namespace Syed.Messaging.Chaos;

/// <summary>
/// Resolves the effective chaos level from configuration + environment
/// variables, applies the production-safety gate, and logs the resolution
/// exactly once per process. Registered as a singleton so the startup
/// decision (especially a production refusal) is logged a single time rather
/// than per message.
/// </summary>
/// <remarks>
/// Per-process semantics are deliberate: each process reads its own
/// <c>SYED_CHAOS_LEVEL</c> at startup. If two pods in the same consumer group
/// run different levels, each logs its own resolved level here so the
/// inconsistency is visible in logs and (optionally) dashboards.
/// </remarks>
internal sealed class ChaosEnvironment
{
    public const string LevelEnvVar = "SYED_CHAOS_LEVEL";
    public const string ProdEnvVar = "SYED_CHAOS_PROD";
    public const string AspNetEnvVar = "ASPNETCORE_ENVIRONMENT";

    /// <summary>The level chaos will actually run at, after gating. May be Off.</summary>
    public ChaosLevel EffectiveLevel { get; }

    /// <summary>True when chaos will fire (EffectiveLevel != Off).</summary>
    public bool IsActive => EffectiveLevel != ChaosLevel.Off;

    public ChaosEnvironment(ChaosOptions options, ILogger<ChaosEnvironment> logger)
    {
        // 1. Resolve the requested level: env var overrides code config when set.
        var requested = options.Level;
        var rawLevel = Environment.GetEnvironmentVariable(LevelEnvVar);
        if (!string.IsNullOrWhiteSpace(rawLevel))
        {
            if (Enum.TryParse<ChaosLevel>(rawLevel, ignoreCase: true, out var parsed))
            {
                requested = parsed;
            }
            else
            {
                logger.LogWarning(
                    "[CHAOS] Invalid {EnvVar}={RawValue}; expected Off/Low/Medium/High. Falling back to configured level {ConfiguredLevel}.",
                    LevelEnvVar, rawLevel, options.Level);
            }
        }

        // 2. Apply the production-safety gate.
        var aspNetEnv = Environment.GetEnvironmentVariable(AspNetEnvVar);
        var isProduction = string.Equals(aspNetEnv, "Production", StringComparison.OrdinalIgnoreCase);

        if (isProduction && requested != ChaosLevel.Off)
        {
            var prodAllowed = options.ProductionAllowed ||
                string.Equals(
                    Environment.GetEnvironmentVariable(ProdEnvVar),
                    "true", StringComparison.OrdinalIgnoreCase);

            if (!prodAllowed)
            {
                // AD-1: refuse, and log ONCE at error level so operators see
                // that chaos was explicitly NOT engaged in production.
                logger.LogError(
                    "[CHAOS:refused] {EnvVar}={RequestedLevel} but {AspNet}=Production and neither {ProdVar}=true nor ProductionAllowed is set. Chaos is DISABLED. Set {ProdVarAgain}=true to run a deliberate game day.",
                    LevelEnvVar, requested, AspNetEnvVar, ProdEnvVar, ProdEnvVar);
                EffectiveLevel = ChaosLevel.Off;
                return;
            }

            logger.LogWarning(
                "[CHAOS:engaged] Chaos is ACTIVE in Production at level {Level} (explicit opt-in). This will inject failures into real traffic.",
                requested);
            EffectiveLevel = requested;
            return;
        }

        EffectiveLevel = requested;
        if (IsActive)
        {
            logger.LogInformation(
                "[CHAOS] Active at level {Level} ({Environment}). Disable with {EnvVar}=off.",
                EffectiveLevel, aspNetEnv ?? "Development (default)", LevelEnvVar);
        }
    }
}
