namespace Syed.Messaging;

/// <summary>
/// Defines a middleware component that participates in message processing.
/// Middlewares execute after deserialization but before the handler,
/// allowing cross-cutting concerns like tenant context propagation.
/// </summary>
public interface IMessageMiddleware
{
    /// <summary>
    /// Process the message envelope and optionally call <paramref name="next"/> to continue the pipeline.
    /// Throwing an exception will trigger the consumer's retry/DLQ logic.
    /// </summary>
    /// <param name="envelope">The raw message envelope with headers and body.</param>
    /// <param name="serviceProvider">The scoped service provider for resolving dependencies.</param>
    /// <param name="next">Delegate to invoke the next middleware or the handler.</param>
    Task InvokeAsync(IMessageEnvelope envelope, IServiceProvider serviceProvider, Func<Task> next);
}
