using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MasterData.Contracts.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Operations.Application.Contracts;
using Operations.Contracts;
using Operations.Domain.ValueObjects;
using Operations.Infrastructure.Persistence;
using Shouldly;

namespace Operations.IntegrationTests;

public sealed partial class FlightVisibilityScopeTests
{
    [Fact]
    public async Task Adhoc_creation_saves_only_flight_details_with_entered_number_and_stationwide_read_attribution()
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var creator = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view", "operations.flights.operate"]);
        var colleague = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view", "operations.flights.operate"]);
        var foreignRefs = await SetupMasterDataAsync(admin);
        var foreign = await CreateStaffLoginAsync(admin, foreignRefs, ["operations.flights.view", "operations.flights.operate"]);
        using var client = creator.Client; using var reader = colleague.Client; using var foreignClient = foreign.Client;
        var id = Guid.NewGuid(); var input = AdHocDetails(refs.CustomerId) with { FlightNumber = " ferry/x " };

        using var response = await CreateMobileAdHocAsync(client, id, input);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var saved = (await response.Content.ReadFromJsonAsync<FlightDetailDto>())!;
        saved.IsAdHocFlight.ShouldBeTrue();
        saved.Id.ShouldBe(id); saved.FlightNumber.ShouldBe("FERRY/X"); saved.StationId.ShouldBe(refs.StationId);
        saved.OperationTypeId.ShouldBe(WellKnownMasterDataIds.AdHocOperationType);
        saved.PlannedServices.ShouldBeEmpty(); saved.AssignedEmployees.ShouldBeEmpty(); saved.AircraftTypeId.ShouldBeNull();
        saved.ArrivalSubmissions!.ShouldBeEmpty(); saved.Services!.ShouldBeEmpty(); saved.Tasks!.ShouldBeEmpty();
        saved.CanEditFlightDetails.ShouldBeTrue(); saved.CreatedByUserId.ShouldNotBe(Guid.Empty);
        saved.CreatedByDisplayName.ShouldNotBeNullOrWhiteSpace();
        var shared = await OperationsBatchDetailAsync(reader, id);
        shared.CreatedByUserId.ShouldBe(saved.CreatedByUserId); shared.CreatedByDisplayName.ShouldBe(saved.CreatedByDisplayName);
        shared.CreatedAtUtc.ShouldBe(saved.CreatedAtUtc); shared.CanEditFlightDetails.ShouldBeFalse();
        shared.CanSaveArrival.ShouldBeTrue(); shared.CanSaveDeparture.ShouldBeTrue(); shared.CanSaveServices.ShouldBeTrue(); shared.CanSaveTasks.ShouldBeTrue();
        var page = (await reader.GetFromJsonAsync<MobileFlightPageDto>($"{Base}/flights/mobile?category=AdHoc&pageSize=100"))!;
        var card = page.Items.Single(item => item.Id == id);
        card.CreatedByUserId.ShouldBe(saved.CreatedByUserId); card.CreatedByDisplayName.ShouldBe(saved.CreatedByDisplayName);
        card.CreatedAtUtc.ShouldBe(saved.CreatedAtUtc);
        (await foreignClient.GetAsync($"{Base}/flights/mobile/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var outside = (await foreignClient.GetFromJsonAsync<MobileFlightPageDto>($"{Base}/flights/mobile?category=AdHoc&pageSize=100"))!;
        outside.Items.ShouldNotContain(item => item.Id == id);
        using (var denied = await SendServiceAsync(foreignClient, id, Guid.NewGuid(),
            ServiceInput(foreignRefs.ServiceId, foreign.StaffId), saved.RowVersion)) denied.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using (var denied = await SendTaskAsync(foreignClient, id, Guid.NewGuid(), TaskInput(foreign.StaffId), saved.RowVersion))
            denied.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using (var denied = await UpdateMobileAdHocAsync(foreignClient, id, input, saved.RowVersion))
            denied.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Adhoc_creation_checks_permission_fields_and_chronology_without_loosening_normal_scheduling()
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var creator = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view", "operations.flights.operate"]);
        var viewer = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view"]);
        using var client = creator.Client; using var readClient = viewer.Client; using var anonymous = factory.CreateClient();
        var valid = AdHocDetails(refs.CustomerId);
        var contextPath = $"{Base}/flights/mobile/adhoc/context";
        using (var contextResponse = await client.GetAsync(contextPath))
        {
            contextResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
            var context = (await contextResponse.Content.ReadFromJsonAsync<MobileAdHocFlightContextDto>())!;
            context.StationId.ShouldBe(refs.StationId); context.CurrentStaffMemberId.ShouldBe(creator.StaffId);
            context.StationTimeZoneId.ShouldBe("Etc/UTC"); context.CanCreate.ShouldBeTrue();
            context.AllowedServiceIds.ShouldContain(refs.ServiceId);
        }
        (await readClient.GetAsync(contextPath)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await anonymous.GetAsync(contextPath)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await admin.GetAsync(contextPath)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using (var denied = await CreateMobileAdHocAsync(readClient, Guid.NewGuid(), valid)) denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using (var unlinked = await CreateMobileAdHocAsync(admin, Guid.NewGuid(), valid)) unlinked.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using (var denied = await CreateMobileAdHocAsync(anonymous, Guid.NewGuid(), valid)) denied.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        foreach (var invalid in new[]
        {
            valid with { CustomerId = Guid.Empty }, valid with { FlightNumber = " " }, valid with { FlightNumber = "1234567890123" },
            valid with { ScheduledArrivalUtc = default }, valid with { ScheduledDepartureUtc = default },
            valid with { ScheduledDepartureUtc = valid.ScheduledArrivalUtc.AddSeconds(-1) }
        })
        {
            var id = Guid.NewGuid();
            using var rejected = await CreateMobileAdHocAsync(client, id, invalid);
            rejected.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await client.GetAsync($"{Base}/flights/mobile/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
        using var strictNumber = await admin.PostAsJsonAsync($"{Base}/flights", new
        {
            customerId = refs.CustomerId, stationId = refs.StationId, operationTypeId = refs.OperationTypeId,
            flightNumber = "FERRY/X", scheduledArrivalUtc = valid.ScheduledArrivalUtc, scheduledDepartureUtc = valid.ScheduledDepartureUtc,
            plannedServiceIds = new[] { refs.ServiceId }, assignedStaffMemberIds = new[] { creator.StaffId }
        });
        strictNumber.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var noScratchScheduling = await admin.PostAsJsonAsync($"{Base}/flights", new
        {
            customerId = refs.CustomerId, stationId = refs.StationId, operationTypeId = WellKnownMasterDataIds.AdHocOperationType,
            flightNumber = "1234", scheduledArrivalUtc = valid.ScheduledArrivalUtc, scheduledDepartureUtc = valid.ScheduledDepartureUtc,
            plannedServiceIds = new[] { refs.ServiceId }, assignedStaffMemberIds = new[] { creator.StaffId }
        });
        noScratchScheduling.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var unknown = valid with { CustomerId = WellKnownMasterDataIds.UnknownCustomer, FlightNumber = "123456789012",
            ScheduledDepartureUtc = valid.ScheduledArrivalUtc };
        using var unknownResponse = await CreateMobileAdHocAsync(client, Guid.NewGuid(), unknown);
        unknownResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var unknownSaved = (await unknownResponse.Content.ReadFromJsonAsync<FlightDetailDto>())!;
        unknownSaved.CustomerId.ShouldBe(WellKnownMasterDataIds.UnknownCustomer);
        unknownSaved.ScheduledArrivalUtc.ShouldBe(unknownSaved.ScheduledDepartureUtc);
    }

    [Fact]
    public async Task Adhoc_creator_edits_require_version_and_retries_survive_later_details_and_arrival_without_duplicate_writes()
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var creator = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view", "operations.flights.operate"]);
        using var client = creator.Client;
        var id = Guid.NewGuid(); var initial = AdHocDetails(refs.CustomerId);
        using var create = await CreateMobileAdHocAsync(client, id, initial); create.StatusCode.ShouldBe(HttpStatusCode.OK);
        var original = (await create.Content.ReadFromJsonAsync<FlightDetailDto>())!;
        var changed = initial with { FlightNumber = " chartr-1 ", ScheduledDepartureUtc = initial.ScheduledDepartureUtc.AddMinutes(30) };
        using (var missing = await UpdateMobileAdHocAsync(client, id, changed, null)) missing.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var update = await UpdateMobileAdHocAsync(client, id, changed, original.RowVersion); update.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = (await update.Content.ReadFromJsonAsync<FlightDetailDto>())!;
        updated.FlightNumber.ShouldBe("CHARTR-1"); updated.CreatedAtUtc.ShouldBe(original.CreatedAtUtc);
        updated.CreatedByUserId.ShouldBe(original.CreatedByUserId); updated.RowVersion.ShouldNotBe(original.RowVersion);
        using (var stale = await UpdateMobileAdHocAsync(client, id, changed with { FlightNumber = "ANOTHER" }, original.RowVersion))
            stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var aircraftId = await OperationsBatchAircraftAsync(admin);
        using var arrivalRequest = new HttpRequestMessage(HttpMethod.Put, $"{Base}/flights/{id}/arrival")
        {
            Content = JsonContent.Create(new SaveFlightArrivalRequest(DateTimeOffset.UtcNow.AddMinutes(-5), aircraftId, "HZ-ADHOC", null, null))
        };
        arrivalRequest.Headers.TryAddWithoutValidation("If-Match", updated.RowVersion);
        using var arrival = await client.SendAsync(arrivalRequest); arrival.StatusCode.ShouldBe(HttpStatusCode.OK);
        var beforeReplay = await OperationsBatchSnapshotAsync(client, id);
        beforeReplay.Detail.CanEditFlightDetails.ShouldBeFalse();
        using (var retry = await CreateMobileAdHocAsync(client, id, initial)) retry.StatusCode.ShouldBe(HttpStatusCode.OK);
        using (var retry = await UpdateMobileAdHocAsync(client, id, changed, original.RowVersion)) retry.StatusCode.ShouldBe(HttpStatusCode.OK);
        await AssertOperationsBatchUnchangedAsync(client, id, beforeReplay);
        using var locked = await UpdateMobileAdHocAsync(client, id, changed with { FlightNumber = "LOCKED" }, beforeReplay.Detail.RowVersion);
        locked.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        await AssertOperationsBatchUnchangedAsync(client, id, beforeReplay);
    }

    [Fact]
    public async Task Stationwide_adhoc_operations_require_arrival_and_keep_planning_creator_owned()
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var creator = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view", "operations.flights.operate"]);
        var colleague = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view", "operations.flights.operate"]);
        using var client = creator.Client; using var foreign = colleague.Client;
        var id = Guid.NewGuid(); var input = AdHocDetails(refs.CustomerId);
        using var create = await CreateMobileAdHocAsync(client, id, input); create.StatusCode.ShouldBe(HttpStatusCode.OK);
        var before = await OperationsBatchSnapshotAsync(client, id);
        using (var update = await UpdateMobileAdHocAsync(foreign, id, input, before.Detail.RowVersion)) update.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using (var reuse = await CreateMobileAdHocAsync(foreign, id, input)) reuse.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using (var changedId = await CreateMobileAdHocAsync(client, id, input with { FlightNumber = "DIFFERENT" })) changedId.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using (var service = await SendServiceAsync(foreign, id, Guid.NewGuid(), ServiceInput(refs.ServiceId, colleague.StaffId), before.Detail.RowVersion))
        {
            service.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await service.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe("Operations.Flight.ArrivalRequired");
        }
        using (var task = await SendTaskAsync(foreign, id, Guid.NewGuid(), TaskInput(colleague.StaffId), before.Detail.RowVersion))
        {
            task.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await task.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe("Operations.Flight.ArrivalRequired");
        }
        await AssertOperationsBatchUnchangedAsync(client, id, before);
        var aircraft = await OperationsBatchAircraftAsync(admin);
        using var arrive = await SendAdHocActualAsync(foreign, id, "arrival",
            new SaveFlightArrivalRequest(DateTimeOffset.UtcNow.AddMinutes(-5), aircraft, "HZ-COLLEAGUE", null, null), before.Detail.RowVersion);
        arrive.StatusCode.ShouldBe(HttpStatusCode.OK);
        var arrived = await OperationsBatchDetailAsync(client, id);
        arrived.ArrivalSubmissions!.Single().SubmittedByEmployee.StaffMemberId.ShouldBe(colleague.StaffId);
        arrived.AssignedEmployees.ShouldBeEmpty(); arrived.CanEditFlightDetails.ShouldBeFalse();
        using var locked = await UpdateMobileAdHocAsync(client, id, input with { FlightNumber = "AFTER-WORK" }, arrived.RowVersion);
        locked.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Racing_identical_adhoc_creates_publish_one_stable_flight()
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var creator = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view", "operations.flights.operate"]);
        using var client = creator.Client; var id = Guid.NewGuid(); var input = AdHocDetails(refs.CustomerId);
        async Task<HttpStatusCode> Create()
        {
            using var response = await CreateMobileAdHocAsync(client, id, input);
            return response.StatusCode;
        }
        var outcomes = await Task.WhenAll(Create(), Create());
        outcomes.ShouldAllBe(status => status == HttpStatusCode.OK);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        (await db.Flights.AsNoTracking().CountAsync(item => item.Id == id)).ShouldBe(1);
        var detail = await OperationsBatchDetailAsync(client, id); detail.FlightNumber.ShouldBe(input.FlightNumber);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Adhoc_station_colleague_fully_corrects_and_deletes_work_preserving_attribution_media_notifications_and_versions(bool atomic)
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var creator = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view", "operations.flights.operate"]);
        var colleague = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view", "operations.flights.operate"]);
        using var author = creator.Client; using var editor = colleague.Client;
        var ownerId = (await author.GetFromJsonAsync<JsonElement>($"{IdentityBase}/me")).GetProperty("id").GetGuid();
        var editorId = (await editor.GetFromJsonAsync<JsonElement>($"{IdentityBase}/me")).GetProperty("id").GetGuid();
        var id = Guid.NewGuid(); var now = DateTimeOffset.UtcNow; var aircraft = await OperationsBatchAircraftAsync(admin);
        using var create = await CreateMobileAdHocAsync(author, id, AdHocDetails(refs.CustomerId));
        create.StatusCode.ShouldBe(HttpStatusCode.OK);
        var initial = (await create.Content.ReadFromJsonAsync<FlightDetailDto>())!;
        var serviceId = Guid.NewGuid(); var taskId = Guid.NewGuid();
        var serviceFile = new SaveFlightServiceAttachmentRequest(Guid.NewGuid(), (int)AttachmentKind.Image, "service.png", "image/png", ServicePng());
        var taskFile = new SaveFlightServiceAttachmentRequest(Guid.NewGuid(), (int)AttachmentKind.Image, "task.png", "image/png", ServicePng());
        var service = OperationsBatchClosedService(refs.ServiceId, creator.StaffId, now.AddMinutes(-80), now.AddMinutes(-40)) with { Attachments = [serviceFile] };
        var task = OperationsBatchClosedTask(creator.StaffId, now.AddMinutes(-70), now.AddMinutes(-30)) with { Attachments = [taskFile] };
        var arrival = new SaveFlightArrivalRequest(now.AddHours(-2), aircraft, "HZ-ORIGINAL", "ORIGINAL", "Original arrival");
        var departure = new SaveFlightDepartureRequest(now.AddMinutes(-5), ServicePng(), now.AddMinutes(-10), "Original departure");
        using var seed = await SendOperationsBatchAsync(author, id, new(arrival, departure,
            [new(serviceId, service)], [new(taskId, task)]), initial.RowVersion);
        seed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var original = (await seed.Content.ReadFromJsonAsync<FlightDetailDto>())!;
        var originalArrival = original.ArrivalSubmissions!.Single(); var originalDeparture = original.DepartureSubmissions!.Single();
        var shared = await OperationsBatchDetailAsync(editor, id);
        shared.AssignedEmployees.ShouldBeEmpty(); shared.CanEditFlightDetails.ShouldBeFalse();
        var serviceCaps = shared.Services!.Single();
        serviceCaps.CanEdit.ShouldBeTrue(); serviceCaps.CanSetEnd.ShouldBeTrue(); serviceCaps.CanChangeService.ShouldBeTrue(); serviceCaps.CanDelete.ShouldBeTrue();
        var taskCaps = shared.Tasks!.Single();
        taskCaps.CanEdit.ShouldBeTrue(); taskCaps.CanSetEnd.ShouldBeTrue(); taskCaps.CanChangeType.ShouldBeTrue(); taskCaps.CanDelete.ShouldBeTrue();
        var filesBefore = (await OperationsBatchSnapshotAsync(author, id)).StoredFiles;
        var changedArrival = arrival with { AircraftTailNumber = "hz-corrected", Remarks = "Colleague arrival", TargetSubmissionId = originalArrival.Id };
        var changedDeparture = departure with { Remarks = "Colleague departure", SignatureContent = null, SignatureSignedAtUtc = null, TargetSubmissionId = originalDeparture.Id };
        var changedService = service with { CustomerReferenceNumber = "colleague", Notes = "Colleague service", Attachments = null };
        var changedTask = task with { TaskType = 1, CustomerReferenceNumber = "colleague", Notes = "Colleague task", Attachments = null };
        var correction = new SaveFlightOperationsBatchRequest(changedArrival, changedDeparture,
            [new(serviceId, changedService)], [new(taskId, changedTask)]);
        FlightDetailDto corrected;
        if (atomic)
        {
            using var response = await SendOperationsBatchAsync(editor, id, correction, original.RowVersion);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            corrected = (await response.Content.ReadFromJsonAsync<FlightDetailDto>())!;
        }
        else
        {
            using var arrive = await SendAdHocActualAsync(editor, id, "arrival", changedArrival, original.RowVersion);
            arrive.StatusCode.ShouldBe(HttpStatusCode.OK);
            var arrivalVersion = (await arrive.Content.ReadFromJsonAsync<FlightArrivalSavedDto>())!.RowVersion;
            using var depart = await SendAdHocActualAsync(editor, id, "departure", changedDeparture, arrivalVersion);
            depart.StatusCode.ShouldBe(HttpStatusCode.OK);
            var departureVersion = (await depart.Content.ReadFromJsonAsync<FlightDepartureSavedDto>())!.RowVersion;
            using var serviceResponse = await SendServiceAsync(editor, id, serviceId, changedService, departureVersion);
            serviceResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
            var serviceVersion = (await serviceResponse.Content.ReadFromJsonAsync<FlightServiceSavedDto>())!.RowVersion;
            using var taskResponse = await SendTaskAsync(editor, id, taskId, changedTask, serviceVersion);
            taskResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
            corrected = await OperationsBatchDetailAsync(editor, id);
        }
        corrected.RowVersion.ShouldNotBe(original.RowVersion); corrected.AssignedEmployees.ShouldBeEmpty();
        corrected.CreatedByUserId.ShouldBe(ownerId); corrected.CreatedAtUtc.ShouldBe(original.CreatedAtUtc);
        var correctedArrival = corrected.ArrivalSubmissions!.Single(); var correctedDeparture = corrected.DepartureSubmissions!.Single();
        correctedArrival.Id.ShouldBe(originalArrival.Id); correctedArrival.SubmittedByEmployee.ShouldBe(originalArrival.SubmittedByEmployee);
        correctedArrival.ReceivedByEmployee.ShouldBe(originalArrival.ReceivedByEmployee); correctedArrival.CreatedAtUtc.ShouldBe(originalArrival.CreatedAtUtc);
        correctedArrival.AircraftTailNumber.ShouldBe("HZ-CORRECTED"); correctedArrival.UpdatedByDisplayName.ShouldNotBeNullOrWhiteSpace();
        correctedDeparture.Id.ShouldBe(originalDeparture.Id); correctedDeparture.SubmittedByEmployee.ShouldBe(originalDeparture.SubmittedByEmployee);
        correctedDeparture.HandledByEmployee.ShouldBe(originalDeparture.HandledByEmployee); correctedDeparture.CreatedAtUtc.ShouldBe(originalDeparture.CreatedAtUtc);
        correctedDeparture.SignatureVersion.ShouldBe(originalDeparture.SignatureVersion); correctedDeparture.UpdatedByDisplayName.ShouldNotBeNullOrWhiteSpace();
        corrected.Services!.Single().CreatedByUserId.ShouldBe(ownerId); corrected.Services!.Single().UpdatedByUserId.ShouldBe(editorId);
        corrected.Services!.Single().CreatedAtUtc.ShouldBe(original.Services!.Single().CreatedAtUtc);
        corrected.Services!.Single().Performers.ShouldBe(original.Services!.Single().Performers);
        corrected.Services!.Single().Attachments.ShouldBe(original.Services!.Single().Attachments);
        corrected.Tasks!.Single().CreatedByUserId.ShouldBe(ownerId); corrected.Tasks!.Single().UpdatedByUserId.ShouldBe(editorId);
        corrected.Tasks!.Single().CreatedAtUtc.ShouldBe(original.Tasks!.Single().CreatedAtUtc); corrected.Tasks!.Single().TaskType.ShouldBe(1);
        corrected.Tasks!.Single().Performers.ShouldBe(original.Tasks!.Single().Performers); corrected.Tasks!.Single().Attachments.ShouldBe(original.Tasks!.Single().Attachments);
        var beforeRetry = await OperationsBatchSnapshotAsync(editor, id); beforeRetry.StoredFiles.ShouldBe(filesBefore);
        using (var retry = await SendOperationsBatchAsync(editor, id, correction, original.RowVersion)) retry.StatusCode.ShouldBe(HttpStatusCode.OK);
        await AssertOperationsBatchUnchangedAsync(editor, id, beforeRetry);
        using (var stale = await SendOperationsBatchAsync(editor, id, correction with
            { ServiceChanges = [new(serviceId, changedService with { Notes = "Stale correction" })] }, original.RowVersion))
            stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        await AssertOperationsBatchUnchangedAsync(editor, id, beforeRetry);
        using (var stale = await SendServiceAsync(editor, id, serviceId, changedService with { Notes = "Stale individual correction" }, original.RowVersion))
            stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        await AssertOperationsBatchUnchangedAsync(editor, id, beforeRetry);
        var servicePreview = $"{Base}/flights/mobile/{id}/services/{serviceId}/attachments/{serviceFile.Id}" +
            $"?expectedUploadedAt={Uri.EscapeDataString(original.Services!.Single().Attachments.Single().UploadedAtUtc.ToString("O"))}";
        var taskPreview = $"{Base}/flights/mobile/{id}/tasks/{taskId}/attachments/{taskFile.Id}" +
            $"?expectedUploadedAt={Uri.EscapeDataString(original.Tasks!.Single().Attachments.Single().UploadedAtUtc.ToString("O"))}";
        var signaturePreview = $"{Base}/flights/mobile/{id}/departure-signatures/{correctedDeparture.Id}" +
            $"?expectedUpdatedAt={Uri.EscapeDataString((correctedDeparture.UpdatedAtUtc ?? correctedDeparture.CreatedAtUtc).ToString("O"))}";
        foreach (var path in new[] { servicePreview, taskPreview, signaturePreview })
        {
            using var preview = await editor.GetAsync(path); preview.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await preview.Content.ReadAsByteArrayAsync()).ShouldBe(ServicePng());
        }
        var updatedNotices = await AdHocUpdateNoticesAsync(id);
        updatedNotices.Count.ShouldBe(4); updatedNotices.ShouldAllBe(item => item.Action == FlightSubmissionAction.Updated &&
            item.SubmittedByUserId == ownerId && item.UpdatedByUserId == editorId && item.UpdatedByStaffMemberId == colleague.StaffId);
        updatedNotices.Select(item => item.SubmissionKind).ShouldBe(new[] { FlightSubmissionKind.Arrival, FlightSubmissionKind.Departure,
            FlightSubmissionKind.Service, FlightSubmissionKind.Task }, ignoreOrder: true);
        if (atomic)
        {
            using var response = await SendOperationsBatchAsync(editor, id,
                new(ServiceChanges: [new(serviceId, null)], TaskChanges: [new(taskId, null)]), corrected.RowVersion);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
        else
        {
            using var serviceResponse = await DeleteServiceAsync(editor, id, serviceId, corrected.RowVersion);
            serviceResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
            var version = (await serviceResponse.Content.ReadFromJsonAsync<FlightServiceRemovedDto>())!.RowVersion;
            using var taskResponse = await DeleteTaskAsync(editor, id, taskId, version); taskResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
        var deleted = await OperationsBatchDetailAsync(editor, id); deleted.Services!.ShouldBeEmpty(); deleted.Tasks!.ShouldBeEmpty();
        deleted.AssignedEmployees.ShouldBeEmpty(); deleted.DepartureSubmissions!.Single().SignatureVersion.ShouldBe(originalDeparture.SignatureVersion);
        (await editor.GetAsync(servicePreview)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await editor.GetAsync(taskPreview)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await editor.GetAsync(signaturePreview)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var afterDelete = await OperationsBatchSnapshotAsync(editor, id);
        filesBefore.Except(afterDelete.StoredFiles).Count().ShouldBe(2);
        var allNotices = await AdHocUpdateNoticesAsync(id); allNotices.Count.ShouldBe(6);
        allNotices.Where(item => item.Action == FlightSubmissionAction.Deleted).Select(item => item.SubmissionKind)
            .ShouldBe(new[] { FlightSubmissionKind.Service, FlightSubmissionKind.Task }, ignoreOrder: true);
        allNotices.ShouldAllBe(item => item.SubmittedByUserId == ownerId && item.UpdatedByUserId == editorId &&
            !string.IsNullOrWhiteSpace(item.UpdatedByDisplayName));
    }

    private async Task<IReadOnlyList<FlightSubmissionUpdated>> AdHocUpdateNoticesAsync(Guid flightId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        var content = await db.OutboxMessages.AsNoTracking().Where(item => item.Type.Contains(nameof(FlightSubmissionUpdated)))
            .Select(item => item.Content).ToListAsync();
        return content.Select(item => JsonSerializer.Deserialize<FlightSubmissionUpdated>(item)!).Where(item => item.FlightId == flightId).ToList();
    }

    private static async Task<HttpResponseMessage> SendAdHocActualAsync<T>(HttpClient client, Guid id, string section, T input, string version)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"{Base}/flights/{id}/{section}") { Content = JsonContent.Create(input) };
        request.Headers.TryAddWithoutValidation("If-Match", version);
        return await client.SendAsync(request);
    }

    private static SaveMobileAdHocFlightDetailsRequest AdHocDetails(Guid customerId)
    {
        var now = DateTimeOffset.UtcNow;
        return new(customerId, "FERRY-1", now.AddMinutes(-15), now.AddHours(1));
    }

    private static async Task<HttpResponseMessage> CreateMobileAdHocAsync(HttpClient client, Guid id, SaveMobileAdHocFlightDetailsRequest input)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"{Base}/flights/mobile/adhoc/{id}") { Content = JsonContent.Create(input) };
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> UpdateMobileAdHocAsync(HttpClient client, Guid id, SaveMobileAdHocFlightDetailsRequest input, string? version)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"{Base}/flights/mobile/adhoc/{id}/details") { Content = JsonContent.Create(input) };
        if (version is not null) request.Headers.TryAddWithoutValidation("If-Match", version);
        return await client.SendAsync(request);
    }
}
