using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Operations.Domain.Entities;
using Operations.Domain.ValueObjects;
using WorkOrderId = Operations.Domain.Aggregates.WorkOrder.WorkOrderId;

namespace Operations.Infrastructure.Persistence.Configurations;

public sealed class WorkOrderServiceLineConfiguration : IEntityTypeConfiguration<WorkOrderServiceLine>
{
    public void Configure(EntityTypeBuilder<WorkOrderServiceLine> builder)
    {
        builder.ToTable("WorkOrderServiceLines", "operations");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.WorkOrderId)
            .HasConversion(v => v.Value, v => WorkOrderId.From(v))
            .IsRequired();

        builder.OwnsOne(x => x.Service, s =>
        {
            s.Property(v => v.ServiceId).HasColumnName("ServiceId").IsRequired();
            s.Property(v => v.Name).HasColumnName("ServiceName").HasMaxLength(150).IsRequired();
        });

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

        builder.Property(x => x.From).IsRequired();
        builder.Property(x => x.To).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(2000);
        builder.Property(x => x.ReturnToRamp).IsRequired();

        builder.HasIndex(x => x.WorkOrderId);
        builder.HasIndex(x => new { x.WorkOrderId, x.From });
    }
}
