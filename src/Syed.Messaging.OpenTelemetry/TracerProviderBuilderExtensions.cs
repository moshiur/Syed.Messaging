using OpenTelemetry.Trace;

namespace Syed.Messaging.OpenTelemetry;

/// <summary>
/// Extension methods for configuring OpenTelemetry tracing with Syed.Messaging.
/// </summary>
public static class TracerProviderBuilderExtensions
{
    /// <summary>
    /// Adds Syed.Messaging instrumentation to the tracer provider.
    /// </summary>
    public static TracerProviderBuilder AddSyedMessagingInstrumentation(
        this TracerProviderBuilder builder)
    {
        return builder.AddSource(MessagingActivitySource.SourceName);
    }
}
