using MasterData.Domain.OperationTypes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MasterData.Infrastructure.Persistence.Configurations;

public sealed class OperationTypeConfiguration : IEntityTypeConfiguration<OperationType>
{
    public void Configure(EntityTypeBuilder<OperationType> builder)
    {
        builder.ToTable("operation_types");
        builder.HasKey(o => o.Id);

        builder.Property(o => o.Name).HasMaxLength(100).IsRequired();
        builder.HasIndex(o => o.Name).IsUnique();

        builder.Property(o => o.Description).HasMaxLength(500);
        builder.Property(o => o.IsActive).IsRequired();
        builder.Property(o => o.CreatedAtUtc).IsRequired();
        builder.Property(o => o.UpdatedAtUtc);
        builder.Property(o => o.RowVersion).IsRowVersion();

        builder.Ignore(o => o.DomainEvents);
    }
}
