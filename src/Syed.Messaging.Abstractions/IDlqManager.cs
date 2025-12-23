namespace Syed.Messaging;

/// <summary>
/// Interface for managing Dead Letter Queue (DLQ) messages.
/// </summary>
public interface IDlqManager
{
    /// <summary>
    /// Gets the count of messages currently in the DLQ.
    /// </summary>
    Task<long> GetCountAsync(CancellationToken ct = default);

    /// <summary>
    /// Peeks at messages in the DLQ without removing them.
    /// </summary>
    /// <param name="limit">Maximum number of messages to return.</param>
    Task<IReadOnlyList<IMessageEnvelope>> PeekAsync(int limit = 10, CancellationToken ct = default);

    /// <summary>
    /// Requeues a message from the DLQ back to the main queue for reprocessing.
    /// </summary>
    /// <param name="messageId">The ID of the message to requeue.</param>
    Task<bool> RequeueAsync(string messageId, CancellationToken ct = default);

    /// <summary>
    /// Requeues all messages from the DLQ back to the main queue.
    /// </summary>
    /// <returns>Number of messages requeued.</returns>
    Task<int> RequeueAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Permanently removes a message from the DLQ.
    /// </summary>
    Task<bool> DeleteAsync(string messageId, CancellationToken ct = default);

    /// <summary>
    /// Purges all messages from the DLQ.
    /// </summary>
    /// <returns>Number of messages purged.</returns>
    Task<int> PurgeAsync(CancellationToken ct = default);
}
