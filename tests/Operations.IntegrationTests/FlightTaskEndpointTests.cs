using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MasterData.Contracts.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Operations.Application.Contracts;
using Operations.Infrastructure.Persistence;
using Shouldly;

namespace Operations.IntegrationTests;

public sealed partial class FlightVisibilityScopeTests
{
    [Fact]
    public async Task Task_boundary_requires_operate_permission_current_version_station_staff_and_valid_input()
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var actor = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view", "operations.flights.operate"]);
        var reader = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view"]);
        using var client = actor.Client; using var readClient = reader.Client; using var anonymous = factory.CreateClient();
        var id = await ScheduleFlightAsync(admin, refs.StationId, refs, "8400", [actor.StaffId, reader.StaffId]);
        await SeedWorkArrivalAsync(admin, client, id);
        var recordId = Guid.NewGuid(); var input = TaskInput(actor.StaffId);
        var detail = (await client.GetFromJsonAsync<FlightDetailDto>($"{Base}/flights/mobile/{id}"))!;
        detail.CanSaveTasks.ShouldBeTrue(); detail.Tasks!.ShouldBeEmpty();
        using (var missing = await client.PutAsJsonAsync($"{Base}/flights/{id}/tasks/{recordId}", input)) missing.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using (var denied = await SendTaskAsync(readClient, id, recordId, input, detail.RowVersion)) denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using (var unauthenticated = await SendTaskAsync(anonymous, id, recordId, input, detail.RowVersion)) unauthenticated.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using (var missingDelete = await client.DeleteAsync($"{Base}/flights/{id}/tasks/{recordId}")) missingDelete.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var person = input.Performers.Single();
        foreach (var invalid in new[] { input with { TaskType = 5 }, input with { Performers = [] },
            input with { Performers = [person, person with { Id = Guid.NewGuid() }] }, input with { EndUtc = input.StartUtc.AddMinutes(-1) },
            input with { Materials = [new(Guid.NewGuid(), WellKnownMasterDataIds.UnknownMaterial, 0, Notes: "Invalid quantity")] } })
        {
            using var response = await SendTaskAsync(client, id, recordId, invalid, detail.RowVersion);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }
        var elsewhere = await SetupMasterDataAsync(admin);
        var foreign = await CreateStaffLoginAsync(admin, elsewhere, ["operations.flights.view"]);
        using var foreignClient = foreign.Client;
        using (var wrongStation = await SendTaskAsync(client, id, recordId, input with { Performers = [person with { StaffMemberId = foreign.StaffId }] }, detail.RowVersion))
            wrongStation.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var hidden = await ScheduleFlightAsync(admin, await CreateStationAsync(admin, refs.CountryId), refs, "8401");
        using (var deniedScope = await SendTaskAsync(client, hidden, recordId, input, detail.RowVersion)) deniedScope.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var after = (await client.GetFromJsonAsync<FlightDetailDto>($"{Base}/flights/mobile/{id}"))!;
        after.Tasks!.ShouldBeEmpty(); after.RowVersion.ShouldBe(detail.RowVersion);
        (await readClient.GetFromJsonAsync<FlightDetailDto>($"{Base}/flights/mobile/{id}"))!.CanSaveTasks.ShouldBeFalse();
    }

    [Fact]
    public async Task Task_save_roundtrips_sql_children_typed_quantities_media_and_exact_retries_without_duplicates()
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync(); var refs = await SetupMasterDataAsync(admin);
        var actor = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view", "operations.flights.operate"]);
        using var client = actor.Client;
        var id = await ScheduleFlightAsync(admin, refs.StationId, refs, "8410", [actor.StaffId]); var recordId = Guid.NewGuid();
        await SeedWorkArrivalAsync(admin, client, id);
        var image = new SaveFlightServiceAttachmentRequest(Guid.NewGuid(), 0, "folder/photo.png", "image/png", ServicePng());
        var input = TaskInput(actor.StaffId) with { Notes = " Task notes ", CustomerReferenceNumber = " REF ", Attachments = [image],
            Tools = [new(Guid.NewGuid(), WellKnownMasterDataIds.UnknownTool, StartUtc: DateTimeOffset.UtcNow.AddMinutes(-10), Notes: "Torque wrench")],
            Materials = [new(Guid.NewGuid(), WellKnownMasterDataIds.UnknownMaterial, 9999999999999999.99m, Notes: "Material")],
            GeneralSupports = [new(Guid.NewGuid(), WellKnownMasterDataIds.UnknownGeneralSupport, 1.25m, Notes: "Support")] };
        var detail = (await client.GetFromJsonAsync<FlightDetailDto>($"{Base}/flights/mobile/{id}"))!;
        using var create = await SendTaskAsync(client, id, recordId, input, detail.RowVersion); create.StatusCode.ShouldBe(HttpStatusCode.OK);
        var raw = await create.Content.ReadAsStringAsync();
        var saved = JsonSerializer.Deserialize<FlightTaskSavedDto>(raw, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        saved.Task.Id.ShouldBe(recordId); saved.Task.Materials.Single().Quantity.ShouldBe(9999999999999999.99m);
        saved.Task.Notes.ShouldBe("Task notes"); saved.Task.CustomerReferenceNumber.ShouldBe("REF");
        saved.Task.Attachments.Single().Id.ShouldBe(image.Id); saved.ActiveWorkCount.ShouldBe(1);
        saved.RowVersion.ShouldNotBe(detail.RowVersion);
        var json = JsonDocument.Parse(raw).RootElement;
        json.GetProperty("task").GetProperty("materials")[0].GetProperty("quantity").GetString().ShouldBe("9999999999999999.99");
        using var retry = await SendTaskAsync(client, id, recordId, input, detail.RowVersion); retry.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await retry.Content.ReadFromJsonAsync<FlightTaskSavedDto>())!.RowVersion.ShouldBe(saved.RowVersion);
        using var stale = await SendTaskAsync(client, id, recordId, input with { Notes = "Changed stale" }, detail.RowVersion); stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var correction = input with { Notes = "Correction", Attachments = null, Materials = [input.Materials.Single() with { Quantity = 2.5m }] };
        using var update = await SendTaskAsync(client, id, recordId, correction, saved.RowVersion); update.StatusCode.ShouldBe(HttpStatusCode.OK);
        var corrected = (await update.Content.ReadFromJsonAsync<FlightTaskSavedDto>())!;
        corrected.Task.Attachments.Single().Id.ShouldBe(image.Id); corrected.Task.Materials.Single().Id.ShouldBe(input.Materials.Single().Id);
        corrected.Task.Materials.Single().Quantity.ShouldBe(2.5m);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        var flight = await db.Flights.AsNoTracking().Include(f => f.TaskRecords).ThenInclude(t => t.Performers)
            .Include(f => f.TaskRecords).ThenInclude(t => t.Materials).Include(f => f.TaskRecords).ThenInclude(t => t.Attachments).SingleAsync(f => f.Id == id);
        var task = flight.Tasks.ShouldHaveSingleItem(); task.Id.ShouldBe(recordId); task.Performers.Single().Id.ShouldBe(input.Performers.Single().Id);
        task.Materials.Single().Usage.Quantity.ShouldBe(2.5m); task.Attachments.Single().Id.ShouldBe(image.Id);
        var read = (await client.GetFromJsonAsync<FlightDetailDto>($"{Base}/flights/mobile/{id}"))!;
        read.Tasks!.ShouldHaveSingleItem().Materials.Single().Quantity.ShouldBe(2.5m); read.CanSaveCancellation.ShouldBeFalse();
    }

    [Fact]
    public async Task Task_attachment_preview_is_private_scoped_versioned_and_delete_is_retryable()
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync(); var refs = await SetupMasterDataAsync(admin);
        var actor = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view", "operations.flights.operate"]);
        var reader = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view"]);
        using var client = actor.Client; using var readClient = reader.Client;
        var id = await ScheduleFlightAsync(admin, refs.StationId, refs, "8420", [actor.StaffId, reader.StaffId]); var recordId = Guid.NewGuid();
        await SeedWorkArrivalAsync(admin, client, id);
        var image = new SaveFlightServiceAttachmentRequest(Guid.NewGuid(), 0, "photo.png", "image/png", ServicePng());
        var detail = (await client.GetFromJsonAsync<FlightDetailDto>($"{Base}/flights/mobile/{id}"))!;
        using var create = await SendTaskAsync(client, id, recordId, TaskInput(actor.StaffId) with { Attachments = [image] }, detail.RowVersion);
        create.StatusCode.ShouldBe(HttpStatusCode.OK); var saved = (await create.Content.ReadFromJsonAsync<FlightTaskSavedDto>())!;
        var url = $"{Base}/flights/mobile/{id}/tasks/{recordId}/attachments/{image.Id}?expectedUploadedAt={Uri.EscapeDataString(saved.Task.Attachments.Single().UploadedAtUtc.ToString("O"))}";
        using var preview = await readClient.GetAsync(url); preview.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await preview.Content.ReadAsByteArrayAsync()).ShouldBe(ServicePng()); preview.Headers.CacheControl!.Private.ShouldBeTrue(); preview.Headers.CacheControl.NoStore.ShouldBeTrue();
        var staleUrl = $"{Base}/flights/mobile/{id}/tasks/{recordId}/attachments/{image.Id}?expectedUploadedAt={Uri.EscapeDataString(saved.Task.Attachments.Single().UploadedAtUtc.AddSeconds(1).ToString("O"))}";
        using (var stale = await client.GetAsync(staleUrl)) stale.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        using (var denied = await DeleteTaskAsync(readClient, id, recordId, saved.RowVersion)) denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using (var deleted = await DeleteTaskAsync(client, id, recordId, saved.RowVersion)) deleted.StatusCode.ShouldBe(HttpStatusCode.OK);
        using (var retry = await DeleteTaskAsync(client, id, recordId, saved.RowVersion)) retry.StatusCode.ShouldBe(HttpStatusCode.OK);
        using (var gone = await readClient.GetAsync(url)) gone.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.GetFromJsonAsync<FlightDetailDto>($"{Base}/flights/mobile/{id}"))!.Tasks!.ShouldBeEmpty();
    }

    [Fact]
    public async Task Task_end_closing_respects_open_staff_creator_closer_and_selection_ownership()
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync(); var refs = await SetupMasterDataAsync(admin);
        var owner = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view", "operations.flights.operate"]);
        var closer = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view", "operations.flights.operate"]);
        using var client = owner.Client; using var other = closer.Client;
        var id = await ScheduleFlightAsync(admin, refs.StationId, refs, "8430", [owner.StaffId]); var recordId = Guid.NewGuid();
        await SeedWorkArrivalAsync(admin, client, id);
        var input = TaskInput(owner.StaffId); var detail = (await client.GetFromJsonAsync<FlightDetailDto>($"{Base}/flights/mobile/{id}"))!;
        using (var invalid = await SendTaskAsync(client, id, recordId, input with { EndUtc = DateTimeOffset.UtcNow }, detail.RowVersion))
        {
            invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            var problem = await invalid.Content.ReadFromJsonAsync<JsonElement>(); problem.GetProperty("code").GetString().ShouldBe("Operations.FlightTask.OpenPerformerPeriods");
        }
        input = input with { Performers = [input.Performers.Single() with { EndUtc = DateTimeOffset.UtcNow.AddMinutes(-2) }] };
        using var create = await SendTaskAsync(client, id, recordId, input, detail.RowVersion); create.StatusCode.ShouldBe(HttpStatusCode.OK);
        var saved = (await create.Content.ReadFromJsonAsync<FlightTaskSavedDto>())!;
        saved = saved with { RowVersion = (await RemoveRecordedFlightAssignmentsAsync(client, id)).RowVersion };
        input = input with { EndUtc = DateTimeOffset.UtcNow };
        using (var tamper = await SendTaskAsync(other, id, recordId, input with { Notes = "Foreign notes" }, saved.RowVersion)) tamper.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var close = await SendTaskAsync(other, id, recordId, input, saved.RowVersion); close.StatusCode.ShouldBe(HttpStatusCode.OK);
        var closed = (await close.Content.ReadFromJsonAsync<FlightTaskSavedDto>())!;
        var mine = (await client.GetFromJsonAsync<FlightDetailDto>($"{Base}/flights/mobile/{id}"))!.Tasks!.Single();
        mine.CanEdit.ShouldBeTrue(); mine.CanSetEnd.ShouldBeFalse(); mine.CanChangeType.ShouldBeTrue(); mine.CanDelete.ShouldBeFalse();
        using (var typeChange = await SendTaskAsync(client, id, recordId, input with { TaskType = 1 }, closed.RowVersion)) typeChange.StatusCode.ShouldBe(HttpStatusCode.OK);
        using (var deletion = await DeleteTaskAsync(client, id, recordId, closed.RowVersion)) deletion.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Task_closure_and_departure_require_staff_and_duration_resource_ends_but_not_quantity_periods()
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync(); var refs = await SetupMasterDataAsync(admin);
        var actor = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view", "operations.flights.operate"]);
        using var client = actor.Client;
        var id = await ScheduleFlightAsync(admin, refs.StationId, refs, "8440", [actor.StaffId]); var recordId = Guid.NewGuid();
        await SeedWorkArrivalAsync(admin, client, id);
        var input = TaskInput(actor.StaffId) with { Tools = [new(Guid.NewGuid(), WellKnownMasterDataIds.UnknownTool, StartUtc: DateTimeOffset.UtcNow.AddMinutes(-10), Notes: "Tool")],
            Materials = [new(Guid.NewGuid(), WellKnownMasterDataIds.UnknownMaterial, 1, Notes: "Quantity material")] };
        var detail = (await client.GetFromJsonAsync<FlightDetailDto>($"{Base}/flights/mobile/{id}"))!;
        using var create = await SendTaskAsync(client, id, recordId, input, detail.RowVersion); create.StatusCode.ShouldBe(HttpStatusCode.OK);
        var open = (await create.Content.ReadFromJsonAsync<FlightTaskSavedDto>())!;
        var departure = new SaveFlightDepartureRequest(DateTimeOffset.UtcNow, ServicePng(), DateTimeOffset.UtcNow, "Completed task");
        async Task<HttpResponseMessage> Depart(string version)
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, $"{Base}/flights/{id}/departure") { Content = JsonContent.Create(departure) };
            request.Headers.TryAddWithoutValidation("If-Match", version); return await client.SendAsync(request);
        }
        using (var incomplete = await Depart(open.RowVersion))
        {
            incomplete.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await incomplete.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe("Operations.Departure.TaskWorkIncomplete");
        }
        var end = departure.Atd.AddMinutes(-1);
        using (var parentOnly = await SendTaskAsync(client, id, recordId, input with { EndUtc = end }, open.RowVersion))
        {
            parentOnly.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            var error = await parentOnly.Content.ReadFromJsonAsync<JsonElement>();
            error.GetProperty("code").GetString().ShouldBe("Operations.FlightTask.OpenResourcePeriods");
            error.GetProperty("errors").TryGetProperty("Performers[0].EndUtc", out _).ShouldBeTrue();
            error.GetProperty("errors").TryGetProperty("Tools[0].EndUtc", out _).ShouldBeTrue();
            error.GetProperty("errors").TryGetProperty("Materials[0].EndUtc", out _).ShouldBeFalse();
        }
        var completed = input with { EndUtc = end, Performers = [input.Performers.Single() with { EndUtc = end }],
            Tools = [input.Tools.Single() with { EndUtc = end }] };
        using var finish = await SendTaskAsync(client, id, recordId, completed, open.RowVersion); finish.StatusCode.ShouldBe(HttpStatusCode.OK);
        var done = (await finish.Content.ReadFromJsonAsync<FlightTaskSavedDto>())!;
        using var depart = await Depart(done.RowVersion); depart.StatusCode.ShouldBe(HttpStatusCode.OK);
        var departed = (await depart.Content.ReadFromJsonAsync<FlightDepartureSavedDto>())!;
        using var reopenResource = await SendTaskAsync(client, id, recordId, completed with { Tools = input.Tools }, departed.RowVersion);
        reopenResource.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await reopenResource.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe("Operations.FlightTask.EndRequiredAfterDeparture");
    }

    [Fact]
    public async Task Task_periods_bound_new_actuals_and_changed_task_input_uses_saved_actuals()
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync(); var refs = await SetupMasterDataAsync(admin);
        var actor = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view", "operations.flights.operate"]);
        using var client = actor.Client;
        var id = await ScheduleFlightAsync(admin, refs.StationId, refs, "8450", [actor.StaffId]); var recordId = Guid.NewGuid();
        await SeedWorkArrivalAsync(admin, client, id);
        var aircraft = await PostForIdAsync(admin, $"{MasterDataBase}/aircraft-types", new { manufacturer = "Airbus", model = $"Task-{Guid.NewGuid():N}" });
        var input = TaskInput(actor.StaffId); var detail = (await client.GetFromJsonAsync<FlightDetailDto>($"{Base}/flights/mobile/{id}"))!;
        using var create = await SendTaskAsync(client, id, recordId, input, detail.RowVersion); create.StatusCode.ShouldBe(HttpStatusCode.OK);
        var saved = (await create.Content.ReadFromJsonAsync<FlightTaskSavedDto>())!;
        async Task<HttpResponseMessage> Arrive(DateTimeOffset ata, string version)
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, $"{Base}/flights/{id}/arrival")
                { Content = JsonContent.Create(new SaveFlightArrivalRequest(ata, aircraft, "HZ-TASK", null, null)) };
            request.Headers.TryAddWithoutValidation("If-Match", version); return await client.SendAsync(request);
        }
        using (var invalidActual = await Arrive(input.StartUtc.AddMinutes(1), saved.RowVersion))
        {
            invalidActual.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await invalidActual.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe("Operations.FlightTask.Validation");
        }
        using var arrive = await Arrive(input.StartUtc.AddMinutes(-1), saved.RowVersion); arrive.StatusCode.ShouldBe(HttpStatusCode.OK);
        var arrival = (await arrive.Content.ReadFromJsonAsync<FlightArrivalSavedDto>())!;
        using var invalidTask = await SendTaskAsync(client, id, recordId, input with { StartUtc = input.StartUtc.AddMinutes(-2) }, arrival.RowVersion);
        invalidTask.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await invalidTask.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe("Operations.FlightTask.Validation");
    }

    private static SaveFlightTaskRequest TaskInput(Guid staffId)
    {
        var start = DateTimeOffset.UtcNow.AddMinutes(-30);
        return new(0, null, start, null, null, "Task notes", [new(Guid.NewGuid(), staffId, start.AddMinutes(5))], [], [], []);
    }
    private static async Task<HttpResponseMessage> SendTaskAsync(HttpClient client, Guid flightId, Guid recordId, SaveFlightTaskRequest input, string version)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"{Base}/flights/{flightId}/tasks/{recordId}") { Content = JsonContent.Create(input) };
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\""); return await client.SendAsync(request);
    }
    private static async Task<HttpResponseMessage> DeleteTaskAsync(HttpClient client, Guid flightId, Guid recordId, string version)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"{Base}/flights/{flightId}/tasks/{recordId}");
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\""); return await client.SendAsync(request);
    }
}
