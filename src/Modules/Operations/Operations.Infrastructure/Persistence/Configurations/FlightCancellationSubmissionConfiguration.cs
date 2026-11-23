using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Operations.Domain.Flights;

namespace Operations.Infrastructure.Persistence.Configurations;

public sealed class FlightCancellationSubmissionConfiguration : IEntityTypeConfiguration<FlightCancellationSubmission>
{
    public void Configure(EntityTypeBuilder<FlightCancellationSubmission> builder)
    {
        builder.ToTable("flight_cancellation_submissions", table => table.HasCheckConstraint("CK_flight_cancellation_submissions_SubmitterSnapshot",
            "([SubmittedByStaffMemberId] IS NULL AND [SubmittedByStaffFullName] IS NULL AND [SubmittedByStaffEmployeeId] IS NULL) OR " +
            "([SubmittedByStaffMemberId] IS NOT NULL AND [SubmittedByStaffFullName] IS NOT NULL AND [SubmittedByStaffEmployeeId] IS NOT NULL)"));
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        // Share the snapshot's column for the per-flight index without duplicating domain state.
        builder.Property<Guid?>("SubmittedByStaffMemberId").HasColumnName("SubmittedByStaffMemberId");

        builder.OwnsOne(c => c.SubmittedByEmployee, employee =>
        {
            employee.Property(e => e.StaffMemberId).HasColumnName("SubmittedByStaffMemberId").IsRequired();
            employee.Property(e => e.FullName).HasColumnName("SubmittedByStaffFullName").HasMaxLength(200).IsRequired();
            employee.Property(e => e.EmployeeId).HasColumnName("SubmittedByStaffEmployeeId").HasMaxLength(50).IsRequired();
        });
        builder.Navigation(c => c.SubmittedByEmployee).IsRequired(false);

        builder.OwnsOne(c => c.Details, details =>
        {
            details.Property(d => d.CanceledAtUtc).HasColumnName("CanceledAtUtc").IsRequired();
            details.Property(d => d.Remarks).HasColumnName("Remarks");
        });
        builder.Navigation(c => c.Details).IsRequired();

        builder.HasIndex(nameof(FlightCancellationSubmission.FlightId), "SubmittedByStaffMemberId").IsUnique()
            .HasFilter("[SubmittedByStaffMemberId] IS NOT NULL");
        builder.HasIndex(c => c.FlightId, "IX_flight_cancellation_submissions_FlightId_Unlinked").IsUnique()
            .HasFilter("[SubmittedByStaffMemberId] IS NULL");
        builder.HasIndex(c => c.FlightId).IsUnique().HasFilter("[IsAccepted] = 1");
    }
}
