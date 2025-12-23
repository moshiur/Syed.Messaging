using System.Diagnostics.Metrics;

namespace Syed.Messaging;

/// <summary>
/// Provides messaging metrics using System.Diagnostics.Metrics.
/// </summary>
public static class MessagingMetrics
{
    public const string MeterName = "Syed.Messaging";

    private static readonly Meter Meter = new(MeterName, "1.0.0");

    /// <summary>
    /// Counter for messages published.
    /// </summary>
    public static readonly Counter<long> MessagesPublished = Meter.CreateCounter<long>(
        "messaging.messages.published",
        unit: "{message}",
        description: "Number of messages published");

    /// <summary>
    /// Counter for messages received.
    /// </summary>
    public static readonly Counter<long> MessagesReceived = Meter.CreateCounter<long>(
        "messaging.messages.received",
        unit: "{message}",
        description: "Number of messages received");

    /// <summary>
    /// Counter for messages successfully processed.
    /// </summary>
    public static readonly Counter<long> MessagesProcessed = Meter.CreateCounter<long>(
        "messaging.messages.processed",
        unit: "{message}",
        description: "Number of messages successfully processed");

    /// <summary>
    /// Counter for messages that failed processing.
    /// </summary>
    public static readonly Counter<long> MessagesFailed = Meter.CreateCounter<long>(
        "messaging.messages.failed",
        unit: "{message}",
        description: "Number of messages that failed processing");

    /// <summary>
    /// Counter for messages retried.
    /// </summary>
    public static readonly Counter<long> MessagesRetried = Meter.CreateCounter<long>(
        "messaging.messages.retried",
        unit: "{message}",
        description: "Number of messages sent for retry");

    /// <summary>
    /// Counter for messages sent to Dead Letter Queue.
    /// </summary>
    public static readonly Counter<long> MessagesDeadLettered = Meter.CreateCounter<long>(
        "messaging.messages.deadlettered",
        unit: "{message}",
        description: "Number of messages sent to Dead Letter Queue");

    /// <summary>
    /// Histogram for message processing duration.
    /// </summary>
    public static readonly Histogram<double> ProcessingDuration = Meter.CreateHistogram<double>(
        "messaging.messages.processing_duration",
        unit: "ms",
        description: "Message processing duration in milliseconds");
}
