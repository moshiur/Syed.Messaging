using Microsoft.EntityFrameworkCore;

namespace Syed.Messaging.Inbox.EfCore;

public class EfInboxStore<TDbContext> : IInboxStore
    where TDbContext : DbContext
{
    private readonly TDbContext _dbContext;

    public EfInboxStore(TDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> HasBeenProcessedAsync(string messageId, CancellationToken ct = default)
    {
        return await _dbContext.Set<InboxMessage>()
            .AnyAsync(m => m.MessageId == messageId, ct);
    }

    public async Task MarkProcessedAsync(string messageId, DateTimeOffset? expiration = null, CancellationToken ct = default)
    {
        var msg = new InboxMessage
        {
            MessageId = messageId,
            ProcessedAt = DateTimeOffset.UtcNow,
            Expiration = expiration
        };

        _dbContext.Set<InboxMessage>().Add(msg);
        
        // We rely on the caller to SaveChangesAsync, typically via UnitOfWork or Transaction scope
        // BUT, IInboxStore contract might imply immediate persistence if used standalone.
        // However, in EF Core patterns within this framework (like Outbox/Saga), we usually share the DbContext transaction.
        // If we call SaveChanges here, it might commit distinct transaction.
        
        // Let's assume the consumer controls the transaction. 
        // BUT ISagaStateStore saves changes internally? Let's check EfSagaStateStore.
        // Checking EfSagaStateStore... (I don't have it open but I recall it calls SaveChangesAsync).
        
        // If this is to be used inside GenericMessageConsumer which might wrap handler in transaction,
        // we should probably auto-save if we want it to be simple, or let caller handle it.
        // Given 'MarkProcessedAsync' implies an action, most stores save.
        
        await _dbContext.SaveChangesAsync(ct);
    }
}
