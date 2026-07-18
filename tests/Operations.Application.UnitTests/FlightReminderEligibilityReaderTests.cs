using Microsoft.EntityFrameworkCore;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Operations.Infrastructure.Persistence;
using Operations.Infrastructure.Readers;
using Shouldly;

namespace Operations.Application.UnitTests;

public sealed class FlightReminderEligibilityReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Reader_requires_current_sta_active_status_and_current_assignment()
    {
        await using var db = NewDb();
        var eligible = CreateFlight(Now.AddHours(2));
        var canceled = CreateFlight(Now.AddHours(2));
        var unassigned = CreateFlight(Now.AddHours(2));
        var rescheduled = CreateFlight(Now.AddHours(2));
        canceled.SettleCanceled(Now).IsSuccess.ShouldBeTrue();
        unassigned.ReplaceAssignedEmployees([], Now).IsSuccess.ShouldBeTrue();
        rescheduled.UpdateSchedule(
            ScheduledTime.Create(Now.AddHours(3), Now.AddHours(5)).Value,
            aircraftType: null,
            Now).IsSuccess.ShouldBeTrue();
        db.Flights.AddRange(eligible, canceled, unassigned, rescheduled);
        await db.SaveChangesAsync();
        var reader = new FlightReminderEligibilityReader(db);

        (await IsEligibleAsync(reader, eligible, Now, eligible.Schedule.Sta)).ShouldBeTrue();
        (await IsEligibleAsync(reader, canceled, Now, canceled.Schedule.Sta)).ShouldBeFalse();
        (await IsEligibleAsync(reader, unassigned, Now, unassigned.Schedule.Sta)).ShouldBeFalse();
        (await IsEligibleAsync(reader, rescheduled, Now, Now.AddHours(2))).ShouldBeFalse();
        (await IsEligibleAsync(reader, eligible, eligible.Schedule.Sta, eligible.Schedule.Sta)).ShouldBeFalse();
    }

    private static Task<bool> IsEligibleAsync(
        FlightReminderEligibilityReader reader,
        Flight flight,
        DateTimeOffset evaluatedAtUtc,
        DateTimeOffset reminderSta) =>
        reader.IsEligibleAsync(
            flight.Id,
            flight.AssignedEmployees.FirstOrDefault()?.Employee.StaffMemberId ?? Guid.NewGuid(),
            reminderSta,
            evaluatedAtUtc);

    private static OperationsDbContext NewDb() =>
        new(new DbContextOptionsBuilder<OperationsDbContext>()
            .UseInMemoryDatabase($"flight-reminder-eligibility-{Guid.NewGuid()}")
            .Options);

    private static Flight CreateFlight(DateTimeOffset sta) =>
        Flight.ScheduleNew(
            new CustomerSnapshot(Guid.NewGuid(), "SV", "Saudia"),
            new StationSnapshot(Guid.NewGuid(), "RUH", "Riyadh"),
            new OperationTypeSnapshot(Guid.NewGuid(), "Transit"),
            FlightNumber.Create("SV720").Value,
            ScheduledTime.Create(sta, sta.AddHours(2)).Value,
            aircraftType: null,
            plannedServices: [new ServiceSnapshot(Guid.NewGuid(), "Marshalling")],
            assignedEmployees: [new StaffMemberSnapshot(Guid.NewGuid(), "Reminder Recipient", "EMP-REM")],
            contractId: null,
            contractNumber: null,
            createdByUserId: Guid.NewGuid(),
            now: Now).Value;
}
