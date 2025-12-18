using Microsoft.EntityFrameworkCore;
using Syed.Messaging.Inbox.EfCore;
using Syed.Messaging.Sagas.EfCore;

namespace OrderSagaDemo;

/// <summary>
/// DbContext for saga persistence. Uses SQLite for demo purposes.
/// </summary>
public class SagaDbContext : DbContext
{
    public SagaDbContext(DbContextOptions<SagaDbContext> options) : base(options) { }

    // Saga State
    public DbSet<SagaStateEntity> SagaStates { get; set; }

    // Saga Timeouts
    public DbSet<SagaTimeoutEntity> SagaTimeouts { get; set; }

    // Inbox
    public DbSet<InboxMessage> InboxMessages { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        // Apply configurations from packages
        modelBuilder.ApplyConfiguration(new EfSagaStateConfiguration());
        modelBuilder.ApplyConfiguration(new EfSagaTimeoutConfiguration());
        
        // Inbox doesn't have a separate Config class right now, but relies on Attributes.
        // Or we can add one? Let's assume Attributes [Key] work for now.
    }
}
