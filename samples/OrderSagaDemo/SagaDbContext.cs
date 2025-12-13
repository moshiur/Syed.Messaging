using Microsoft.EntityFrameworkCore;
using Syed.Messaging.Sagas.EfCore;

namespace OrderSagaDemo;

/// <summary>
/// DbContext for saga persistence. Uses SQLite for demo purposes.
/// </summary>
public class SagaDbContext : DbContext
{
    public SagaDbContext(DbContextOptions<SagaDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Configure saga entities (SagaStates and SagaTimeouts tables)
        modelBuilder.ConfigureSagaEntities();
    }
}
