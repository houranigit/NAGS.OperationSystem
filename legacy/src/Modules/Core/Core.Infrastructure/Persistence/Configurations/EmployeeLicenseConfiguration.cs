using Core.Domain.Aggregates.Employee;
using Core.Domain.Aggregates.License;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Core.Infrastructure.Persistence.Configurations;

public sealed class EmployeeLicenseConfiguration : IEntityTypeConfiguration<EmployeeLicense>
{
    public void Configure(EntityTypeBuilder<EmployeeLicense> builder)
    {
        builder.ToTable("EmployeeLicenses", "core");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion(v => v.Value, v => EmployeeLicenseId.From(v));

        builder.Property(x => x.EmployeeId)
            .HasConversion(v => v.Value, v => EmployeeId.From(v))
            .IsRequired();

        builder.Property(x => x.LicenseId)
            .HasConversion(v => v.Value, v => LicenseId.From(v))
            .IsRequired();

        builder.Property(x => x.LicenseNumber)
            .IsRequired()
            .HasMaxLength(100);

        builder.HasIndex(x => new { x.EmployeeId, x.LicenseId }).IsUnique();
        builder.HasIndex(x => x.LicenseNumber);
    }
}
