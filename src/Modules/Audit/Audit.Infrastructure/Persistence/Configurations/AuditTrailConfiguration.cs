using Audit.Domain.Trails;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Audit.Infrastructure.Persistence.Configurations;

public sealed class AuditTrailConfiguration : IEntityTypeConfiguration<AuditTrail>
{
    public void Configure(EntityTypeBuilder<AuditTrail> builder)
    {
        builder.ToTable("audit_trails");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.EventId).IsRequired();
        builder.HasIndex(a => a.EventId).IsUnique();

        builder.Property(a => a.OccurredOnUtc).IsRequired();

        builder.Property(a => a.ActorId);
        builder.Property(a => a.ActorDisplayName).HasMaxLength(150);
        builder.Property(a => a.IsSystemActor).IsRequired();

        builder.Property(a => a.Module).HasMaxLength(50).IsRequired();
        builder.Property(a => a.RootSubjectType).HasMaxLength(100).IsRequired();
        builder.Property(a => a.RootSubjectId);
        builder.Property(a => a.EntityType).HasMaxLength(100).IsRequired();
        builder.Property(a => a.EntityId);
        builder.Property(a => a.Action).HasMaxLength(60).IsRequired();

        builder.Property(a => a.CorrelationId).HasMaxLength(200);
        builder.Property(a => a.ChangesJson);
        builder.Property(a => a.Metadata).HasMaxLength(2000);

        // Common access paths: a subject's timeline and an entity's timeline, newest first.
        builder.HasIndex(a => new { a.RootSubjectType, a.RootSubjectId, a.OccurredOnUtc });
        builder.HasIndex(a => new { a.EntityType, a.EntityId, a.OccurredOnUtc });
        builder.HasIndex(a => a.OccurredOnUtc);
    }
}
