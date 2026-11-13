using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Operations.Application.Contracts;
using Operations.Domain.ValueObjects;
using Operations.Infrastructure.Persistence;
using Shouldly;

namespace Operations.IntegrationTests;

public sealed partial class FlightVisibilityScopeTests
{
    [Fact]
    public async Task Service_boundary_requires_operate_permission_current_version_valid_performers_and_mobile_scope()
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var actor = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view", "operations.flights.operate"]);
        var reader = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view"]);
        using var actorClient = actor.Client;
        using var readerClient = reader.Client;
        using var anonymous = factory.CreateClient();
        var flightId = await ScheduleFlightAsync(admin, refs.StationId, refs, "8100", [actor.StaffId, reader.StaffId]);
        await SeedWorkArrivalAsync(admin, actorClient, flightId);
        var recordId = Guid.NewGuid();
        var path = $"{Base}/flights/{flightId}/services/{recordId}";
        var detail = (await actorClient.GetFromJsonAsync<FlightDetailDto>($"{Base}/flights/mobile/{flightId}"))!;
        detail.CanSaveServices.ShouldBeTrue();
        detail.AllowedServiceIds.ShouldNotBeNull().ShouldContain(refs.ServiceId);
        var input = ServiceInput(refs.ServiceId, actor.StaffId);

        using (var missingVersion = await actorClient.PutAsJsonAsync(path, input))
        {
            missingVersion.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await missingVersion.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
                .ShouldBe("General.PreconditionRequired");
        }
        using (var denied = await SendServiceAsync(readerClient, flightId, recordId, input, detail.RowVersion))
            denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using (var unauthenticated = await SendServiceAsync(anonymous, flightId, recordId, input, detail.RowVersion))
            unauthenticated.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using (var missingDeleteVersion = await actorClient.DeleteAsync(path))
            missingDeleteVersion.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using (var deniedDelete = await DeleteServiceAsync(readerClient, flightId, recordId, detail.RowVersion))
            deniedDelete.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var performer = input.Performers.Single();
        foreach (var invalid in new[]
        {
            input with { Performers = [] },
            input with { Performers = [performer, performer with { Id = Guid.NewGuid() }] },
            input with { Performers = [performer, performer with { StaffMemberId = reader.StaffId }] },
            input with { EndUtc = input.StartUtc.AddMinutes(-1) }
        })
        {
            using var response = await SendServiceAsync(actorClient, flightId, recordId, invalid, detail.RowVersion);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }
        var otherRefs = await SetupMasterDataAsync(admin);
        var foreignStaff = await CreateStaffLoginAsync(admin, otherRefs, ["operations.flights.view"]);
        using var foreignStaffClient = foreignStaff.Client;
        using (var wrongStationPerformer = await SendServiceAsync(actorClient, flightId, recordId,
            input with { Performers = [performer with { StaffMemberId = foreignStaff.StaffId }] }, detail.RowVersion))
        {
            wrongStationPerformer.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await wrongStationPerformer.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
                .ShouldBe("Operations.Ref.StaffStationMismatch");
        }
        var hiddenId = await ScheduleFlightAsync(admin, await CreateStationAsync(admin, refs.CountryId), refs, "8101");
        using (var hidden = await SendServiceAsync(actorClient, hiddenId, Guid.NewGuid(), input, detail.RowVersion))
            hidden.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var unchanged = (await actorClient.GetFromJsonAsync<FlightDetailDto>($"{Base}/flights/mobile/{flightId}"))!;
        unchanged.Services!.ShouldBeEmpty();
        unchanged.RowVersion.ShouldBe(detail.RowVersion);
        var readOnly = (await readerClient.GetFromJsonAsync<FlightDetailDto>($"{Base}/flights/mobile/{flightId}"))!;
        readOnly.CanSaveServices.ShouldBeFalse();
    }

    [Fact]
    public async Task Service_save_round_trips_sql_ids_media_and_counts_and_replays_exact_old_version_without_duplicates()
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var actor = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view", "operations.flights.operate"]);
        using var client = actor.Client;
        var actorId = (await client.GetFromJsonAsync<JsonElement>($"{IdentityBase}/me")).GetProperty("id").GetGuid();
        var flightId = await ScheduleFlightAsync(admin, refs.StationId, refs, "8110", [actor.StaffId]);
        await SeedWorkArrivalAsync(admin, client, flightId);
        var recordId = Guid.NewGuid();
        var attachment = new SaveFlightServiceAttachmentRequest(Guid.NewGuid(), (int)AttachmentKind.Image,
            "folder/photo.png", "image/png", ServicePng());
        var input = ServiceInput(refs.ServiceId, actor.StaffId) with
        {
            CustomerReferenceNumber = " REF-SERVICE ", Notes = " Recorded service ", Attachments = [attachment]
        };
        var detail = (await client.GetFromJsonAsync<FlightDetailDto>($"{Base}/flights/mobile/{flightId}"))!;
        using var initial = await SendServiceAsync(client, flightId, recordId, input, detail.RowVersion);
        initial.StatusCode.ShouldBe(HttpStatusCode.OK);
        var saved = (await initial.Content.ReadFromJsonAsync<FlightServiceSavedDto>())!;
        saved.Service.Id.ShouldBe(recordId);
        saved.Service.CreatedByUserId.ShouldBe(actorId);
        saved.Service.Performers.ShouldHaveSingleItem().Id.ShouldBe(input.Performers.Single().Id);
        saved.Service.Attachments.ShouldHaveSingleItem().Id.ShouldBe(attachment.Id);
        saved.Service.Attachments.Single().FileName.ShouldBe("photo.png");
        saved.Service.CustomerReferenceNumber.ShouldBe("REF-SERVICE");
        saved.Service.Notes.ShouldBe("Recorded service");
        saved.Service.CanEdit.ShouldBeTrue();
        saved.Service.CanSetEnd.ShouldBeTrue();
        saved.Service.CanChangeService.ShouldBeTrue();
        saved.Service.CanDelete.ShouldBeTrue();
        saved.RowVersion.ShouldNotBe(detail.RowVersion);
        saved.ActiveWorkCount.ShouldBe(1);
        saved.CompletedWorkCount.ShouldBe(0);

        using var retry = await SendServiceAsync(client, flightId, recordId, input, detail.RowVersion);
        retry.StatusCode.ShouldBe(HttpStatusCode.OK);
        var replayed = (await retry.Content.ReadFromJsonAsync<FlightServiceSavedDto>())!;
        replayed.Service.Id.ShouldBe(recordId);
        replayed.RowVersion.ShouldBe(saved.RowVersion);
        replayed.Service.Attachments.Single().UploadedAtUtc.ShouldBe(saved.Service.Attachments.Single().UploadedAtUtc);
        using var stale = await SendServiceAsync(client, flightId, recordId, input with { Notes = "Stale correction" }, detail.RowVersion);
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var repeatedId = Guid.NewGuid();
        var repeatedInput = input with
        {
            StartUtc = input.StartUtc.AddMinutes(-30), EndUtc = input.StartUtc,
            Performers = [input.Performers.Single() with { Id = Guid.NewGuid(), StartUtc = input.StartUtc.AddMinutes(-25), EndUtc = input.StartUtc.AddMinutes(-5) }],
            Attachments = []
        };
        using var repeated = await SendServiceAsync(client, flightId, repeatedId, repeatedInput, saved.RowVersion);
        repeated.StatusCode.ShouldBe(HttpStatusCode.OK);
        var second = (await repeated.Content.ReadFromJsonAsync<FlightServiceSavedDto>())!;
        second.ActiveWorkCount.ShouldBe(1);
        second.CompletedWorkCount.ShouldBe(1);
        using var retryAfterOtherWork = await SendServiceAsync(client, flightId, recordId, input, detail.RowVersion);
        retryAfterOtherWork.StatusCode.ShouldBe(HttpStatusCode.OK);
        var acknowledged = (await retryAfterOtherWork.Content.ReadFromJsonAsync<FlightServiceSavedDto>())!;
        acknowledged.RowVersion.ShouldBe(second.RowVersion);
        acknowledged.ActiveWorkCount.ShouldBe(1);
        acknowledged.CompletedWorkCount.ShouldBe(1);
        var readback = (await client.GetFromJsonAsync<FlightDetailDto>($"{Base}/flights/mobile/{flightId}"))!;
        readback.Services!.Count.ShouldBe(2);
        readback.Services.ShouldContain(service => service.Id == recordId && service.CanEdit && service.CanSetEnd);
        readback.PlannedServices.ShouldHaveSingleItem().ServiceId.ShouldBe(refs.ServiceId);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        var stored = await db.Flights.Include(f => f.ServiceRecords).ThenInclude(s => s.Performers)
            .Include(f => f.ServiceRecords).ThenInclude(s => s.Attachments).SingleAsync(f => f.Id == flightId);
        stored.Services.Count.ShouldBe(2);
        var media = stored.Services.Single(service => service.Id == recordId).Attachments.ShouldHaveSingleItem();
        media.Id.ShouldBe(attachment.Id);
        media.UploadedByUserId.ShouldBe(actorId);
        await using var content = await scope.ServiceProvider.GetRequiredService<IFileStorage>().OpenAsync(media.Details.StorageKey);
        content.ShouldNotBeNull();
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer);
        buffer.ToArray().ShouldBe(ServicePng());
    }

    [Fact]
    public async Task Service_attachment_preview_is_private_versioned_scoped_and_does_not_expose_storage_keys()
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var actor = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view", "operations.flights.operate"]);
        var viewer = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view"]);
        using var actorClient = actor.Client;
        using var viewerClient = viewer.Client;
        var flightId = await ScheduleFlightAsync(admin, refs.StationId, refs, "8120", [actor.StaffId, viewer.StaffId]);
        await SeedWorkArrivalAsync(admin, actorClient, flightId);
        var recordId = Guid.NewGuid();
        var attachment = new SaveFlightServiceAttachmentRequest(Guid.NewGuid(), (int)AttachmentKind.Image, "photo.png", "image/png", ServicePng());
        var input = ServiceInput(refs.ServiceId, actor.StaffId) with { Attachments = [attachment] };
        var detail = (await actorClient.GetFromJsonAsync<FlightDetailDto>($"{Base}/flights/mobile/{flightId}"))!;
        using var save = await SendServiceAsync(actorClient, flightId, recordId, input, detail.RowVersion);
        save.StatusCode.ShouldBe(HttpStatusCode.OK);
        var saved = (await save.Content.ReadFromJsonAsync<FlightServiceSavedDto>())!;
        var timestamp = saved.Service.Attachments.ShouldHaveSingleItem().UploadedAtUtc;
        string Preview(Guid? flight = null, Guid? record = null, Guid? file = null, DateTimeOffset? expected = null) =>
            $"{Base}/flights/mobile/{flight ?? flightId}/services/{record ?? recordId}/attachments/{file ?? attachment.Id}" +
            $"?expectedUploadedAt={Uri.EscapeDataString((expected ?? timestamp).ToString("O"))}";
        foreach (var client in new[] { actorClient, viewerClient })
        {
            using var preview = await client.GetAsync(Preview());
            preview.StatusCode.ShouldBe(HttpStatusCode.OK);
            preview.Content.Headers.ContentType!.MediaType.ShouldBe("image/png");
            preview.Headers.CacheControl!.Private.ShouldBeTrue();
            preview.Headers.CacheControl.NoStore.ShouldBeTrue();
            (await preview.Content.ReadAsByteArrayAsync()).ShouldBe(ServicePng());
        }
        using (var wrongVersion = await actorClient.GetAsync(Preview(expected: timestamp.AddSeconds(1))))
            wrongVersion.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        using (var wrongParent = await actorClient.GetAsync(Preview(record: Guid.NewGuid())))
            wrongParent.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using (var wrongAttachment = await actorClient.GetAsync(Preview(file: Guid.NewGuid())))
            wrongAttachment.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var hiddenId = await ScheduleFlightAsync(admin, await CreateStationAsync(admin, refs.CountryId), refs, "8121");
        using (var hidden = await actorClient.GetAsync(Preview(flight: hiddenId)))
            hidden.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using (var anonymous = factory.CreateClient())
        using (var unauthenticated = await anonymous.GetAsync(Preview()))
            unauthenticated.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var json = (await viewerClient.GetFromJsonAsync<JsonElement>($"{Base}/flights/mobile/{flightId}"));
        var serviceJson = json.GetProperty("services").EnumerateArray().Single();
        serviceJson.GetProperty("canEdit").GetBoolean().ShouldBeFalse();
        serviceJson.GetProperty("canSetEnd").GetBoolean().ShouldBeFalse();
        serviceJson.GetProperty("canChangeService").GetBoolean().ShouldBeFalse();
        serviceJson.GetProperty("canDelete").GetBoolean().ShouldBeFalse();
        var metadata = serviceJson.GetProperty("attachments").EnumerateArray().Single();
        metadata.TryGetProperty("storageKey", out _).ShouldBeFalse();
        metadata.TryGetProperty("storageReference", out _).ShouldBeFalse();
        metadata.TryGetProperty("content", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Service_end_owner_is_independent_of_creator_and_delete_preserves_another_users_end()
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var creator = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view", "operations.flights.operate"]);
        var closer = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view", "operations.flights.operate"]);
        using var creatorClient = creator.Client;
        using var closerClient = closer.Client;
        var creatorId = (await creatorClient.GetFromJsonAsync<JsonElement>($"{IdentityBase}/me")).GetProperty("id").GetGuid();
        var closerId = (await closerClient.GetFromJsonAsync<JsonElement>($"{IdentityBase}/me")).GetProperty("id").GetGuid();
        var flightId = await ScheduleFlightAsync(admin, refs.StationId, refs, "8130", [creator.StaffId]);
        await SeedWorkArrivalAsync(admin, creatorClient, flightId);
        var recordId = Guid.NewGuid();
        var input = ServiceInput(refs.ServiceId, creator.StaffId);
        input = input with { Performers = [input.Performers.Single() with { EndUtc = input.StartUtc.AddMinutes(20) }] };
        var detail = (await creatorClient.GetFromJsonAsync<FlightDetailDto>($"{Base}/flights/mobile/{flightId}"))!;
        using var create = await SendServiceAsync(creatorClient, flightId, recordId, input, detail.RowVersion);
        create.StatusCode.ShouldBe(HttpStatusCode.OK);
        var created = (await create.Content.ReadFromJsonAsync<FlightServiceSavedDto>())!;
        created = created with { RowVersion = (await RemoveRecordedFlightAssignmentsAsync(creatorClient, flightId)).RowVersion };
        var closedInput = input with { EndUtc = input.StartUtc.AddMinutes(30) };
        using (var tamper = await SendServiceAsync(closerClient, flightId, recordId, closedInput with { Notes = "Unowned notes" }, created.RowVersion))
            tamper.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var close = await SendServiceAsync(closerClient, flightId, recordId, closedInput, created.RowVersion);
        close.StatusCode.ShouldBe(HttpStatusCode.OK);
        var closed = (await close.Content.ReadFromJsonAsync<FlightServiceSavedDto>())!;
        closed.Service.CreatedByUserId.ShouldBe(creatorId);
        closed.Service.EndedByUserId.ShouldBe(closerId);
        closed.Service.CanEdit.ShouldBeFalse();
        closed.Service.CanSetEnd.ShouldBeFalse();
        closed.ActiveWorkCount.ShouldBe(0);
        closed.CompletedWorkCount.ShouldBe(1);
        var ownerView = (await creatorClient.GetFromJsonAsync<FlightDetailDto>($"{Base}/flights/mobile/{flightId}"))!;
        ownerView.Services!.ShouldHaveSingleItem().CanEdit.ShouldBeTrue();
        ownerView.Services.Single().CanSetEnd.ShouldBeFalse();
        ownerView.Services.Single().CanChangeService.ShouldBeFalse();
        ownerView.Services.Single().CanDelete.ShouldBeFalse();
        using (var overwrite = await SendServiceAsync(creatorClient, flightId, recordId, input, closed.RowVersion))
            overwrite.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using (var delete = await DeleteServiceAsync(creatorClient, flightId, recordId, closed.RowVersion))
            delete.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var correctedInput = closedInput with { Notes = "Creator correction keeps the recorded end" };
        using var correction = await SendServiceAsync(creatorClient, flightId, recordId, correctedInput, closed.RowVersion);
        correction.StatusCode.ShouldBe(HttpStatusCode.OK);
        var corrected = (await correction.Content.ReadFromJsonAsync<FlightServiceSavedDto>())!;
        corrected.Service.EndedByUserId.ShouldBe(closerId);
        corrected.Service.EndUtc.ShouldBe(closedInput.EndUtc);
        using var clear = await SendServiceAsync(closerClient, flightId, recordId, correctedInput with { EndUtc = null }, corrected.RowVersion);
        clear.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var retained = await OperationsBatchDetailAsync(creatorClient, flightId);
        retained.Services!.ShouldHaveSingleItem().EndUtc.ShouldBe(closedInput.EndUtc);
    }

    [Fact]
    public async Task Departure_requires_service_and_employee_ends_while_service_saves_can_remain_open()
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var actor = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view", "operations.flights.operate"]);
        using var client = actor.Client;
        var flightId = await ScheduleFlightAsync(admin, refs.StationId, refs, "8140", [actor.StaffId]);
        await SeedWorkArrivalAsync(admin, client, flightId);
        var recordId = Guid.NewGuid();
        var input = ServiceInput(refs.ServiceId, actor.StaffId);
        var detail = (await client.GetFromJsonAsync<FlightDetailDto>($"{Base}/flights/mobile/{flightId}"))!;
        using var initial = await SendServiceAsync(client, flightId, recordId, input, detail.RowVersion);
        initial.StatusCode.ShouldBe(HttpStatusCode.OK);
        var open = (await initial.Content.ReadFromJsonAsync<FlightServiceSavedDto>())!;
        open.Service.EndUtc.ShouldBeNull();
        open.Service.Performers.ShouldHaveSingleItem().EndUtc.ShouldBeNull();
        var departure = new SaveFlightDepartureRequest(DateTimeOffset.UtcNow, ServicePng(), DateTimeOffset.UtcNow, "Departure after completed work");
        async Task<HttpResponseMessage> Depart(string version)
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, $"{Base}/flights/{flightId}/departure")
            {
                Content = JsonContent.Create(departure)
            };
            request.Headers.TryAddWithoutValidation("If-Match", version);
            return await client.SendAsync(request);
        }
        using (var unfinishedService = await Depart(open.RowVersion))
        {
            unfinishedService.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await unfinishedService.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
                .ShouldBe("Operations.Departure.ServiceWorkIncomplete");
        }
        var serviceEnd = departure.Atd.AddMinutes(-1);
        var parentClosed = input with { EndUtc = serviceEnd };
        using (var closeService = await SendServiceAsync(client, flightId, recordId, parentClosed, open.RowVersion))
        {
            closeService.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            var failure = await closeService.Content.ReadFromJsonAsync<JsonElement>();
            failure.GetProperty("code").GetString().ShouldBe("Operations.FlightService.OpenPerformerPeriods");
            failure.GetProperty("errors").TryGetProperty("Performers[0].EndUtc", out _).ShouldBeTrue();
        }
        using (var unfinishedEmployee = await Depart(open.RowVersion))
        {
            unfinishedEmployee.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await unfinishedEmployee.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
                .ShouldBe("Operations.Departure.ServiceWorkIncomplete");
        }
        var completed = parentClosed with { Performers = [input.Performers.Single() with { EndUtc = serviceEnd }] };
        using var closeEmployee = await SendServiceAsync(client, flightId, recordId, completed, open.RowVersion);
        closeEmployee.StatusCode.ShouldBe(HttpStatusCode.OK);
        var allClosed = (await closeEmployee.Content.ReadFromJsonAsync<FlightServiceSavedDto>())!;
        using var acceptedDeparture = await Depart(allClosed.RowVersion);
        acceptedDeparture.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await acceptedDeparture.Content.ReadFromJsonAsync<FlightDepartureSavedDto>())!.Submission.Atd.ShouldBe(departure.Atd);
    }

    private static SaveFlightServiceRequest ServiceInput(Guid serviceId, Guid staffId)
    {
        var start = DateTimeOffset.UtcNow.AddMinutes(-30);
        return new(serviceId, start, null, null, "Service notes", [new(Guid.NewGuid(), staffId, start.AddMinutes(5))]);
    }

    private static async Task<HttpResponseMessage> SendServiceAsync(HttpClient client, Guid flightId, Guid recordId,
        SaveFlightServiceRequest input, string rowVersion)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"{Base}/flights/{flightId}/services/{recordId}")
        {
            Content = JsonContent.Create(input)
        };
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{rowVersion}\"");
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> DeleteServiceAsync(HttpClient client, Guid flightId, Guid recordId, string rowVersion)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"{Base}/flights/{flightId}/services/{recordId}");
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{rowVersion}\"");
        return await client.SendAsync(request);
    }

    private static byte[] ServicePng() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/l9sAAAAASUVORK5CYII=");
}
