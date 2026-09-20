using MasterData.Domain.AtaChapters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MasterData.Infrastructure.Persistence.Configurations;

public sealed class AtaChapterCategoryConfiguration : IEntityTypeConfiguration<AtaChapterCategory>
{
    public void Configure(EntityTypeBuilder<AtaChapterCategory> builder)
    {
        builder.ToTable("ata_chapter_categories");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => x.Name).IsUnique();
        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.Ignore(x => x.DomainEvents);
    }
}

public sealed class AtaChapterConfiguration : IEntityTypeConfiguration<AtaChapter>
{
    public void Configure(EntityTypeBuilder<AtaChapter> builder)
    {
        builder.ToTable("ata_chapters");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(20).IsRequired();
        builder.HasIndex(x => x.Code).IsUnique();
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.Ignore(x => x.DomainEvents);
    }
}
