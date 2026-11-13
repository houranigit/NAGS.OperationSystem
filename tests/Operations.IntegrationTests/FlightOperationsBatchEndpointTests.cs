using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Contracts.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Operations.Application.Contracts;
using Operations.Application.Features.Flights;
using Operations.Domain.ValueObjects;
using Operations.Infrastructure.Persistence;
using Shouldly;

namespace Operations.IntegrationTests;

public sealed partial class FlightVisibilityScopeTests
{
    [Theory]
    [InlineData("task-notes")]
    [InlineData("task-attachment")]
    [InlineData("service-attachment")]
    public async Task Operations_batch_invalid_later_section_keeps_all_records_media_and_outbox_unchanged(string invalid)
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var author = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view", "operations.flights.operate"]);
        var editor = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view", "operations.flights.operate"]);
        using var authorClient = author.Client;
        using var client = editor.Client;
        var aircraftId = await OperationsBatchAircraftAsync(admin);
        var id = await ScheduleFlightAsync(admin, refs.StationId, refs, "8701", [author.StaffId, editor.StaffId]);
        await SeedWorkArrivalAsync(admin, authorClient, id);
        var recordId = Guid.NewGuid();
        var originalBytes = Encoding.ASCII.GetBytes("%PDF-original");
        var originalAttachment = new SaveFlightServiceAttachmentRequest(Guid.NewGuid(), (int)AttachmentKind.Document,
            "original.pdf", "application/pdf", originalBytes);
        var originalInput = ServiceInput(refs.ServiceId, author.StaffId) with
        {
            CustomerReferenceNumber = "ORIGINAL", Notes = "Original service", Attachments = [originalAttachment]
        };
        var initial = await OperationsBatchDetailAsync(client, id);
        using (var seed = await SendServiceAsync(authorClient, id, recordId, originalInput, initial.RowVersion))
            seed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var before = await OperationsBatchSnapshotAsync(client, id);
        var replacement = new SaveFlightServiceAttachmentRequest(Guid.NewGuid(), (int)AttachmentKind.Document,
            "replacement.pdf", "application/pdf", Encoding.ASCII.GetBytes("%PDF-replacement"));
        var changes = new List<FlightServiceBatchChange>
        {
            new(recordId, originalInput with
            {
                CustomerReferenceNumber = "UNCOMMITTED", Notes = "Colleague correction must roll back", Attachments = [replacement]
            })
        };
        var task = TaskInput(editor.StaffId);
        if (invalid == "task-notes")
            task = task with { Notes = new string('x', 2001) };
        else if (invalid == "task-attachment")
            task = task with { Attachments = [replacement with { Id = Guid.NewGuid(), Content = Encoding.ASCII.GetBytes("not a PDF") }] };
        else
        {
            var oversizedPdf = new byte[2 * 1024 * 1024 + 1];
            Encoding.ASCII.GetBytes("%PDF-").CopyTo(oversizedPdf, 0);
            changes.Add(new(Guid.NewGuid(), ServiceInput(refs.ServiceId, editor.StaffId) with
            {
                Attachments = [replacement with { Id = Guid.NewGuid(), Content = oversizedPdf }]
            }));
        }
        var arrival = new SaveFlightArrivalRequest(originalInput.StartUtc.AddMinutes(-10), aircraftId, "BATCH-ROLLBACK", null, null);
        var batch = OperationsBatchInput(arrival: arrival, services: changes,
            tasks: [new(Guid.NewGuid(), task)]);

        using var rejected = await SendOperationsBatchAsync(client, id, batch, before.Detail.RowVersion);
        rejected.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        await AssertOperationsBatchUnchangedAsync(client, id, before);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        var stored = await db.Flights.AsNoTracking().Include(flight => flight.ServiceRecords)
            .ThenInclude(service => service.Attachments).SingleAsync(flight => flight.Id == id);
        var oldAttachment = stored.Services.ShouldHaveSingleItem().Attachments.ShouldHaveSingleItem();
        oldAttachment.Id.ShouldBe(originalAttachment.Id);
        await using var oldFile = await scope.ServiceProvider.GetRequiredService<IFileStorage>().OpenAsync(oldAttachment.Details.StorageKey);
        oldFile.ShouldNotBeNull();
        using var content = new MemoryStream();
        await oldFile.CopyToAsync(content);
        content.ToArray().ShouldBe(originalBytes);
    }

    [Fact]
    public async Task Operations_batch_stale_mixed_matching_and_changed_sections_cannot_partially_acknowledge_or_write()
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var actor = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view", "operations.flights.operate"]);
        using var client = actor.Client;
        var id = await ScheduleFlightAsync(admin, refs.StationId, refs, "8702", [actor.StaffId]);
        await SeedWorkArrivalAsync(admin, client, id);
        var serviceId = Guid.NewGuid();
        var input = ServiceInput(refs.ServiceId, actor.StaffId);
        var original = await OperationsBatchDetailAsync(client, id);
        using (var seed = await SendServiceAsync(client, id, serviceId, input, original.RowVersion))
            seed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var before = await OperationsBatchSnapshotAsync(client, id);
        var batch = OperationsBatchInput(services: [new(serviceId, input)],
            tasks: [new(Guid.NewGuid(), TaskInput(actor.StaffId))]);

        using var stale = await SendOperationsBatchAsync(client, id, batch, original.RowVersion);
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await stale.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
            .ShouldBe("General.ConcurrencyConflict");
        await AssertOperationsBatchUnchangedAsync(client, id, before);
    }

    [Fact]
    public async Task Operations_batch_saves_repeated_services_and_task_with_one_flight_version_and_one_root_audit_update()
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var actor = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view", "operations.flights.operate"]);
        using var client = actor.Client;
        var id = await ScheduleFlightAsync(admin, refs.StationId, refs, "8703", [actor.StaffId]);
        await SeedWorkArrivalAsync(admin, client, id);
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var split = DateTimeOffset.UtcNow.AddMinutes(-10);
        var first = OperationsBatchClosedService(refs.ServiceId, actor.StaffId, split.AddMinutes(-20), split);
        var second = ServiceInput(refs.ServiceId, actor.StaffId) with { StartUtc = split,
            Notes = "Separate occurrence of the same catalog service", Performers = [new(Guid.NewGuid(), actor.StaffId, split)] };
        var task = TaskInput(actor.StaffId);
        var before = await OperationsBatchSnapshotAsync(client, id);
        var batch = OperationsBatchInput(services: [new(firstId, first), new(secondId, second)], tasks: [new(taskId, task)]);

        using var response = await SendOperationsBatchAsync(client, id, batch, before.Detail.RowVersion);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var saved = (await response.Content.ReadFromJsonAsync<FlightDetailDto>())!;
        saved.RowVersion.ShouldNotBe(before.Detail.RowVersion);
        saved.Services!.Select(service => service.Id).ShouldBe([firstId, secondId], ignoreOrder: true);
        saved.Services!.ShouldAllBe(service => service.ServiceId == refs.ServiceId);
        saved.Tasks!.ShouldHaveSingleItem().Id.ShouldBe(taskId);
        saved.Services!.SelectMany(service => service.Performers).Select(person => person.Id)
            .ShouldBe([first.Performers.Single().Id, second.Performers.Single().Id], ignoreOrder: true);

        var persisted = await OperationsBatchDetailAsync(client, id);
        persisted.RowVersion.ShouldBe(saved.RowVersion);
        JsonSerializer.Serialize(persisted).ShouldBe(JsonSerializer.Serialize(saved));
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        var newAuditContent = await db.OutboxMessages.AsNoTracking()
            .Where(message => !before.OutboxIds.Contains(message.Id) && message.Type.Contains(nameof(AuditEntryRecorded)))
            .Select(message => message.Content).ToListAsync();
        var rootAudit = newAuditContent.Select(content => JsonSerializer.Deserialize<AuditEntryRecorded>(content)!)
            .Where(entry => entry.EntityId == id).ShouldHaveSingleItem();
        rootAudit.Action.ShouldBe(AuditActions.Updated);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Operations_batch_validates_coordinated_actual_and_work_corrections_against_the_final_graph(bool tightenArrival)
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var actor = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view", "operations.flights.operate"]);
        using var client = actor.Client;
        var aircraftId = await OperationsBatchAircraftAsync(admin);
        var id = await ScheduleFlightAsync(admin, refs.StationId, refs, "8704", [actor.StaffId]);
        var now = DateTimeOffset.UtcNow;
        var serviceId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var service = OperationsBatchClosedService(refs.ServiceId, actor.StaffId, now.AddMinutes(-100), now.AddMinutes(-40));
        var task = OperationsBatchClosedTask(actor.StaffId, now.AddMinutes(-90), now.AddMinutes(-30));
        var arrival = new SaveFlightArrivalRequest(now.AddMinutes(-120), aircraftId, "BATCH-BOUNDS", null, null);
        var departure = new SaveFlightDepartureRequest(now.AddMinutes(-10), ServicePng(), now, null);
        var initial = await OperationsBatchDetailAsync(client, id);
        using var seed = await SendOperationsBatchAsync(client, id,
            OperationsBatchInput(arrival, departure, [new(serviceId, service)], [new(taskId, task)]), initial.RowVersion);
        seed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var original = (await seed.Content.ReadFromJsonAsync<FlightDetailDto>())!;

        var newArrival = arrival with { Ata = now.AddMinutes(tightenArrival ? -80 : -150) };
        var newDeparture = departure with
        {
            Atd = now.AddMinutes(tightenArrival ? -5 : -50), SignatureContent = null, SignatureSignedAtUtc = null
        };
        var correctedService = service with
        {
            StartUtc = now.AddMinutes(tightenArrival ? -75 : -140),
            EndUtc = now.AddMinutes(tightenArrival ? -25 : -70),
            Performers = [service.Performers.Single() with
            {
                StartUtc = now.AddMinutes(tightenArrival ? -70 : -135), EndUtc = now.AddMinutes(tightenArrival ? -30 : -75)
            }]
        };
        var correctedTask = task with
        {
            StartUtc = now.AddMinutes(tightenArrival ? -70 : -130),
            EndUtc = now.AddMinutes(tightenArrival ? -20 : -60),
            Performers = [task.Performers.Single() with
            {
                StartUtc = now.AddMinutes(tightenArrival ? -65 : -125), EndUtc = now.AddMinutes(tightenArrival ? -25 : -65)
            }]
        };

        using var correction = await SendOperationsBatchAsync(client, id,
            OperationsBatchInput(newArrival, newDeparture, [new(serviceId, correctedService)], [new(taskId, correctedTask)]), original.RowVersion);
        correction.StatusCode.ShouldBe(HttpStatusCode.OK);
        var saved = (await correction.Content.ReadFromJsonAsync<FlightDetailDto>())!;
        saved.ArrivalSubmissions!.ShouldHaveSingleItem().Ata.ShouldBe(newArrival.Ata);
        saved.ArrivalSubmissions.Single().Id.ShouldBe(original.ArrivalSubmissions!.Single().Id);
        saved.DepartureSubmissions!.ShouldHaveSingleItem().Atd.ShouldBe(newDeparture.Atd);
        saved.DepartureSubmissions.Single().Id.ShouldBe(original.DepartureSubmissions!.Single().Id);
        saved.DepartureSubmissions.Single().SignatureVersion.ShouldBe(original.DepartureSubmissions!.Single().SignatureVersion);
        saved.Services!.ShouldHaveSingleItem().StartUtc.ShouldBe(correctedService.StartUtc);
        saved.Services.Single().EndUtc.ShouldBe(correctedService.EndUtc);
        saved.Tasks!.ShouldHaveSingleItem().StartUtc.ShouldBe(correctedTask.StartUtc);
        saved.Tasks.Single().EndUtc.ShouldBe(correctedTask.EndUtc);
        var persisted = await OperationsBatchDetailAsync(client, id);
        JsonSerializer.Serialize(persisted).ShouldBe(JsonSerializer.Serialize(saved));
    }

    [Fact]
    public async Task Operations_batch_exact_whole_retry_keeps_record_ids_signature_files_and_outbox_without_another_write()
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var actor = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view", "operations.flights.operate"]);
        using var client = actor.Client;
        var aircraftId = await OperationsBatchAircraftAsync(admin);
        var id = await ScheduleFlightAsync(admin, refs.StationId, refs, "8705", [actor.StaffId]);
        var now = DateTimeOffset.UtcNow;
        var service = OperationsBatchClosedService(refs.ServiceId, actor.StaffId, now.AddMinutes(-50), now.AddMinutes(-20)) with
        {
            Attachments = [new(Guid.NewGuid(), (int)AttachmentKind.Document, "service.pdf", "application/pdf", Encoding.ASCII.GetBytes("%PDF-service"))]
        };
        var task = OperationsBatchClosedTask(actor.StaffId, now.AddMinutes(-40), now.AddMinutes(-10)) with
        {
            Attachments = [new(Guid.NewGuid(), (int)AttachmentKind.Image, "task.png", "image/png", ServicePng())]
        };
        var batch = OperationsBatchInput(new(now.AddMinutes(-60), aircraftId, "BATCH-RETRY", "PKG-RETRY", "Arrival"),
            new(now.AddMinutes(-5), ServicePng(), now, "Departure"),
            [new(Guid.NewGuid(), service)], [new(Guid.NewGuid(), task)]);
        var initial = await OperationsBatchDetailAsync(client, id);
        using (var save = await SendOperationsBatchAsync(client, id, batch, initial.RowVersion))
            save.StatusCode.ShouldBe(HttpStatusCode.OK);
        var beforeReplay = await OperationsBatchSnapshotAsync(client, id);
        beforeReplay.Detail.ArrivalSubmissions!.Count.ShouldBe(1);
        beforeReplay.Detail.DepartureSubmissions!.Count.ShouldBe(1);
        beforeReplay.Detail.Services!.Single().Attachments.Count.ShouldBe(1);
        beforeReplay.Detail.Tasks!.Single().Attachments.Count.ShouldBe(1);

        using var replay = await SendOperationsBatchAsync(client, id, batch, initial.RowVersion);
        replay.StatusCode.ShouldBe(HttpStatusCode.OK);
        var acknowledged = (await replay.Content.ReadFromJsonAsync<FlightDetailDto>())!;
        acknowledged.RowVersion.ShouldBe(beforeReplay.Detail.RowVersion);
        JsonSerializer.Serialize(acknowledged).ShouldBe(beforeReplay.GraphJson);
        await AssertOperationsBatchUnchangedAsync(client, id, beforeReplay);
    }

    [Theory]
    [InlineData(true, "Operations.WorkOrder.ServiceNotAllowed")]
    [InlineData(false, "Operations.Ref.StaffInactive")]
    public async Task Operations_batch_rechecks_catalog_changes_after_review_even_when_flight_version_did_not_change(
        bool revokeService, string expectedCode)
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var actor = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view", "operations.flights.operate"]);
        var performer = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view"]);
        using var client = actor.Client;
        using var performerClient = performer.Client;
        var id = await ScheduleFlightAsync(admin, refs.StationId, refs, "8709", [actor.StaffId]);
        await SeedWorkArrivalAsync(admin, client, id);
        var reviewed = await OperationsBatchDetailAsync(client, id);
        var service = ServiceInput(refs.ServiceId, performer.StaffId) with
        {
            Attachments = [new(Guid.NewGuid(), (int)AttachmentKind.Image, "service.png", "image/png", ServicePng())]
        };
        var batch = OperationsBatchInput(services: [new(Guid.NewGuid(), service)],
            tasks: [new(Guid.NewGuid(), TaskInput(actor.StaffId))]);
        var path = revokeService
            ? $"{MasterDataBase}/manpower-types/{refs.ManpowerTypeId}"
            : $"{MasterDataBase}/staff-members/{performer.StaffId}";
        var metadata = (await admin.GetFromJsonAsync<ConcurrencyDetail>(path))!;
        using (var change = new HttpRequestMessage(revokeService ? HttpMethod.Put : HttpMethod.Post,
            path + (revokeService ? "/service-allowances" : "/deactivate")))
        {
            if (revokeService) change.Content = JsonContent.Create(new { serviceIds = Array.Empty<Guid>() });
            change.Headers.TryAddWithoutValidation("If-Match", metadata.RowVersion);
            using var changed = await admin.SendAsync(change);
            changed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }
        var before = await OperationsBatchSnapshotAsync(client, id);
        before.Detail.RowVersion.ShouldBe(reviewed.RowVersion);

        using var rejected = await SendOperationsBatchAsync(client, id, batch, reviewed.RowVersion);
        rejected.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await rejected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe(expectedCode);
        await AssertOperationsBatchUnchangedAsync(client, id, before);
    }

    [Fact]
    public async Task Operations_batch_boundary_requires_operate_current_version_and_mobile_visibility()
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var actor = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view", "operations.flights.operate"]);
        var reader = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view"]);
        using var client = actor.Client;
        using var readerClient = reader.Client;
        using var anonymous = factory.CreateClient();
        var id = await ScheduleFlightAsync(admin, refs.StationId, refs, "8706", [actor.StaffId, reader.StaffId]);
        var before = await OperationsBatchSnapshotAsync(client, id);
        var batch = OperationsBatchInput(services: [new(Guid.NewGuid(), ServiceInput(refs.ServiceId, actor.StaffId))]);
        using (var missingVersion = await SendOperationsBatchAsync(client, id, batch, null))
            missingVersion.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using (var malformedVersion = await SendOperationsBatchAsync(client, id, batch, "invalid-version"))
            malformedVersion.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using (var forbidden = await SendOperationsBatchAsync(readerClient, id, batch, before.Detail.RowVersion))
            forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using (var unauthenticated = await SendOperationsBatchAsync(anonymous, id, batch, before.Detail.RowVersion))
            unauthenticated.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var hiddenId = await ScheduleFlightAsync(admin, await CreateStationAsync(admin, refs.CountryId), refs, "8707");
        using (var hidden = await SendOperationsBatchAsync(client, hiddenId, batch, before.Detail.RowVersion))
            hidden.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await AssertOperationsBatchUnchangedAsync(client, id, before);
    }

    [Fact]
    public async Task Operations_batch_rejects_declared_oversized_body_before_parsing_or_writing()
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var actor = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view", "operations.flights.operate"]);
        using var client = actor.Client;
        var id = await ScheduleFlightAsync(admin, refs.StationId, refs, "8708", [actor.StaffId]);
        var before = await OperationsBatchSnapshotAsync(client, id);
        using var request = new HttpRequestMessage(HttpMethod.Put, $"{Base}/flights/{id}/operations-batch")
        {
            Content = new ByteArrayContent([])
        };
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        request.Content.Headers.ContentLength = FlightOperationsBatchPolicy.MaxJsonRequestBytes + 1;
        request.Headers.TryAddWithoutValidation("If-Match", before.Detail.RowVersion);

        using var rejected = await client.SendAsync(request);
        rejected.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        await AssertOperationsBatchUnchangedAsync(client, id, before);
    }

    private static async Task SeedWorkArrivalAsync(HttpClient admin, HttpClient client, Guid flightId)
    {
        var aircraft = await OperationsBatchAircraftAsync(admin);
        var detail = await OperationsBatchDetailAsync(client, flightId);
        using var request = new HttpRequestMessage(HttpMethod.Put, $"{Base}/flights/{flightId}/arrival")
        {
            Content = JsonContent.Create(new SaveFlightArrivalRequest(DateTimeOffset.UtcNow.AddHours(-6), aircraft,
                "HZ-WORK", null, "Arrival prerequisite"))
        };
        request.Headers.TryAddWithoutValidation("If-Match", detail.RowVersion);
        using var response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static SaveFlightOperationsBatchRequest OperationsBatchInput(SaveFlightArrivalRequest? arrival = null,
        SaveFlightDepartureRequest? departure = null, IReadOnlyList<FlightServiceBatchChange>? services = null,
        IReadOnlyList<FlightTaskBatchChange>? tasks = null) => new(arrival, departure, services ?? [], tasks ?? []);

    private static async Task<HttpResponseMessage> SendOperationsBatchAsync(HttpClient client, Guid id,
        SaveFlightOperationsBatchRequest input, string? version)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"{Base}/flights/{id}/operations-batch")
        {
            Content = JsonContent.Create(input)
        };
        if (version is not null) request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        return await client.SendAsync(request);
    }

    private static async Task<FlightDetailDto> OperationsBatchDetailAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<FlightDetailDto>($"{Base}/flights/mobile/{id}"))!;

    private static Task<Guid> OperationsBatchAircraftAsync(HttpClient admin) => PostForIdAsync(admin,
        $"{MasterDataBase}/aircraft-types", new { manufacturer = "Airbus", model = $"Batch-{Guid.NewGuid():N}" });

    private static SaveFlightServiceRequest OperationsBatchClosedService(Guid serviceId, Guid staffId,
        DateTimeOffset start, DateTimeOffset end) => new(serviceId, start, end, null, "Batch service",
        [new(Guid.NewGuid(), staffId, start.AddMinutes(5), end.AddMinutes(-5))]);

    private static SaveFlightTaskRequest OperationsBatchClosedTask(Guid staffId, DateTimeOffset start, DateTimeOffset end) =>
        new(0, null, start, end, null, "Batch task", [new(Guid.NewGuid(), staffId, start.AddMinutes(5), end.AddMinutes(-5))], [], [], []);

    private sealed record OperationsBatchSnapshot(FlightDetailDto Detail, string GraphJson, Guid[] OutboxIds, string[] StoredFiles);

    private async Task<OperationsBatchSnapshot> OperationsBatchSnapshotAsync(HttpClient client, Guid id)
    {
        var detail = await OperationsBatchDetailAsync(client, id);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        var flightId = id.ToString();
        var outboxIds = await db.OutboxMessages.AsNoTracking().Where(message => message.Content.Contains(flightId))
            .OrderBy(message => message.Id).Select(message => message.Id).ToArrayAsync();
        var storageRoot = scope.ServiceProvider.GetRequiredService<IConfiguration>()["FileStorage:RootPath"]!;
        var files = Directory.Exists(storageRoot)
            ? Directory.GetFiles(storageRoot, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray()
            : [];
        return new(detail, JsonSerializer.Serialize(detail), outboxIds, files);
    }

    private async Task AssertOperationsBatchUnchangedAsync(HttpClient client, Guid id, OperationsBatchSnapshot before)
    {
        var after = await OperationsBatchSnapshotAsync(client, id);
        after.Detail.RowVersion.ShouldBe(before.Detail.RowVersion);
        after.GraphJson.ShouldBe(before.GraphJson);
        after.OutboxIds.ShouldBe(before.OutboxIds);
        after.StoredFiles.ShouldBe(before.StoredFiles);
    }
}
