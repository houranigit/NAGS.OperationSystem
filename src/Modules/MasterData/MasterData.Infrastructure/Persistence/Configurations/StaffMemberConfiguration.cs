using MasterData.Domain.StaffMembers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MasterData.Infrastructure.Persistence.Configurations;

public sealed class StaffMemberConfiguration : IEntityTypeConfiguration<StaffMember>
{
    public void Configure(EntityTypeBuilder<StaffMember> builder)
    {
        builder.ToTable("staff_members");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.FullName).HasMaxLength(200).IsRequired();
        builder.Property(s => s.Email).HasMaxLength(256).IsRequired();
        builder.HasIndex(s => s.Email).IsUnique();

        builder.Property(s => s.StationId).IsRequired();
        builder.HasIndex(s => s.StationId);

        builder.Property(s => s.ManpowerTypeId).IsRequired();
        builder.HasIndex(s => s.ManpowerTypeId);

        builder.Property(s => s.EmploymentStartDate);
        builder.Property(s => s.EmploymentEndDate);
        builder.Property(s => s.WorkingScheduleMask);
        builder.Property(s => s.LinkedUserId);
        builder.Property(s => s.PortalState).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(s => s.PortalCorrelationId);
        builder.Property(s => s.PortalFailureReason).HasMaxLength(500);

        // VO accessors are derived from the scalar columns above; not separately persisted.
        builder.Ignore(s => s.EmploymentContract);
        builder.Ignore(s => s.WorkingSchedule);

        builder.Property(s => s.IsActive).IsRequired();
        builder.Property(s => s.CreatedAtUtc).IsRequired();
        builder.Property(s => s.UpdatedAtUtc);

        builder.Property(s => s.RowVersion).IsRowVersion();

        builder.HasMany(s => s.Licenses)
            .WithOne()
            .HasForeignKey(l => l.StaffMemberId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Metadata.FindNavigation(nameof(StaffMember.Licenses))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(s => s.DomainEvents);
    }
}

public sealed class StaffMemberLicenseConfiguration : IEntityTypeConfiguration<StaffMemberLicense>
{
    public void Configure(EntityTypeBuilder<StaffMemberLicense> builder)
    {
        builder.ToTable("staff_member_licenses");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();

        builder.Property(l => l.StaffMemberId).IsRequired();
        builder.Property(l => l.LicenseId).IsRequired();
        builder.Property(l => l.LicenseNumber).HasMaxLength(100).IsRequired();

        // A staff member cannot hold the same License type more than once.
        builder.HasIndex(l => new { l.StaffMemberId, l.LicenseId }).IsUnique();
        builder.HasIndex(l => l.LicenseId);
    }
}
