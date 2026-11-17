using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Operations.Domain.Flights;

namespace Operations.Infrastructure.Persistence.Configurations;

public sealed class FlightShareConfiguration : IEntityTypeConfiguration<FlightShare>
{
    public void Configure(EntityTypeBuilder<FlightShare> builder)
    {
        builder.ToTable("flight_shares");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.AuthorFullName).HasMaxLength(200).IsRequired();
        builder.Property(item => item.AuthorEmployeeId).HasMaxLength(50).IsRequired();
        builder.Property(item => item.Message).HasMaxLength(2000);
        builder.HasOne<Flight>().WithMany().HasForeignKey(item => item.FlightId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(item => item.Recipients).WithOne().HasForeignKey(item => item.ShareId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(item => item.Recipients).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(item => new { item.StationId, item.CreatedAtUtc, item.Id });
        builder.HasIndex(item => new { item.AuthorUserId, item.StationId, item.CreatedAtUtc, item.Id });
    }
}

public sealed class FlightShareRecipientConfiguration : IEntityTypeConfiguration<FlightShareRecipient>
{
    public void Configure(EntityTypeBuilder<FlightShareRecipient> builder)
    {
        builder.ToTable("flight_share_recipients");
        builder.HasKey(item => new { item.ShareId, item.StaffMemberId });
        builder.Property(item => item.FullName).HasMaxLength(200).IsRequired();
        builder.Property(item => item.EmployeeId).HasMaxLength(50).IsRequired();
        builder.HasIndex(item => new { item.StaffMemberId, item.ShareId });
    }
}
