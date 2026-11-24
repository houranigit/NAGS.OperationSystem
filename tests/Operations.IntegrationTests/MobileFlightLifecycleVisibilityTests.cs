using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Operations.Application.Contracts;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Operations.Infrastructure.Persistence;
using Shouldly;

namespace Operations.IntegrationTests;

public sealed partial class FlightVisibilityScopeTests
{
    [Fact]
    public async Task Lifecycle_visibility_is_consistent_in_sql_lists_search_history_details_and_notification_targets()
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync(); var refs = await SetupMasterDataAsync(admin);
        var staff = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view"]);
        using var client = staff.Client;
        var me = await client.GetFromJsonAsync<JsonElement>($"{IdentityBase}/me"); var userId = me.GetProperty("id").GetGuid();
        var now = DateTimeOffset.UtcNow;
        var employee = new StaffMemberSnapshot(staff.StaffId, "Employee", "E1");
        var scenarios = new (string Kind, bool Visible)[]
        {
            ("overdue", true), ("lookahead", true), ("too-early", false),
            ("ground", true), ("early-arrived", true), ("ground-canceled", true),
            ("canceled-recent", true), ("canceled-old", false),
            ("departed-recent", true), ("departed-old", false),
            ("rtr-open", true), ("rtr-closed-recent", true), ("rtr-closed-old", false)
        };
        var seeded = new List<(Flight Flight, string Kind, bool Visible, bool Assigned)>();
        foreach (var (kind, visible) in scenarios)
        foreach (var assigned in new[] { true, false })
        {
            var number = $"7{Array.FindIndex(scenarios, item => item.Kind == kind):000}";
            var sta = kind switch { "lookahead" => now.AddHours(11), "too-early" => now.AddHours(13), "early-arrived" => now.AddHours(30), _ => now.AddDays(-30) };
            var flight = Flight.ScheduleNew(new(refs.CustomerId, null, "Lifecycle customer"), new(refs.StationId, "TST", "Station"),
                new(refs.OperationTypeId, "Transit"), FlightNumber.Create(number).Value, ScheduledTime.Create(sta, sta.AddHours(1)).Value,
                null, [new(refs.ServiceId, "Service")], assigned ? [new(employee.StaffMemberId, employee.FullName, employee.EmployeeId)] : [], userId, now).Value;
            if (kind is "ground" or "early-arrived" or "ground-canceled" || kind.StartsWith("departed") || kind.StartsWith("rtr"))
                flight.SubmitArrival(employee, ArrivalDetails.Create(null, now.AddDays(-5), new(Guid.NewGuid(), "Airbus", "A320"), "HZ-TEST", employee, null).Value,
                    userId, now).IsSuccess.ShouldBeTrue();
            if (kind.StartsWith("canceled") || kind == "ground-canceled")
            {
                var cancellationEmployee = new StaffMemberSnapshot(Guid.NewGuid(), "Cancellation employee", "E2");
                flight.SubmitCancellation(cancellationEmployee, CancellationDetails.Create(now.AddHours(kind == "canceled-recent" ? -11 : -13), null).Value,
                    Guid.NewGuid(), now).IsSuccess.ShouldBeTrue();
            }
            if (kind.StartsWith("departed") || kind.StartsWith("rtr"))
                flight.SubmitDeparture(employee, DepartureDetails.Create(now.AddHours(kind == "departed-recent" ? -11 : kind == "departed-old" ? -13 : -40), null, employee, null).Value,
                    userId, now).IsSuccess.ShouldBeTrue();
            if (kind.StartsWith("rtr"))
            {
                flight.Approve(userId, "Reviewer", now, seeded.Count + 1).IsSuccess.ShouldBeTrue();
                var occurrence = flight.StartReturnToRamp(RtrArrivalDetails.Create(now.AddHours(-30), employee, null).Value, userId, now).Value;
                if (kind != "rtr-open")
                {
                    var window = TimeWindow.Create(now.AddHours(-29), now.AddHours(-28)).Value;
                    flight.AddService(new(refs.ServiceId, "RTR service"), null, window, null, userId, now, occurrence.Id,
                        [new(employee, window)]).IsSuccess.ShouldBeTrue();
                    flight.CloseReturnToRamp(occurrence.Id, RtrDepartureDetails.Create(now.AddHours(kind == "rtr-closed-recent" ? -11 : -13), null, employee, null).Value,
                        userId, now).IsSuccess.ShouldBeTrue();
                }
            }
            seeded.Add((flight, kind, visible, assigned));
        }
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
            db.Flights.AddRange(seeded.Select(item => item.Flight));
            db.FlightUserActivities.AddRange(seeded.Select(item => FlightUserActivity.Create(userId, item.Flight.Id, now, usedSearch: true)));
            await db.SaveChangesAsync();
        }
        var mobile = $"{Base}/flights/mobile";
        var expected = seeded.Where(item => item.Visible).Select(item => item.Flight.Id).ToArray();
        var normal = (await client.GetFromJsonAsync<MobileFlightPageDto>($"{mobile}?pageSize=100"))!;
        normal.Items.Select(item => item.Id).ShouldBe(seeded.Where(item => item.Visible && item.Assigned).Select(item => item.Flight.Id), ignoreOrder: true);
        var offline = (await client.GetFromJsonAsync<MobileFlightPageDto>($"{mobile}/search/offline?pageSize=100"))!;
        offline.Items.Select(item => item.Id).ShouldBe(seeded.Where(item => item.Visible && !item.Assigned).Select(item => item.Flight.Id), ignoreOrder: true);
        foreach (var suffix in new[] { "history", "search" })
        {
            var history = (await client.GetFromJsonAsync<MobileFlightPageDto>($"{mobile}/{suffix}?pageSize=100"))!;
            history.Items.Select(item => item.Id).ShouldBe(expected, ignoreOrder: true);
        }
        foreach (var (flight, _, visible, _) in seeded)
            (await client.GetAsync($"{mobile}/{flight.Id}")).StatusCode.ShouldBe(visible ? HttpStatusCode.OK : HttpStatusCode.NotFound);
        foreach (var (kind, visible) in scenarios)
        {
            var matches = seeded.Where(item => item.Kind == kind).ToArray();
            var search = (await client.GetFromJsonAsync<MobileFlightPageDto>($"{mobile}/search?flightNumber={matches[0].Flight.FlightNumber.Value}"))!;
            search.Items.Select(item => item.Id).ShouldBe(visible ? matches.Select(item => item.Flight.Id) : [], ignoreOrder: true);
        }
        using var targets = await client.PostAsJsonAsync($"{mobile}/notification-targets", new { flightIds = seeded.Select(item => item.Flight.Id).ToArray() });
        targets.StatusCode.ShouldBe(HttpStatusCode.OK); (await targets.Content.ReadFromJsonAsync<Guid[]>()).ShouldBe(expected, ignoreOrder: true);
        foreach (var card in normal.Items.Concat(offline.Items))
        {
            var source = seeded.Single(item => item.Flight.Id == card.Id).Flight;
            card.VisibilityPolicyVersion.ShouldBe(1);
            card.LatestDepartureAtUtc.ShouldBe(source.DepartureSubmissions.Select(item => (DateTimeOffset?)item.Details.Atd).Max());
            card.LatestReturnToRampDepartureAtUtc.ShouldBe(source.ReturnToRamps.Select(item => item.DepartureDetails?.DepartedAtUtc).Max());
        }
    }
}
