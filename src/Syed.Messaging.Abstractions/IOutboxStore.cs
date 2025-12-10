namespace Syed.Messaging;

public interface IOutboxStore
{
    Task SaveAsync(OutboxMessage message, CancellationToken ct);
    Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(int batchSize, CancellationToken ct);
    Task MarkAsProcessedAsync(Guid id, CancellationToken ct);
}

public sealed class OutboxMessage
{
    public Guid Id { get; init; }
    public string Destination { get; init; } = default!;
    public string MessageType { get; init; } = default!; // CLR type name by default
    public string? Version { get; init; }                // optional message version
    public byte[] Payload { get; init; } = Array.Empty<byte>();
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? ProcessedAtUtc { get; set; }
}
