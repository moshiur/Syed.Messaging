using System.Diagnostics;

namespace Syed.Messaging.OpenTelemetry;

/// <summary>
/// OpenTelemetry instrumentation for Syed.Messaging.
/// Provides Activity sources for tracing message publishing, consumption, and handling.
/// </summary>
public static class MessagingActivitySource
{
    /// <summary>
    /// The name of the activity source used for all messaging instrumentation.
    /// </summary>
    public const string SourceName = "Syed.Messaging";

    /// <summary>
    /// The version of the instrumentation.
    /// </summary>
    public const string Version = "1.0.0";

    /// <summary>
    /// The activity source for messaging operations.
    /// </summary>
    public static readonly ActivitySource Source = new(SourceName, Version);

    // Semantic conventions for messaging spans
    public static class Tags
    {
        public const string System = "messaging.system";
        public const string Destination = "messaging.destination";
        public const string MessageId = "messaging.message_id";
        public const string CorrelationId = "messaging.correlation_id";
        public const string CausationId = "messaging.causation_id";
        public const string MessageType = "messaging.message_type";
        public const string MessageVersion = "messaging.message_version";
        public const string Operation = "messaging.operation";
        public const string RetryCount = "messaging.retry_count";
        public const string ConsumerGroup = "messaging.consumer_group";
        public const string SagaType = "messaging.saga.type";
        public const string SagaCorrelationKey = "messaging.saga.correlation_key";
    }

    public static class Operations
    {
        public const string Publish = "publish";
        public const string Receive = "receive";
        public const string Process = "process";
    }

    /// <summary>
    /// Starts a new activity for publishing a message.
    /// </summary>
    public static Activity? StartPublishActivity(
        string destination,
        string messageType,
        string? messageId = null,
        string? correlationId = null,
        string? system = null)
    {
        var activity = Source.StartActivity(
            $"{messageType} {Operations.Publish}",
            ActivityKind.Producer);

        if (activity is not null)
        {
            activity.SetTag(Tags.Operation, Operations.Publish);
            activity.SetTag(Tags.Destination, destination);
            activity.SetTag(Tags.MessageType, messageType);
            
            if (system is not null)
                activity.SetTag(Tags.System, system);
            if (messageId is not null)
                activity.SetTag(Tags.MessageId, messageId);
            if (correlationId is not null)
                activity.SetTag(Tags.CorrelationId, correlationId);
        }

        return activity;
    }

    /// <summary>
    /// Starts a new activity for receiving a message.
    /// </summary>
    public static Activity? StartReceiveActivity(
        string destination,
        string messageType,
        string? messageId = null,
        string? correlationId = null,
        string? system = null,
        ActivityContext? parentContext = null)
    {
        var activity = parentContext.HasValue
            ? Source.StartActivity($"{messageType} {Operations.Receive}", ActivityKind.Consumer, parentContext.Value)
            : Source.StartActivity($"{messageType} {Operations.Receive}", ActivityKind.Consumer);

        if (activity is not null)
        {
            activity.SetTag(Tags.Operation, Operations.Receive);
            activity.SetTag(Tags.Destination, destination);
            activity.SetTag(Tags.MessageType, messageType);
            
            if (system is not null)
                activity.SetTag(Tags.System, system);
            if (messageId is not null)
                activity.SetTag(Tags.MessageId, messageId);
            if (correlationId is not null)
                activity.SetTag(Tags.CorrelationId, correlationId);
        }

        return activity;
    }

    /// <summary>
    /// Starts a new activity for processing/handling a message.
    /// </summary>
    public static Activity? StartProcessActivity(
        string messageType,
        string handlerType,
        string? correlationId = null)
    {
        var activity = Source.StartActivity(
            $"{handlerType} {Operations.Process}",
            ActivityKind.Internal);

        if (activity is not null)
        {
            activity.SetTag(Tags.Operation, Operations.Process);
            activity.SetTag(Tags.MessageType, messageType);
            activity.SetTag("messaging.handler_type", handlerType);
            
            if (correlationId is not null)
                activity.SetTag(Tags.CorrelationId, correlationId);
        }

        return activity;
    }

    /// <summary>
    /// Starts a new activity for saga processing.
    /// </summary>
    public static Activity? StartSagaActivity(
        string sagaType,
        string correlationKey,
        string messageType)
    {
        var activity = Source.StartActivity(
            $"{sagaType} handle {messageType}",
            ActivityKind.Internal);

        if (activity is not null)
        {
            activity.SetTag(Tags.SagaType, sagaType);
            activity.SetTag(Tags.SagaCorrelationKey, correlationKey);
            activity.SetTag(Tags.MessageType, messageType);
        }

        return activity;
    }

    /// <summary>
    /// Records an exception on the current activity.
    /// </summary>
    public static void RecordException(Activity? activity, Exception exception)
    {
        activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity?.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
        {
            { "exception.type", exception.GetType().FullName },
            { "exception.message", exception.Message },
            { "exception.stacktrace", exception.StackTrace }
        }));
    }
}
