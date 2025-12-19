namespace Syed.Messaging;

/// <summary>
/// Interface for inbox storage to support idempotent message processing.
/// </summary>
public interface IInboxStore
{
    /// <summary>
    /// Checks if a message has already been processed.
    /// </summary>
    /// <param name="messageId">The unique ID of the message.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the message has been processed; otherwise, false.</returns>
    Task<bool> HasBeenProcessedAsync(string messageId, CancellationToken ct = default);

    /// <summary>
    /// Marks a message as processed.
    /// </summary>
    /// <param name="messageId">The unique ID of the message.</param>
    /// <param name="expiration">Optional expiration time for the inbox record.</param>
    /// <param name="ct">Cancellation token.</param>
    Task MarkProcessedAsync(string messageId, DateTimeOffset? expiration = null, CancellationToken ct = default);
}
