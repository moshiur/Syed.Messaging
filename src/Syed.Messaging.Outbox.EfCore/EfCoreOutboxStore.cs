using Microsoft.EntityFrameworkCore;
using Syed.Messaging;

namespace Syed.Messaging.Outbox;

public class EfCoreOutboxStore<TContext> : IOutboxStore where TContext : DbContext
{
    private readonly TContext _db;

    public EfCoreOutboxStore(TContext db)
    {
        _db = db;
    }

    public async Task SaveAsync(OutboxMessage message, CancellationToken ct)
    {
        _db.Set<OutboxMessage>().Add(message);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(int batchSize, CancellationToken ct)
    {
        return await _db.Set<OutboxMessage>()
            .Where(x => x.ProcessedAtUtc == null)
            .OrderBy(x => x.CreatedAtUtc)
            .Take(batchSize)
            .ToListAsync(ct);
    }

    public async Task MarkAsProcessedAsync(Guid id, CancellationToken ct)
    {
        var entity = await _db.Set<OutboxMessage>().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return;
        entity.ProcessedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }
}
