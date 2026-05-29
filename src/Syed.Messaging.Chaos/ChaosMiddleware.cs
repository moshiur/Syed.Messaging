using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Syed.Messaging.Chaos;

/// <summary>
/// The middleware that applies chaos shapes chosen by <see cref="IChaosInjector"/>.
/// Sits in the existing <see cref="IMessageMiddleware"/> pipeline; fires only
/// when <see cref="ChaosEnvironment.IsActive"/>. Every injection logs a
/// <c>[CHAOS:shape]</c> warning explaining what happened and why, and
/// increments the dedicated <see cref="ChaosMetrics.Injected"/> counter.
/// </summary>
internal sealed class ChaosMiddleware : IMessageMiddleware
{
    private readonly ChaosEnvironment _env;
    private readonly ChaosOptions _options;
    private readonly IChaosInjector _injector;
    private readonly ILogger<ChaosMiddleware> _logger;

    public ChaosMiddleware(
        ChaosEnvironment env,
        ChaosOptions options,
        IChaosInjector injector,
        ILogger<ChaosMiddleware> logger)
    {
        _env = env;
        _options = options;
        _injector = injector;
        _logger = logger;
    }

    public async Task InvokeAsync(
        IMessageEnvelope envelope, IServiceProvider serviceProvider, Func<Task> next)
    {
        if (!_env.IsActive)
        {
            await next();
            return;
        }

        // Build an options view that reflects the gated effective level so the
        // injector's probability roll matches what the environment allows.
        var effective = _options.Level == _env.EffectiveLevel
            ? _options
            : CloneWithLevel(_options, _env.EffectiveLevel);

        ChaosOutcome outcome;
        try
        {
            outcome = _injector.Decide(envelope, effective);
        }
        catch (Exception ex)
        {
            // AD-2: a buggy custom injector must never break message processing.
            _logger.LogError(ex,
                "[CHAOS] Injector threw while deciding chaos for {MessageType}; proceeding with no chaos.",
                envelope.MessageType);
            await next();
            return;
        }

        if (!outcome.IsChaos)
        {
            await next();
            return;
        }

        await ApplyAsync(outcome, envelope, serviceProvider, next);
    }

    private async Task ApplyAsync(
        ChaosOutcome outcome, IMessageEnvelope envelope,
        IServiceProvider serviceProvider, Func<Task> next)
    {
        var messageType = envelope.MessageType;

        switch (outcome.Applied)
        {
            case ChaosShape.Drop:
                Tag("drop", messageType);
                _logger.LogWarning(
                    "[CHAOS:drop] Dropped {MessageType} before the handler ran. " +
                    "Your system should tolerate at-least-once gaps and recover via upstream retry. " +
                    "Disable with {EnvVar}=off.",
                    messageType, ChaosEnvironment.LevelEnvVar);
                return; // handler never runs

            case ChaosShape.Delay:
                var ms = ParseDelayMs(outcome.Note, _options.MaxDelayInjected);
                Tag("delay", messageType);
                _logger.LogWarning(
                    "[CHAOS:delay] Delaying {MessageType} by {DelayMs}ms before the handler. " +
                    "Tests whether slow consumers cause timeouts or backpressure.",
                    messageType, ms);
                await Task.Delay(ms);
                await next();
                return;

            case ChaosShape.Duplicate:
                // E3: double-invocation before the inbox mark is unsafe for
                // non-idempotent handlers. When an inbox store is present, skip.
                if (serviceProvider.GetService<IInboxStore>() is not null)
                {
                    _logger.LogDebug(
                        "[CHAOS:duplicate] Skipped for {MessageType} — an IInboxStore is registered, which would dedupe the replay.",
                        messageType);
                    await next();
                    return;
                }
                Tag("duplicate", messageType);
                _logger.LogWarning(
                    "[CHAOS:duplicate] Invoking the handler for {MessageType} TWICE to test idempotency. " +
                    "If your handler isn't safe to call twice, that's a real bug production will eventually hit.",
                    messageType);
                await next();
                await next();
                return;

            case ChaosShape.HeaderCorruption:
                // E1: additive only. We never mutate existing headers (that
                // would corrupt the live envelope the transport/retry/DLQ read).
                // We add one junk header and log NAMES only, never values (AD-3).
                const string junkHeader = "x-syed-chaos";
                envelope.Headers[junkHeader] = "1";
                Tag("header_corruption", messageType);
                _logger.LogWarning(
                    "[CHAOS:header] Added junk header '{HeaderName}' to {MessageType}. " +
                    "Tests whether your handler assumes a fixed header set. " +
                    "(Header values are never logged.)",
                    junkHeader, messageType);
                await next();
                return;

            case ChaosShape.AckTimeout:
                Tag("ack_timeout", messageType);
                _logger.LogWarning(
                    "[CHAOS:ack-timeout] Handler for {MessageType} will run successfully, then chaos throws " +
                    "to simulate a lost broker ack. Tests whether your retry path safely replays an " +
                    "already-processed message.",
                    messageType);
                await next();
                throw new ChaosAckTimeoutException(messageType);

            default:
                await next();
                return;
        }
    }

    private static void Tag(string shape, string messageType) =>
        ChaosMetrics.Injected.Add(1,
            new KeyValuePair<string, object?>("chaos.shape", shape),
            new KeyValuePair<string, object?>("message_type", messageType));

    private static int ParseDelayMs(string? note, TimeSpan max)
    {
        var fallback = (int)Math.Clamp(max.TotalMilliseconds / 2, 1, int.MaxValue);
        if (string.IsNullOrEmpty(note) ||
            !int.TryParse(note, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var ms))
        {
            return fallback;
        }
        return Math.Clamp(ms, 1, (int)Math.Clamp(max.TotalMilliseconds, 1, int.MaxValue));
    }

    [SuppressMessage("Performance", "CA1812", Justification = "Cloned per-message only when env-gated level differs from configured level.")]
    private static ChaosOptions CloneWithLevel(ChaosOptions src, ChaosLevel level) => new()
    {
        Level = level,
        EnabledShapes = src.EnabledShapes,
        Seed = src.Seed,
        MaxDelayInjected = src.MaxDelayInjected,
        ProductionAllowed = src.ProductionAllowed
    };
}
