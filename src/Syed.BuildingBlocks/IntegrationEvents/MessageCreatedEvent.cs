namespace Syed.BuildingBlocks.IntegrationEvents;

/// <summary>
/// Integration event published when a new message is created.
/// Used to notify recipients via SignalR in real-time.
/// </summary>
public record MessageCreatedEvent(
    Guid MessageId,
    Guid PostId,
    Guid SenderId,
    Guid RecipientId,
    string SenderName,
    string Content,
    DateTime CreatedAtUtc);
