using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Operations.Domain.Flights;

namespace Operations.Infrastructure.Persistence.Configurations;

public sealed class FlightDepartureSubmissionConfiguration : IEntityTypeConfiguration<FlightDepartureSubmission>
{
    public void Configure(EntityTypeBuilder<FlightDepartureSubmission> builder)
    {
        builder.ToTable("flight_departure_submissions");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        // Share the snapshot's column for the per-flight index without duplicating domain state.
        builder.Property<Guid>("SubmittedByStaffMemberId").HasColumnName("SubmittedByStaffMemberId").IsRequired();

        builder.OwnsOne(d => d.SubmittedByEmployee, employee =>
        {
            employee.Property(e => e.StaffMemberId).HasColumnName("SubmittedByStaffMemberId").IsRequired();
            employee.Property(e => e.FullName).HasColumnName("SubmittedByStaffFullName").HasMaxLength(200).IsRequired();
            employee.Property(e => e.EmployeeId).HasColumnName("SubmittedByStaffEmployeeId").HasMaxLength(50).IsRequired();
        });
        builder.Navigation(d => d.SubmittedByEmployee).IsRequired();

        builder.OwnsOne(d => d.Details, details =>
        {
            details.Property(d => d.Atd).HasColumnName("ActualDepartureUtc").IsRequired();
            details.Property(d => d.Remarks).HasColumnName("Remarks");

            details.OwnsOne(d => d.CustomerSignature, signature =>
            {
                signature.Property(s => s.StorageKey).HasColumnName("CustomerSignatureStorageKey").IsRequired();
                signature.Property(s => s.ContentType).HasColumnName("CustomerSignatureContentType").IsRequired();
                signature.Property(s => s.SizeBytes).HasColumnName("CustomerSignatureSizeBytes").IsRequired();
            });
            details.Navigation(d => d.CustomerSignature).IsRequired();

            details.OwnsOne(d => d.HandledByEmployee, employee =>
            {
                employee.Property(e => e.StaffMemberId).HasColumnName("HandledByStaffMemberId").IsRequired();
                employee.Property(e => e.FullName).HasColumnName("HandledByStaffFullName").HasMaxLength(200).IsRequired();
                employee.Property(e => e.EmployeeId).HasColumnName("HandledByStaffEmployeeId").HasMaxLength(50).IsRequired();
            });
            details.Navigation(d => d.HandledByEmployee).IsRequired();
        });
        builder.Navigation(d => d.Details).IsRequired();

        builder.HasIndex(nameof(FlightDepartureSubmission.FlightId), "SubmittedByStaffMemberId").IsUnique();
        builder.HasIndex(d => d.FlightId).IsUnique().HasFilter("[IsAccepted] = 1");
    }
}
