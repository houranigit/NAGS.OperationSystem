using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Operations.Domain.Flights;

namespace Operations.Infrastructure.Persistence.Configurations;

public sealed class FlightUserActivityConfiguration : IEntityTypeConfiguration<FlightUserActivity>
{
    public void Configure(EntityTypeBuilder<FlightUserActivity> builder)
    {
        builder.ToTable("flight_user_activities");
        builder.HasKey(activity => new { activity.UserId, activity.FlightId });
        builder.HasOne<Flight>().WithMany().HasForeignKey(activity => activity.FlightId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(activity => new { activity.UserId, activity.LastInteractionAtUtc, activity.FlightId });
    }
}
