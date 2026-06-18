using Identity.Domain.Aggregates.User;
using Identity.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Identity.Infrastructure.Persistence.Configurations;

public sealed class PasswordHistoryConfiguration : IEntityTypeConfiguration<PasswordHistoryEntry>
{
    public void Configure(EntityTypeBuilder<PasswordHistoryEntry> builder)
    {
        builder.ToTable("PasswordHistory", "identity");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.UserId)
            .HasConversion(v => v.Value, v => UserId.From(v));

        builder.Property(x => x.Hash)
            .HasConversion(v => v.Value, v => PasswordHash.Create(v).Value!)
            .IsRequired()
            .HasMaxLength(500);

        builder.HasIndex(x => new { x.UserId, x.CreatedAt });
    }
}
