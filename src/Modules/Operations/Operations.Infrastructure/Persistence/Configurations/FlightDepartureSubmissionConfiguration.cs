using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Operations.Domain.Flights;

namespace Operations.Infrastructure.Persistence.Configurations;

public sealed class FlightDepartureSubmissionConfiguration : IEntityTypeConfiguration<FlightDepartureSubmission>
{
    public void Configure(EntityTypeBuilder<FlightDepartureSubmission> builder)
    {
        builder.ToTable("flight_departure_submissions", table => table.HasCheckConstraint("CK_flight_departure_submissions_SubmitterSnapshot",
            "([SubmittedByStaffMemberId] IS NULL AND [SubmittedByStaffFullName] IS NULL AND [SubmittedByStaffEmployeeId] IS NULL) OR " +
            "([SubmittedByStaffMemberId] IS NOT NULL AND [SubmittedByStaffFullName] IS NOT NULL AND [SubmittedByStaffEmployeeId] IS NOT NULL)"));
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        // Share the snapshot's column for the per-flight index without duplicating domain state.
        builder.Property<Guid?>("SubmittedByStaffMemberId").HasColumnName("SubmittedByStaffMemberId");

        builder.OwnsOne(d => d.SubmittedByEmployee, employee =>
        {
            employee.Property(e => e.StaffMemberId).HasColumnName("SubmittedByStaffMemberId").IsRequired();
            employee.Property(e => e.FullName).HasColumnName("SubmittedByStaffFullName").HasMaxLength(200).IsRequired();
            employee.Property(e => e.EmployeeId).HasColumnName("SubmittedByStaffEmployeeId").HasMaxLength(50).IsRequired();
        });
        builder.Navigation(d => d.SubmittedByEmployee).IsRequired(false);

        builder.OwnsOne(d => d.Details, details =>
        {
            details.Property(d => d.Atd).HasColumnName("ActualDepartureUtc").IsRequired();
            details.Property(d => d.Remarks).HasColumnName("Remarks");

            details.OwnsOne(d => d.CustomerSignature, signature =>
            {
                signature.Property(s => s.FileName).HasColumnName("CustomerSignatureFileName").HasMaxLength(255).IsRequired();
                signature.Property(s => s.StorageKey).HasColumnName("CustomerSignatureStorageKey").HasMaxLength(500).IsRequired();
                signature.Property(s => s.ContentType).HasColumnName("CustomerSignatureContentType").HasMaxLength(100).IsRequired();
                signature.Property(s => s.SizeBytes).HasColumnName("CustomerSignatureSizeBytes").IsRequired();
                signature.Property(s => s.SignedAtUtc).HasColumnName("CustomerSignedAtUtc").IsRequired();
            });
            details.Navigation(d => d.CustomerSignature).IsRequired(false);

            details.OwnsOne(d => d.HandledByEmployee, employee =>
            {
                employee.Property(e => e.StaffMemberId).HasColumnName("HandledByStaffMemberId").IsRequired();
                employee.Property(e => e.FullName).HasColumnName("HandledByStaffFullName").HasMaxLength(200).IsRequired();
                employee.Property(e => e.EmployeeId).HasColumnName("HandledByStaffEmployeeId").HasMaxLength(50).IsRequired();
            });
            details.Navigation(d => d.HandledByEmployee).IsRequired();
        });
        builder.Navigation(d => d.Details).IsRequired();

        builder.HasIndex(nameof(FlightDepartureSubmission.FlightId), "SubmittedByStaffMemberId").IsUnique()
            .HasFilter("[SubmittedByStaffMemberId] IS NOT NULL");
        builder.HasIndex(d => d.FlightId, "IX_flight_departure_submissions_FlightId_Unlinked").IsUnique()
            .HasFilter("[SubmittedByStaffMemberId] IS NULL");
        builder.HasIndex(d => d.FlightId).IsUnique().HasFilter("[IsAccepted] = 1");
    }
}
