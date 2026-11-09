using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Operations.Domain.Flights;

namespace Operations.Infrastructure.Persistence.Configurations;

public sealed class FlightReturnToRampConfiguration : IEntityTypeConfiguration<FlightReturnToRamp>
{
    public void Configure(EntityTypeBuilder<FlightReturnToRamp> builder)
    {
        builder.ToTable("flight_return_to_ramps", table =>
        {
            table.HasCheckConstraint("CK_flight_return_to_ramps_Sequence", "[Sequence] > 0");
            table.HasCheckConstraint("CK_flight_return_to_ramps_Window", "[RtrDepartureUtc] IS NULL OR [RtrDepartureUtc] >= [RtrArrivalUtc]");
        });
        builder.HasKey(r => r.Id);
        builder.HasAlternateKey(r => new { r.Id, r.FlightId });
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.FlightId).IsRequired();
        builder.Property(r => r.Sequence).IsRequired();
        builder.HasIndex(r => new { r.FlightId, r.Sequence }).IsUnique();

        // EF-only index property shares the departure VO column; there is no duplicate CLR time.
        builder.Property<DateTimeOffset?>("DepartureAtUtc").HasColumnName("RtrDepartureUtc");
        builder.HasIndex(nameof(FlightReturnToRamp.FlightId), "DepartureAtUtc").IsUnique()
            .HasFilter("[RtrDepartureUtc] IS NULL");

        builder.OwnsOne(r => r.ArrivalDetails, arrival =>
        {
            arrival.Property(a => a.ArrivedAtUtc).HasColumnName("RtrArrivalUtc").IsRequired();
            arrival.Property(a => a.Remarks).HasColumnName("ArrivalRemarks").HasMaxLength(2000);
            arrival.OwnsOne(a => a.ReceivedByEmployee, employee =>
            {
                employee.Property(e => e.StaffMemberId).HasColumnName("ArrivalReceivedByStaffMemberId").IsRequired();
                employee.Property(e => e.FullName).HasColumnName("ArrivalReceivedByStaffFullName").HasMaxLength(200).IsRequired();
                employee.Property(e => e.EmployeeId).HasColumnName("ArrivalReceivedByStaffEmployeeId").HasMaxLength(50).IsRequired();
            });
            arrival.Navigation(a => a.ReceivedByEmployee).IsRequired();
        });
        builder.Navigation(r => r.ArrivalDetails).IsRequired();

        builder.OwnsOne(r => r.DepartureDetails, departure =>
        {
            departure.Property(d => d.DepartedAtUtc).HasColumnName("RtrDepartureUtc").IsRequired();
            departure.Property(d => d.Remarks).HasColumnName("DepartureRemarks").HasMaxLength(2000);
            // The optional departure owns nested details; this existing required column identifies its presence.
            departure.Property<Guid>("HandledByStaffMemberId").HasColumnName("DepartureHandledByStaffMemberId").IsRequired();
            departure.OwnsOne(d => d.HandledByEmployee, employee =>
            {
                employee.Property(e => e.StaffMemberId).HasColumnName("DepartureHandledByStaffMemberId").IsRequired();
                employee.Property(e => e.FullName).HasColumnName("DepartureHandledByStaffFullName").HasMaxLength(200).IsRequired();
                employee.Property(e => e.EmployeeId).HasColumnName("DepartureHandledByStaffEmployeeId").HasMaxLength(50).IsRequired();
            });
            departure.Navigation(d => d.HandledByEmployee).IsRequired();

            departure.OwnsOne(d => d.CustomerSignature, signature =>
            {
                signature.Property(s => s.FileName).HasColumnName("CustomerSignatureFileName").HasMaxLength(255).IsRequired();
                signature.Property(s => s.StorageKey).HasColumnName("CustomerSignatureStorageKey").HasMaxLength(500).IsRequired();
                signature.Property(s => s.ContentType).HasColumnName("CustomerSignatureContentType").HasMaxLength(100).IsRequired();
                signature.Property(s => s.SizeBytes).HasColumnName("CustomerSignatureSizeBytes").IsRequired();
                signature.Property(s => s.SignedAtUtc).HasColumnName("CustomerSignedAtUtc").IsRequired();
            });
            departure.Navigation(d => d.CustomerSignature).IsRequired(false);
        });
        builder.Navigation(r => r.DepartureDetails).IsRequired(false);

        // Flight remains the cascade path. Composite occurrence ownership prevents cross-flight grouping.
        builder.HasMany(r => r.Services).WithOne()
            .HasForeignKey(s => new { s.ReturnToRampId, s.FlightId })
            .HasPrincipalKey(r => new { r.Id, r.FlightId })
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasMany(r => r.Tasks).WithOne()
            .HasForeignKey(t => new { t.ReturnToRampId, t.FlightId })
            .HasPrincipalKey(r => new { r.Id, r.FlightId })
            .OnDelete(DeleteBehavior.NoAction);

        builder.Ignore(r => r.IsOpen);
        builder.Ignore(r => r.Window);
    }
}
