using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Operations.Domain.Flights;

namespace Operations.Infrastructure.Persistence.Configurations;

public sealed class FlightArrivalSubmissionConfiguration : IEntityTypeConfiguration<FlightArrivalSubmission>
{
    public void Configure(EntityTypeBuilder<FlightArrivalSubmission> builder)
    {
        builder.ToTable("flight_arrival_submissions");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        // This EF-only shadow property defines the composite index on the owner. Its value
        // shares the snapshot's column; SubmittedByEmployee supplies the staff identity.
        builder.Property<Guid>("SubmittedByStaffMemberId").HasColumnName("SubmittedByStaffMemberId").IsRequired();

        builder.OwnsOne(a => a.SubmittedByEmployee, employee =>
        {
            employee.Property(e => e.StaffMemberId).HasColumnName("SubmittedByStaffMemberId").IsRequired();
            employee.Property(e => e.FullName).HasColumnName("SubmittedByStaffFullName").HasMaxLength(200).IsRequired();
            employee.Property(e => e.EmployeeId).HasColumnName("SubmittedByStaffEmployeeId").HasMaxLength(50).IsRequired();
        });
        builder.Navigation(a => a.SubmittedByEmployee).IsRequired();

        builder.OwnsOne(a => a.Details, details =>
        {
            details.Property(d => d.CustomerWorkReferenceNumber).HasColumnName("CustomerWorkReferenceNumber");
            details.Property(d => d.Ata).HasColumnName("ActualArrivalUtc").IsRequired();
            details.Property(d => d.AircraftTailNumber).HasColumnName("AircraftTailNumber").IsRequired();
            details.Property(d => d.Remarks).HasColumnName("Remarks");

            details.OwnsOne(d => d.AircraftType, aircraft =>
            {
                aircraft.Property(a => a.AircraftTypeId).HasColumnName("AircraftTypeId").IsRequired();
                aircraft.Property(a => a.Manufacturer).HasColumnName("AircraftManufacturer").HasMaxLength(100).IsRequired();
                aircraft.Property(a => a.Model).HasColumnName("AircraftModel").HasMaxLength(50).IsRequired();
            });
            details.Navigation(d => d.AircraftType).IsRequired();

            details.OwnsOne(d => d.ReceivedByEmployee, employee =>
            {
                employee.Property(e => e.StaffMemberId).HasColumnName("ReceivedByStaffMemberId").IsRequired();
                employee.Property(e => e.FullName).HasColumnName("ReceivedByStaffFullName").HasMaxLength(200).IsRequired();
                employee.Property(e => e.EmployeeId).HasColumnName("ReceivedByStaffEmployeeId").HasMaxLength(50).IsRequired();
            });
            details.Navigation(d => d.ReceivedByEmployee).IsRequired();
        });
        builder.Navigation(a => a.Details).IsRequired();

        builder.HasIndex(nameof(FlightArrivalSubmission.FlightId), "SubmittedByStaffMemberId").IsUnique();
        builder.HasIndex(a => a.FlightId).IsUnique().HasFilter("[IsAccepted] = 1");
    }
}
