using MasterData.Domain.GeneralSupports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MasterData.Infrastructure.Persistence.Configurations;

public sealed class GeneralSupportConfiguration : IEntityTypeConfiguration<GeneralSupport>
{
    public void Configure(EntityTypeBuilder<GeneralSupport> builder)
    {
        builder.ToTable("general_supports");
        builder.HasKey(g => g.Id);

        builder.Property(g => g.Name).HasMaxLength(200).IsRequired();
        builder.HasIndex(g => g.Name).IsUnique();

        builder.Property(g => g.Description).HasMaxLength(500);
        builder.Property(g => g.IsActive).IsRequired();
        builder.Property(g => g.CreatedAtUtc).IsRequired();
        builder.Property(g => g.UpdatedAtUtc);
        builder.Property(g => g.RowVersion).IsRowVersion();

        builder.Ignore(g => g.DomainEvents);
    }
}
