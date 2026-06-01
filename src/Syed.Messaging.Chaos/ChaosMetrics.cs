using System.Diagnostics.Metrics;

namespace Syed.Messaging.Chaos;

/// <summary>
/// A dedicated meter for chaos injection. Deliberately separate from the
/// core <c>Syed.Messaging</c> meter and its <c>messaging.messages.failed</c>
/// counter — tagging chaos onto an existing SRE-alerted counter would trigger
/// false-positive pages. Subscribe with <c>AddMeter("Syed.Messaging.Chaos")</c>.
/// </summary>
public static class ChaosMetrics
{
    /// <summary>The meter name to register with OpenTelemetry.</summary>
    public const string MeterName = "Syed.Messaging.Chaos";

    private static readonly Meter Meter = new(MeterName, "1.0.0");

    /// <summary>
    /// Counts chaos injections, tagged with <c>chaos.shape</c> (drop, delay,
    /// duplicate, header_corruption, ack_timeout) and <c>message_type</c>.
    /// </summary>
    public static readonly Counter<long> Injected = Meter.CreateCounter<long>(
        "messaging.chaos.injected",
        unit: "{injection}",
        description: "Number of chaos shapes injected into consumed messages");
}
