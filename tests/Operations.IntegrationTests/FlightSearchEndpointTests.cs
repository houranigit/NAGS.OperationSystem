using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MasterData.Contracts.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Operations.Infrastructure.Persistence;
using Operations.Application.Contracts;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Shouldly;

namespace Operations.IntegrationTests;

public sealed partial class FlightVisibilityScopeTests
{
    [Fact]
    public async Task Offline_search_index_pages_never_searched_unassigned_flights_without_granting_assignment_or_history()
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync(); var refs = await SetupMasterDataAsync(admin);
        var staff = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view"]);
        using var client = staff.Client;
        var firstId = await ScheduleFlightAsync(admin, refs.StationId, refs, "8881");
        var secondId = await ScheduleFlightAsync(admin, refs.StationId, refs, "8882");
        var assignedId = await ScheduleFlightAsync(admin, refs.StationId, refs, "8883", [staff.StaffId]);
        await ScheduleFlightAsync(admin, refs.StationId, refs, "8884", perLanding: true);
        var now = DateTimeOffset.UtcNow;
        Flight Seed(string number, Guid station, bool adHoc = false, int hours = 0) => Flight.ScheduleNew(
            new(refs.CustomerId, null, "Index customer"), new(station, "TST", "Index station"),
            new(adHoc ? WellKnownMasterDataIds.AdHocOperationType : refs.OperationTypeId, "Operation"),
            FlightNumber.Create(number).Value, ScheduledTime.Create(now.AddHours(hours), now.AddHours(hours + 1)).Value,
            null, [new(refs.ServiceId, "Service")], [], Guid.NewGuid(), now).Value;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
            db.Flights.AddRange(Seed("8885", refs.StationId, adHoc: true), Seed("8886", Guid.NewGuid()),
                Seed("8887", refs.StationId, hours: 13), Seed("8888", refs.StationId, hours: -14));
            await db.SaveChangesAsync();
        }
        var path = $"{Base}/flights/mobile/search/offline";
        var all = (await client.GetFromJsonAsync<MobileFlightPageDto>($"{path}?pageSize=100&stationId={Guid.NewGuid()}&fromUtc=2000-01-01&toUtc=2100-01-01"))!;
        all.Items.Select(item => item.Id).ShouldBe([firstId, secondId], ignoreOrder: true);
        all.TotalCount.ShouldBe(2); all.StationId.ShouldBe(refs.StationId);
        all.WindowStartUtc.ShouldBe(all.AsOfUtc.AddHours(-12)); all.WindowEndUtc.ShouldBe(all.AsOfUtc.AddHours(12));
        all.Items.ShouldAllBe(item => item.StationId == refs.StationId && !item.IsMyFlight && !item.IsAdHocFlight && !item.IsPerLanding);
        all.Items.ShouldAllBe(item => !string.IsNullOrWhiteSpace(item.StationTimeZoneId) && item.CustomerLogoVersion != null);
        var first = (await client.GetFromJsonAsync<MobileFlightPageDto>($"{path}?pageSize=1"))!;
        var second = (await client.GetFromJsonAsync<MobileFlightPageDto>($"{path}?page=2&pageSize=1"))!;
        first.HasNextPage.ShouldBeTrue(); second.HasPreviousPage.ShouldBeTrue(); second.HasNextPage.ShouldBeFalse();
        first.Items.Concat(second.Items).Select(item => item.Id).ShouldBe(all.Items.Select(item => item.Id));
        var empty = (await client.GetFromJsonAsync<MobileFlightPageDto>($"{path}?page=3&pageSize=1"))!;
        empty.Items.ShouldBeEmpty(); empty.TotalCount.ShouldBe(2); empty.StationId.ShouldBe(refs.StationId);
        var detail = await OperationsBatchDetailAsync(client, firstId);
        detail.AssignedEmployees.ShouldBeEmpty(); detail.IsUnassignedSearchFlight.ShouldBeTrue();
        detail.CanSaveArrival.ShouldBeFalse(); detail.CanSaveServices.ShouldBeFalse(); detail.CanSaveTasks.ShouldBeFalse();
        (await client.GetFromJsonAsync<MobileFlightPageDto>($"{Base}/flights/mobile?category=MyFlights"))!.Items.ShouldHaveSingleItem().Id.ShouldBe(assignedId);
        (await client.GetFromJsonAsync<MobileFlightPageDto>($"{Base}/flights/mobile/history"))!.Items.ShouldBeEmpty();
        (await client.GetFromJsonAsync<MobileFlightPageDto>($"{Base}/flights/mobile/search"))!.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Offline_search_index_requires_view_permission_active_linked_station_and_authenticated_account()
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync(); var refs = await SetupMasterDataAsync(admin);
        var permitted = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view"]);
        var forbidden = await CreateStaffLoginAsync(admin, refs, []);
        using var client = permitted.Client; using var noView = forbidden.Client; using var anonymous = factory.CreateClient();
        var path = $"{Base}/flights/mobile/search/offline";
        await ScheduleFlightAsync(admin, refs.StationId, refs, "8889");
        (await anonymous.GetAsync(path)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await noView.GetAsync(path)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await admin.GetAsync(path)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.GetAsync(path)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var station = await admin.GetFromJsonAsync<JsonElement>($"{MasterDataBase}/stations/{refs.StationId}");
        using var deactivate = new HttpRequestMessage(HttpMethod.Post, $"{MasterDataBase}/stations/{refs.StationId}/deactivate");
        deactivate.Headers.TryAddWithoutValidation("If-Match", station.GetProperty("rowVersion").GetString());
        using var deactivated = await admin.SendAsync(deactivate); deactivated.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.GetAsync(path)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private async Task<FlightDetailDto> RemoveRecordedFlightAssignmentsAsync(HttpClient client, Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        var flight = await db.Flights.Include(item => item.AssignedEmployees).Include(item => item.PlannedServices).SingleAsync(item => item.Id == id);
        flight.ReplaceAssignedEmployees([], DateTimeOffset.UtcNow).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync();
        return await OperationsBatchDetailAsync(client, id);
    }

    [Fact]
    public async Task Per_landing_station_colleagues_fully_correct_and_delete_work_without_assignments_and_history_retains_both_accounts()
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync(); var refs = await SetupMasterDataAsync(admin);
        var author = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view", "operations.flights.operate"]);
        var colleague = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view", "operations.flights.operate"]);
        using var owner = author.Client; using var client = colleague.Client;
        var id = await ScheduleFlightAsync(admin, refs.StationId, refs, "8893", perLanding: true);
        await SeedWorkArrivalAsync(admin, owner, id);
        var serviceId = Guid.NewGuid(); var taskId = Guid.NewGuid();
        var service = ServiceInput(refs.ServiceId, author.StaffId); var task = TaskInput(author.StaffId);
        var initial = await OperationsBatchDetailAsync(owner, id);
        using (var seed = await SendOperationsBatchAsync(owner, id, new(ServiceChanges: [new(serviceId, service)], TaskChanges: [new(taskId, task)]), initial.RowVersion))
            seed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var before = await OperationsBatchDetailAsync(client, id);
        before.AssignedEmployees.ShouldBeEmpty(); before.IsPerLanding.ShouldBeTrue(); before.IsUnassignedSearchFlight.ShouldBeFalse();
        before.CanAddServices.ShouldBe(true); before.CanAddTasks.ShouldBe(true);
        before.Services!.Single().CanEdit.ShouldBeTrue(); before.Services.Single().CanDelete.ShouldBeTrue(); before.Services.Single().CanChangeService.ShouldBeTrue();
        before.Tasks!.Single().CanEdit.ShouldBeTrue(); before.Tasks.Single().CanDelete.ShouldBeTrue();
        using var update = await SendOperationsBatchAsync(client, id, new(ServiceChanges: [new(serviceId, service with { Notes = "Station correction" })],
            TaskChanges: [new(taskId, task with { Notes = "Station task correction" })]), before.RowVersion);
        update.StatusCode.ShouldBe(HttpStatusCode.OK);
        var corrected = (await update.Content.ReadFromJsonAsync<FlightDetailDto>())!;
        corrected.AssignedEmployees.ShouldBeEmpty(); corrected.Services!.Single().Notes.ShouldBe("Station correction");
        using var deleted = await SendOperationsBatchAsync(client, id, new(ServiceChanges: [new(serviceId, null)], TaskChanges: [new(taskId, null)]), corrected.RowVersion);
        deleted.StatusCode.ShouldBe(HttpStatusCode.OK);
        foreach (var actorClient in new[] { owner, client })
            (await actorClient.GetFromJsonAsync<MobileFlightPageDto>($"{Base}/flights/mobile/history"))!.Items.ShouldHaveSingleItem().Id.ShouldBe(id);
        (await OperationsBatchDetailAsync(client, id)).AssignedEmployees.ShouldBeEmpty();
    }

    [Fact]
    public async Task Exact_search_reads_unassigned_regular_flights_without_feed_or_view_history_membership()
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync(); var refs = await SetupMasterDataAsync(admin);
        var staff = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view", "operations.flights.operate"]);
        using var client = staff.Client;
        var id = await ScheduleFlightAsync(admin, refs.StationId, refs, "8890");
        await ScheduleFlightAsync(admin, refs.StationId, refs, "8890", perLanding: true);
        var detail = await OperationsBatchDetailAsync(client, id);
        detail.IsUnassignedSearchFlight.ShouldBeTrue(); detail.CanAddServices.ShouldBe(true); detail.CanAddTasks.ShouldBe(true);
        var exact = await client.GetFromJsonAsync<MobileFlightPageDto>($"{Base}/flights/mobile/search?flightNumber={Uri.EscapeDataString(" " + detail.FlightNumber.ToLowerInvariant() + " ")}");
        exact!.Items.ShouldHaveSingleItem().Id.ShouldBe(id);
        (await client.GetFromJsonAsync<MobileFlightPageDto>($"{Base}/flights/mobile/search?flightNumber=889"))!.Items.ShouldBeEmpty();
        (await client.GetFromJsonAsync<MobileFlightPageDto>($"{Base}/flights/mobile?category=MyFlights"))!.Items.ShouldBeEmpty();
        (await client.GetFromJsonAsync<MobileFlightPageDto>($"{Base}/flights/mobile/history"))!.Items.ShouldBeEmpty();
        (await client.GetFromJsonAsync<MobileFlightPageDto>($"{Base}/flights/mobile/search"))!.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Unassigned_first_combined_save_is_account_history_across_clients_and_later_additions_are_blocked()
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync(); var refs = await SetupMasterDataAsync(admin);
        var staff = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view", "operations.flights.operate"]);
        using var client = staff.Client; var id = await ScheduleFlightAsync(admin, refs.StationId, refs, "8891");
        var aircraft = await OperationsBatchAircraftAsync(admin); var now = DateTimeOffset.UtcNow;
        var first = new SaveFlightOperationsBatchRequest(new(now.AddHours(-1), aircraft, "hz-first", null, null),
            ServiceChanges: [new(Guid.NewGuid(), ServiceInput(refs.ServiceId, staff.StaffId))],
            TaskChanges: [new(Guid.NewGuid(), TaskInput(staff.StaffId))]);
        var original = await OperationsBatchDetailAsync(client, id);
        using var response = await SendOperationsBatchAsync(client, id, first, original.RowVersion);
        response.StatusCode.ShouldBe(HttpStatusCode.OK); var saved = (await response.Content.ReadFromJsonAsync<FlightDetailDto>())!;
        saved.AssignedEmployees.ShouldBeEmpty(); saved.CanAddServices.ShouldBe(false); saved.CanAddTasks.ShouldBe(false);
        using var secondDevice = factory.CreateClient(); secondDevice.DefaultRequestHeaders.Authorization = client.DefaultRequestHeaders.Authorization;
        var history = (await secondDevice.GetFromJsonAsync<MobileFlightPageDto>($"{Base}/flights/mobile/history"))!;
        history.Items.ShouldHaveSingleItem().Id.ShouldBe(id); history.Items.Single().LastInteractionAtUtc.ShouldNotBeNull();
        (await secondDevice.GetFromJsonAsync<MobileFlightPageDto>($"{Base}/flights/mobile/search"))!.Items.ShouldHaveSingleItem().Id.ShouldBe(id);
        using var denied = await SendServiceAsync(client, id, Guid.NewGuid(), ServiceInput(refs.ServiceId, staff.StaffId), saved.RowVersion);
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var beforeReplay = history.Items.Single().LastInteractionAtUtc;
        using var replay = await SendOperationsBatchAsync(client, id, first, original.RowVersion);
        replay.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetFromJsonAsync<MobileFlightPageDto>($"{Base}/flights/mobile/history"))!.Items.Single().LastInteractionAtUtc.ShouldBe(beforeReplay);
    }

    [Fact]
    public async Task Search_foreign_work_closes_outside_service_allowance_without_core_edits_assignments_or_new_work()
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync(); var refs = await SetupMasterDataAsync(admin);
        var author = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view", "operations.flights.operate"]);
        var searcher = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view", "operations.flights.operate"]);
        using var owner = author.Client; using var client = searcher.Client;
        var id = await ScheduleFlightAsync(admin, refs.StationId, refs, "8892", [author.StaffId]);
        await SeedWorkArrivalAsync(admin, owner, id);
        var serviceId = Guid.NewGuid(); var taskId = Guid.NewGuid();
        var service = ServiceInput(refs.ServiceId, author.StaffId) with { Attachments = [new(Guid.NewGuid(), (int)AttachmentKind.Document,
            "retained.pdf", "application/pdf", Encoding.ASCII.GetBytes("%PDF-retained"))] };
        var task = TaskInput(author.StaffId);
        var initial = await OperationsBatchDetailAsync(owner, id);
        using (var seed = await SendOperationsBatchAsync(owner, id, new(ServiceChanges: [new(serviceId, service)], TaskChanges: [new(taskId, task)]), initial.RowVersion))
            seed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var manpower = (await admin.GetFromJsonAsync<ConcurrencyDetail>($"{MasterDataBase}/manpower-types/{refs.ManpowerTypeId}"))!;
        using (var allowanceRequest = new HttpRequestMessage(HttpMethod.Put, $"{MasterDataBase}/manpower-types/{refs.ManpowerTypeId}/service-allowances") { Content = JsonContent.Create(new { serviceIds = Array.Empty<Guid>() }) })
        {
            allowanceRequest.Headers.TryAddWithoutValidation("If-Match", manpower.RowVersion);
            using var changed = await admin.SendAsync(allowanceRequest); changed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }
        var before = await OperationsBatchSnapshotAsync(client, id); var detail = before.Detail;
        detail.Services!.Single().CanCloseOnly.ShouldBeTrue(); detail.Services.Single().CanEdit.ShouldBeFalse();
        detail.Tasks!.Single().CanCloseOnly.ShouldBeTrue(); detail.CanAddServices.ShouldBe(false); detail.CanSaveArrival.ShouldBeFalse();
        var end = DateTimeOffset.UtcNow; var closed = service with { EndUtc = end, Attachments = null,
            Performers = service.Performers.Select(item => item with { EndUtc = end }).ToArray() };
        using (var tampered = await SendServiceAsync(client, id, serviceId, closed with { Notes = "Foreign notes" }, detail.RowVersion))
            tampered.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        await AssertOperationsBatchUnchangedAsync(client, id, before);
        var closedTask = task with { EndUtc = end, Performers = task.Performers.Select(item => item with { EndUtc = end }).ToArray() };
        using var response = await SendOperationsBatchAsync(client, id, new(Departure: new(end.AddMinutes(1), null, null, null),
            ServiceChanges: [new(serviceId, closed)], TaskChanges: [new(taskId, closedTask)]), detail.RowVersion);
        response.StatusCode.ShouldBe(HttpStatusCode.OK); var saved = (await response.Content.ReadFromJsonAsync<FlightDetailDto>())!;
        saved.Services!.Single().Attachments.ShouldHaveSingleItem(); saved.Services.Single().CanSetEnd.ShouldBeFalse();
        saved.Tasks!.Single().CanSetEnd.ShouldBeFalse(); saved.AssignedEmployees.Select(item => item.StaffMemberId).ShouldBe([author.StaffId]);
        using (var deniedDelete = await DeleteServiceAsync(client, id, serviceId, saved.RowVersion)) deniedDelete.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.GetFromJsonAsync<MobileFlightPageDto>($"{Base}/flights/mobile/history"))!.Items.ShouldHaveSingleItem().Id.ShouldBe(id);
    }
}
