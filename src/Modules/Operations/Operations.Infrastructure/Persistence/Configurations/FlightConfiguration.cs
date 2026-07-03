using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Operations.Domain.Flights;

namespace Operations.Infrastructure.Persistence.Configurations;

public sealed class FlightConfiguration : IEntityTypeConfiguration<Flight>
{
    public void Configure(EntityTypeBuilder<Flight> builder)
    {
        builder.ToTable("flights");
        builder.HasKey(f => f.Id);

        builder.Property(f => f.OriginalFlightNumber).HasMaxLength(12).IsRequired();
        builder.Property(f => f.Status).HasConversion<int>();
        builder.Property(f => f.ContractNumber).HasMaxLength(50);
        builder.Property(f => f.CreatedAtUtc).IsRequired();
        builder.Property(f => f.RowVersion).IsRowVersion();

        builder.OwnsOne(f => f.FlightNumber, n =>
            n.Property(p => p.Value).HasColumnName("FlightNumber").HasMaxLength(12).IsRequired());

        builder.OwnsOne(f => f.Schedule, s =>
        {
            s.Property(p => p.Sta).HasColumnName("ScheduledArrivalUtc").IsRequired();
            s.Property(p => p.Std).HasColumnName("ScheduledDepartureUtc").IsRequired();
        });

        builder.OwnsOne(f => f.Customer, c =>
        {
            c.Property(p => p.CustomerId).HasColumnName("CustomerId").IsRequired();
            c.Property(p => p.IataCode).HasColumnName("CustomerIataCode").HasMaxLength(3);
            c.Property(p => p.Name).HasColumnName("CustomerName").HasMaxLength(200).IsRequired();
        });

        builder.OwnsOne(f => f.Station, s =>
        {
            s.Property(p => p.StationId).HasColumnName("StationId").IsRequired();
            s.Property(p => p.IataCode).HasColumnName("StationIataCode").HasMaxLength(3).IsRequired();
            s.Property(p => p.Name).HasColumnName("StationName").HasMaxLength(150).IsRequired();
        });

        builder.OwnsOne(f => f.OperationType, o =>
        {
            o.Property(p => p.OperationTypeId).HasColumnName("OperationTypeId").IsRequired();
            o.Property(p => p.Name).HasColumnName("OperationTypeName").HasMaxLength(100).IsRequired();
        });

        builder.OwnsOne(f => f.AircraftType, a =>
        {
            a.Property(p => p.AircraftTypeId).HasColumnName("AircraftTypeId");
            a.Property(p => p.Manufacturer).HasColumnName("AircraftManufacturer").HasMaxLength(100);
            a.Property(p => p.Model).HasColumnName("AircraftModel").HasMaxLength(50);
        });

        builder.HasMany(f => f.PlannedServices).WithOne().HasForeignKey(p => p.FlightId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(f => f.AssignedEmployees).WithOne().HasForeignKey(e => e.FlightId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(f => f.Status);
        builder.HasIndex(f => f.OriginalFlightNumber);

        builder.Ignore(f => f.IsPerLanding);
        builder.Ignore(f => f.IsUpdateLocked);
        builder.Ignore(f => f.DomainEvents);
    }
}

public sealed class PlannedServiceConfiguration : IEntityTypeConfiguration<PlannedService>
{
    public void Configure(EntityTypeBuilder<PlannedService> builder)
    {
        builder.ToTable("flight_planned_services");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.FlightId).IsRequired();

        builder.OwnsOne(p => p.Service, s =>
        {
            s.Property(x => x.ServiceId).HasColumnName("ServiceId").IsRequired();
            s.Property(x => x.Name).HasColumnName("ServiceName").HasMaxLength(200).IsRequired();
        });

        builder.HasIndex(p => p.FlightId);
        builder.Ignore(p => p.IsAircraftPerLanding);
    }
}

public sealed class FlightAssignedEmployeeConfiguration : IEntityTypeConfiguration<FlightAssignedEmployee>
{
    public void Configure(EntityTypeBuilder<FlightAssignedEmployee> builder)
    {
        builder.ToTable("flight_assigned_employees");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.FlightId).IsRequired();

        builder.OwnsOne(e => e.Employee, s =>
        {
            s.Property(x => x.StaffMemberId).HasColumnName("StaffMemberId").IsRequired();
            s.Property(x => x.FullName).HasColumnName("StaffFullName").HasMaxLength(200).IsRequired();
            s.Property(x => x.EmployeeId).HasColumnName("StaffEmployeeId").HasMaxLength(50).IsRequired();
        });

        builder.HasIndex(e => e.FlightId);
    }
}
