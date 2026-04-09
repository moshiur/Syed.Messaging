using System.Diagnostics.Metrics;
using System.Text.RegularExpressions;

namespace Syed.Messaging;

/// <summary>
/// Provides messaging metrics using System.Diagnostics.Metrics.
/// </summary>
public static class MessagingMetrics
{
    public const string MeterName = "Syed.Messaging";
    private static readonly Regex GuidPattern = new(
        @"\b[a-fA-F0-9]{8}-[a-fA-F0-9]{4}-[1-5][a-fA-F0-9]{3}-[89abAB][a-fA-F0-9]{3}-[a-fA-F0-9]{12}\b",
        RegexOptions.Compiled);
    private static readonly Regex LongNumericSegmentPattern = new(@"\b\d{7,}\b", RegexOptions.Compiled);

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
    /// Counter for messages identified as poison (deserialization failures, consistently failing handlers).
    /// A subset of dead-lettered messages — indicates malformed or incompatible messages.
    /// </summary>
    public static readonly Counter<long> MessagesPoisoned = Meter.CreateCounter<long>(
        "messaging.messages.poisoned",
        unit: "{message}",
        description: "Number of messages identified as poison (undeserializable or consistently failing)");

    /// <summary>
    /// Histogram for message processing duration.
    /// </summary>
    public static readonly Histogram<double> ProcessingDuration = Meter.CreateHistogram<double>(
        "messaging.messages.processing_duration",
        unit: "ms",
        description: "Message processing duration in milliseconds");

    public const string DlqReasonDeserializationFailure = "deserialization_failure";
    public const string DlqReasonMaxRetryExhausted = "max_retry_exhausted";
    public const string DlqReasonHandlerException = "handler_exception";
    public const string DlqReasonSchemaValidationFailed = "schema_validation_failed";
    public const string DlqReasonTransportReject = "transport_reject";

    public static KeyValuePair<string, object?>[] BuildDeadLetterTags(
        string transport,
        string destination,
        string messageType,
        string? reason)
    {
        return
        [
            new KeyValuePair<string, object?>("transport", transport.ToLowerInvariant()),
            new KeyValuePair<string, object?>("destination", NormalizeDestination(destination)),
            new KeyValuePair<string, object?>("message_type", messageType),
            new KeyValuePair<string, object?>("reason", NormalizeDeadLetterReason(reason))
        ];
    }

    public static string NormalizeDeadLetterReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return DlqReasonTransportReject;

        var normalized = reason.Trim().ToLowerInvariant().Replace(' ', '_');

        return normalized switch
        {
            "deserialization_failure" => DlqReasonDeserializationFailure,
            "retry_exhausted" => DlqReasonMaxRetryExhausted,
            "max_retry_exhausted" => DlqReasonMaxRetryExhausted,
            "handler_exception" => DlqReasonHandlerException,
            "schema_validation_failed" => DlqReasonSchemaValidationFailed,
            "transport_reject" => DlqReasonTransportReject,
            _ => DlqReasonTransportReject
        };
    }

    public static string NormalizeDestination(string destination)
    {
        if (string.IsNullOrWhiteSpace(destination))
            return "unknown";

        var normalized = destination.Trim().ToLowerInvariant();
        normalized = GuidPattern.Replace(normalized, "{id}");
        normalized = LongNumericSegmentPattern.Replace(normalized, "{n}");

        const int maxLength = 120;
        if (normalized.Length > maxLength)
            normalized = normalized[..maxLength];

        return normalized;
    }
}
