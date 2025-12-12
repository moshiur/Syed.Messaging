using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Syed.Messaging;
using Syed.Messaging.Sagas;

namespace Syed.Messaging.Sagas.EfCore;

/// <summary>
/// Extension methods for configuring EF Core-based saga persistence.
/// </summary>
public static class SagaEfCoreServiceCollectionExtensions
{
    /// <summary>
    /// Adds EF Core saga state store for the specified saga state type.
    /// </summary>
    public static IServiceCollection AddEfSagaStateStore<TContext, TSagaState>(this IServiceCollection services)
        where TContext : DbContext
        where TSagaState : class, ISagaState, new()
    {
        services.AddScoped<ISagaStateStore<TSagaState>, EfSagaStateStore<TContext, TSagaState>>();
        return services;
    }

    /// <summary>
    /// Adds EF Core saga timeout store.
    /// </summary>
    public static IServiceCollection AddEfSagaTimeoutStore<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        services.AddScoped<ISagaTimeoutStore, EfSagaTimeoutStore<TContext>>();
        return services;
    }

    /// <summary>
    /// Configures the DbContext model for saga entities.
    /// Call this from your DbContext.OnModelCreating method.
    /// </summary>
    public static void ConfigureSagaEntities(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SagaStateEntity>(entity =>
        {
            entity.ToTable("SagaStates");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.SagaType, e.CorrelationKey }).IsUnique();
            
            entity.Property(e => e.SagaType).IsRequired().HasMaxLength(256);
            entity.Property(e => e.CorrelationKey).IsRequired().HasMaxLength(256);
            entity.Property(e => e.StateJson).IsRequired();
            entity.Property(e => e.Version).IsConcurrencyToken();
        });

        modelBuilder.Entity<SagaTimeoutEntity>(entity =>
        {
            entity.ToTable("SagaTimeouts");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.SagaTypeKey, e.CorrelationKey });
            entity.HasIndex(e => new { e.DueAtUtc, e.Cancelled, e.Processed });

            entity.Property(e => e.SagaTypeKey).IsRequired().HasMaxLength(256);
            entity.Property(e => e.TimeoutTypeKey).IsRequired().HasMaxLength(256);
            entity.Property(e => e.TimeoutTypeVersion).HasMaxLength(64);
            entity.Property(e => e.CorrelationKey).IsRequired().HasMaxLength(256);
        });
    }
}
