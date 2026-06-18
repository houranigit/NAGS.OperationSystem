using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Operations.Domain.Entities;
using FlightId = Operations.Domain.Aggregates.Flight.FlightId;

namespace Operations.Infrastructure.Persistence.Configurations;

public sealed class FlightAssignedEmployeeConfiguration : IEntityTypeConfiguration<FlightAssignedEmployee>
{
    public void Configure(EntityTypeBuilder<FlightAssignedEmployee> builder)
    {
        builder.ToTable("FlightAssignments", "operations");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.FlightId)
            .HasConversion(v => v.Value, v => FlightId.From(v))
            .IsRequired();

        builder.OwnsOne(x => x.Employee, e =>
        {
            e.Property(v => v.EmployeeId).HasColumnName("EmployeeId").IsRequired();
            e.Property(v => v.FullName).HasColumnName("EmployeeFullName").HasMaxLength(200).IsRequired();
            e.OwnsOne(v => v.StationSnapshot, st =>
            {
                st.Property(z => z.StationId).HasColumnName("EmployeeStationId").IsRequired();
                st.Property(z => z.Name).HasColumnName("EmployeeStationName").HasMaxLength(150).IsRequired();
                st.Property(z => z.IataCode).HasColumnName("EmployeeStationIataCode").HasMaxLength(3).IsRequired();
            });
            e.OwnsOne(v => v.ManpowerTypeSnapshot, m =>
            {
                m.Property(z => z.ManpowerTypeId).HasColumnName("EmployeeManpowerTypeId").IsRequired();
                m.Property(z => z.Name).HasColumnName("EmployeeManpowerTypeName").HasMaxLength(150).IsRequired();
            });
        });

        builder.HasIndex(x => x.FlightId);
    }
}
