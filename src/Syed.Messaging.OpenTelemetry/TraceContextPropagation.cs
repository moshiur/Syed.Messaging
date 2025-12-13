using System.Diagnostics;
using System.Text;

namespace Syed.Messaging.OpenTelemetry;

/// <summary>
/// Utilities for propagating trace context through message headers.
/// </summary>
public static class TraceContextPropagation
{
    public const string TraceParentHeader = "traceparent";
    public const string TraceStateHeader = "tracestate";

    /// <summary>
    /// Injects the current trace context into message headers.
    /// </summary>
    public static void InjectTraceContext(IDictionary<string, string> headers)
    {
        var activity = Activity.Current;
        if (activity is null) return;

        // W3C Trace Context format
        var traceParent = $"00-{activity.TraceId}-{activity.SpanId}-{(activity.Recorded ? "01" : "00")}";
        headers[TraceParentHeader] = traceParent;

        if (!string.IsNullOrEmpty(activity.TraceStateString))
        {
            headers[TraceStateHeader] = activity.TraceStateString;
        }
    }

    /// <summary>
    /// Extracts trace context from message headers.
    /// </summary>
    public static ActivityContext? ExtractTraceContext(IDictionary<string, string> headers)
    {
        if (!headers.TryGetValue(TraceParentHeader, out var traceParent))
            return null;

        headers.TryGetValue(TraceStateHeader, out var traceState);
        return ParseTraceParent(traceParent, traceState);
    }

    /// <summary>
    /// Injects trace context into a byte array header dictionary (for transports like RabbitMQ).
    /// </summary>
    public static void InjectTraceContext(IDictionary<string, object?> headers)
    {
        var activity = Activity.Current;
        if (activity is null) return;

        var traceParent = $"00-{activity.TraceId}-{activity.SpanId}-{(activity.Recorded ? "01" : "00")}";
        headers[TraceParentHeader] = Encoding.UTF8.GetBytes(traceParent);

        if (!string.IsNullOrEmpty(activity.TraceStateString))
        {
            headers[TraceStateHeader] = Encoding.UTF8.GetBytes(activity.TraceStateString);
        }
    }

    /// <summary>
    /// Extracts trace context from a byte array header dictionary.
    /// </summary>
    public static ActivityContext? ExtractTraceContext(IDictionary<string, object?> headers)
    {
        if (!headers.TryGetValue(TraceParentHeader, out var traceParentObj) || traceParentObj is null)
            return null;

        string? traceParent = traceParentObj switch
        {
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            string s => s,
            _ => null
        };

        if (string.IsNullOrEmpty(traceParent))
            return null;

        string? traceState = null;
        if (headers.TryGetValue(TraceStateHeader, out var traceStateObj) && traceStateObj is not null)
        {
            traceState = traceStateObj switch
            {
                byte[] bytes => Encoding.UTF8.GetString(bytes),
                string s => s,
                _ => null
            };
        }

        return ParseTraceParent(traceParent, traceState);
    }

    private static ActivityContext? ParseTraceParent(string traceParent, string? traceState)
    {
        // Parse W3C Trace Context format: 00-{trace-id}-{span-id}-{flags}
        var parts = traceParent.Split('-');
        if (parts.Length < 4)
            return null;

        try
        {
            var traceId = ActivityTraceId.CreateFromString(parts[1].AsSpan());
            var spanId = ActivitySpanId.CreateFromString(parts[2].AsSpan());
            var flags = parts[3] == "01" ? ActivityTraceFlags.Recorded : ActivityTraceFlags.None;

            return new ActivityContext(traceId, spanId, flags, traceState);
        }
        catch
        {
            return null;
        }
    }
}
