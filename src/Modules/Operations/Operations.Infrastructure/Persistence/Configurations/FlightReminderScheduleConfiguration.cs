using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Operations.Infrastructure.BackgroundJobs;

namespace Operations.Infrastructure.Persistence.Configurations;

public sealed class FlightReminderScheduleConfiguration : IEntityTypeConfiguration<FlightReminderSchedule>
{
    public void Configure(EntityTypeBuilder<FlightReminderSchedule> builder)
    {
        builder.ToTable("flight_reminder_schedules");
        builder.HasKey(reminder => reminder.Id);
        builder.Property(reminder => reminder.Id).ValueGeneratedNever();
        builder.Property(reminder => reminder.State).HasConversion<int>();
        builder.Property(reminder => reminder.SkipReason).HasMaxLength(300);

        builder.HasIndex(reminder => new
        {
            reminder.FlightId,
            reminder.StaffMemberId,
            reminder.LeadTimeMinutes,
            reminder.ScheduledArrivalUtc
        }).IsUnique();
        builder.HasIndex(reminder => new { reminder.State, reminder.DueAtUtc });
        builder.HasIndex(reminder => new { reminder.State, reminder.DispatchedAtUtc });
        builder.HasIndex(reminder => new { reminder.State, reminder.SkippedAtUtc });
    }
}
