using MasterData.Domain.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MasterData.Infrastructure.Persistence.Configurations;

public sealed class ServiceConfiguration : IEntityTypeConfiguration<Service>
{
    public void Configure(EntityTypeBuilder<Service> builder)
    {
        builder.ToTable("services");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Name).HasMaxLength(100).IsRequired();
        builder.HasIndex(s => s.Name).IsUnique();

        builder.Property(s => s.Description).HasMaxLength(500);
        builder.Property(s => s.IsActive).IsRequired();
        builder.Property(s => s.CreatedAtUtc).IsRequired();
        builder.Property(s => s.UpdatedAtUtc);
        builder.Property(s => s.RowVersion).IsRowVersion();

        builder.Ignore(s => s.DomainEvents);
    }
}

public sealed class ManpowerTypeAllowedServiceConfiguration : IEntityTypeConfiguration<ManpowerTypeAllowedService>
{
    public void Configure(EntityTypeBuilder<ManpowerTypeAllowedService> builder)
    {
        builder.ToTable("manpower_type_allowed_services");
        builder.HasKey(x => new { x.ManpowerTypeId, x.ServiceId });

        builder.Property(x => x.ManpowerTypeId).IsRequired();
        builder.Property(x => x.ServiceId).IsRequired();

        builder.HasOne<MasterData.Domain.ManpowerTypes.ManpowerType>()
            .WithMany()
            .HasForeignKey(x => x.ManpowerTypeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Service>()
            .WithMany()
            .HasForeignKey(x => x.ServiceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.ServiceId);
    }
}
