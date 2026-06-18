using Core.Domain.Aggregates.Employee;
using Core.Domain.Aggregates.ManpowerType;
using Core.Domain.Aggregates.Station;
using Core.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Core.Infrastructure.Persistence.Configurations;

public sealed class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> builder)
    {
        builder.ToTable("Employees", "core");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion(v => v.Value, v => EmployeeId.From(v));

        builder.Property(x => x.FullName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.Email)
            .IsRequired()
            .HasMaxLength(254);

        builder.HasIndex(x => x.Email).IsUnique();

        builder.Property(x => x.Logo)
            .IsRequired(false)
            .HasColumnType("varbinary(max)");

        builder.Property(x => x.ManpowerTypeId)
            .HasConversion(v => v.Value, v => ManpowerTypeId.From(v))
            .IsRequired();

        builder.Property(x => x.StationId)
            .HasConversion(v => v.Value, v => StationId.From(v))
            .IsRequired();

        builder.OwnsOne(x => x.Contract, contract =>
        {
            contract.Property(c => c.From)
                .HasColumnName("Contract_From")
                .IsRequired();

            contract.Property(c => c.To)
                .HasColumnName("Contract_To")
                .IsRequired(false);
        });

        builder.Property(x => x.WorkingSchedule)
            .HasConversion(
                v => v.Mask,
                v => WorkingSchedule.FromMask(v))
            .HasColumnName("WorkingSchedule")
            .IsRequired();

        builder.HasMany(x => x.Licenses)
            .WithOne()
            .HasForeignKey(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(x => x.LinkedUserId).IsRequired(false);

        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => x.FullName);
        builder.HasIndex(x => x.CreatedAt);
    }
}
