using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Syed.Messaging.Sagas.EfCore;

public class EfSagaStateConfiguration : IEntityTypeConfiguration<SagaStateEntity>
{
    public void Configure(EntityTypeBuilder<SagaStateEntity> builder)
    {
        builder.ToTable("SagaStates");
        builder.HasKey(x => x.Id);
        
        builder.Property(x => x.SagaType).IsRequired().HasMaxLength(200);
        builder.Property(x => x.CorrelationKey).IsRequired().HasMaxLength(200);
        builder.Property(x => x.StateJson).IsRequired();
        
        builder.HasIndex(x => new { x.SagaType, x.CorrelationKey }).IsUnique();
        builder.HasIndex(x => new { x.SagaType, x.CorrelationKey, x.IsCompleted });
    }
}

public class EfSagaTimeoutConfiguration : IEntityTypeConfiguration<SagaTimeoutEntity>
{
    public void Configure(EntityTypeBuilder<SagaTimeoutEntity> builder)
    {
        builder.ToTable("SagaTimeouts");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.SagaTypeKey).IsRequired().HasMaxLength(200);
        builder.Property(x => x.CorrelationKey).IsRequired().HasMaxLength(200);
        builder.Property(x => x.TimeoutTypeKey).IsRequired().HasMaxLength(200);
        
        builder.HasIndex(x => new { x.SagaTypeKey, x.CorrelationKey });
        builder.HasIndex(x => x.DueAtUtc);
        builder.HasIndex(x => x.Processed);
    }
}
