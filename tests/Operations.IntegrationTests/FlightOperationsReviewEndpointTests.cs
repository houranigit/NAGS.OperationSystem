using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Operations.Application.Contracts;
using Operations.Contracts;
using Operations.Infrastructure.Persistence;
using Shouldly;

namespace Operations.IntegrationTests;

public sealed partial class FlightVisibilityScopeTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Operations_review_identical_colleague_actuals_acknowledge_without_duplicate_write_or_notification(
        bool explicitTarget, bool repeatSignature)
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var author = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view", "operations.flights.operate"]);
        var colleague = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view", "operations.flights.operate"]);
        using var authorClient = author.Client;
        using var client = colleague.Client;
        var aircraftId = await OperationsBatchAircraftAsync(admin);
        var id = await ScheduleFlightAsync(admin, refs.StationId, refs, "8751", [author.StaffId, colleague.StaffId]);
        var now = DateTimeOffset.UtcNow;
        var arrival = new SaveFlightArrivalRequest(now.AddHours(-2), aircraftId, "HZ-REVIEW", "ONLINE-REF", "Arrival");
        var departure = new SaveFlightDepartureRequest(now.AddMinutes(-5), ServicePng(), now.AddMinutes(-10), "Departure");
        var original = await OperationsBatchDetailAsync(client, id);
        using (var seed = await SendOperationsBatchAsync(authorClient, id, OperationsBatchInput(arrival, departure), original.RowVersion))
            seed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var before = await OperationsBatchSnapshotAsync(client, id);
        var onlineArrival = before.Detail.ArrivalSubmissions!.ShouldHaveSingleItem();
        var onlineDeparture = before.Detail.DepartureSubmissions!.ShouldHaveSingleItem();
        var sameArrival = arrival with
        {
            AircraftTailNumber = " HZ-REVIEW ", CustomerWorkReferenceNumber = " ONLINE-REF ", Remarks = " Arrival ",
            TargetSubmissionId = explicitTarget ? onlineArrival.Id : null
        };
        var sameDeparture = departure with
        {
            Remarks = " Departure ", TargetSubmissionId = explicitTarget ? onlineDeparture.Id : null,
            SignatureContent = repeatSignature ? departure.SignatureContent : null,
            SignatureSignedAtUtc = repeatSignature ? departure.SignatureSignedAtUtc : null
        };

        using var acknowledged = await SendOperationsBatchAsync(client, id, OperationsBatchInput(sameArrival, sameDeparture), original.RowVersion);

        acknowledged.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = (await acknowledged.Content.ReadFromJsonAsync<FlightDetailDto>())!;
        result.RowVersion.ShouldBe(before.Detail.RowVersion);
        result.ArrivalSubmissions!.ShouldHaveSingleItem().Id.ShouldBe(onlineArrival.Id);
        result.DepartureSubmissions!.ShouldHaveSingleItem().Id.ShouldBe(onlineDeparture.Id);
        result.ArrivalSubmissions.Single().SubmittedByEmployee.StaffMemberId.ShouldBe(author.StaffId);
        result.DepartureSubmissions.Single().SubmittedByEmployee.StaffMemberId.ShouldBe(author.StaffId);
        await AssertOperationsBatchUnchangedAsync(client, id, before);
    }

    [Fact]
    public async Task Operations_review_different_stale_colleague_actuals_reject_the_entire_batch_without_publishing_work()
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var author = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view", "operations.flights.operate"]);
        var colleague = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view", "operations.flights.operate"]);
        using var authorClient = author.Client;
        using var client = colleague.Client;
        var aircraftId = await OperationsBatchAircraftAsync(admin);
        var id = await ScheduleFlightAsync(admin, refs.StationId, refs, "8752", [author.StaffId, colleague.StaffId]);
        var now = DateTimeOffset.UtcNow;
        var arrival = new SaveFlightArrivalRequest(now.AddHours(-2), aircraftId, "HZ-ONLINE", "ONLINE-REF", "Arrival");
        var departure = new SaveFlightDepartureRequest(now.AddMinutes(-5), ServicePng(), now.AddMinutes(-10), "Departure");
        var original = await OperationsBatchDetailAsync(client, id);
        using (var seed = await SendOperationsBatchAsync(authorClient, id, OperationsBatchInput(arrival, departure), original.RowVersion))
            seed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var before = await OperationsBatchSnapshotAsync(client, id);
        var local = OperationsBatchInput(arrival with { AircraftTailNumber = "HZ-LOCAL" }, departure with { Remarks = "Local departure" },
            [new(Guid.NewGuid(), OperationsBatchClosedService(refs.ServiceId, colleague.StaffId, now.AddMinutes(-90), now.AddMinutes(-30)))],
            [new(Guid.NewGuid(), OperationsBatchClosedTask(colleague.StaffId, now.AddMinutes(-80), now.AddMinutes(-20)))]);

        using var rejected = await SendOperationsBatchAsync(client, id, local, original.RowVersion);

        rejected.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        await AssertOperationsBatchUnchangedAsync(client, id, before);
    }

    [Fact]
    public async Task Operations_review_targeted_overwrite_requires_current_review_and_preserves_original_attribution_signature_and_notifications()
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var author = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view", "operations.flights.operate"]);
        var colleague = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view", "operations.flights.operate"]);
        using var authorClient = author.Client;
        using var client = colleague.Client;
        var aircraftId = await OperationsBatchAircraftAsync(admin);
        var id = await ScheduleFlightAsync(admin, refs.StationId, refs, "8753", [author.StaffId, colleague.StaffId]);
        var now = DateTimeOffset.UtcNow;
        var arrival = new SaveFlightArrivalRequest(now.AddHours(-2), aircraftId, "HZ-ONLINE", "ORIGINAL-REF", "Original arrival");
        var departure = new SaveFlightDepartureRequest(now.AddMinutes(-5), ServicePng(), now.AddMinutes(-10), "Original departure");
        var original = await OperationsBatchDetailAsync(client, id);
        using (var seed = await SendOperationsBatchAsync(authorClient, id, OperationsBatchInput(arrival, departure), original.RowVersion))
            seed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var reviewed = await OperationsBatchDetailAsync(client, id);
        var arrivalId = reviewed.ArrivalSubmissions!.ShouldHaveSingleItem().Id;
        var departureId = reviewed.DepartureSubmissions!.ShouldHaveSingleItem().Id;

        // A colleague can change the record after Review opens. That old review cannot authorize overwrite.
        using (var external = await SendOperationsBatchAsync(authorClient, id,
            OperationsBatchInput(arrival with { CustomerWorkReferenceNumber = "LATEST-ONLINE-REF" }), reviewed.RowVersion))
            external.StatusCode.ShouldBe(HttpStatusCode.OK);
        var before = await OperationsBatchSnapshotAsync(client, id);
        var oldReviewInput = OperationsBatchInput(
            arrival with { AircraftTailNumber = "HZ-LOCAL", TargetSubmissionId = arrivalId },
            departure with { Remarks = "Local departure", SignatureContent = null, SignatureSignedAtUtc = null, TargetSubmissionId = departureId });
        using (var stale = await SendOperationsBatchAsync(client, id, oldReviewInput, reviewed.RowVersion))
            stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        await AssertOperationsBatchUnchangedAsync(client, id, before);

        // Mobile merges present local fields over the reloaded online form, then sends a complete PUT.
        var onlineArrival = before.Detail.ArrivalSubmissions!.Single();
        var onlineDeparture = before.Detail.DepartureSubmissions!.Single();
        var correction = OperationsBatchInput(new(onlineArrival.Ata, onlineArrival.AircraftTypeId, "HZ-LOCAL",
            onlineArrival.CustomerWorkReferenceNumber, onlineArrival.Remarks, TargetSubmissionId: arrivalId),
            new(onlineDeparture.Atd, null, null, "Local departure", TargetSubmissionId: departureId));
        using var accepted = await SendOperationsBatchAsync(client, id, correction, before.Detail.RowVersion);
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK);
        var saved = (await accepted.Content.ReadFromJsonAsync<FlightDetailDto>())!;
        saved.ArrivalSubmissions!.ShouldHaveSingleItem().Id.ShouldBe(arrivalId);
        saved.DepartureSubmissions!.ShouldHaveSingleItem().Id.ShouldBe(departureId);
        var savedArrival = saved.ArrivalSubmissions.Single();
        var savedDeparture = saved.DepartureSubmissions.Single();
        savedArrival.AircraftTailNumber.ShouldBe("HZ-LOCAL");
        savedArrival.CustomerWorkReferenceNumber.ShouldBe("LATEST-ONLINE-REF");
        savedArrival.Remarks.ShouldBe(onlineArrival.Remarks);
        savedArrival.SubmittedByEmployee.StaffMemberId.ShouldBe(author.StaffId);
        savedArrival.ReceivedByEmployee.ShouldBe(onlineArrival.ReceivedByEmployee);
        savedArrival.CreatedAtUtc.ShouldBe(onlineArrival.CreatedAtUtc);
        savedDeparture.Remarks.ShouldBe("Local departure");
        savedDeparture.SubmittedByEmployee.StaffMemberId.ShouldBe(author.StaffId);
        savedDeparture.HandledByEmployee.ShouldBe(onlineDeparture.HandledByEmployee);
        savedDeparture.CreatedAtUtc.ShouldBe(onlineDeparture.CreatedAtUtc);
        savedDeparture.SignatureVersion.ShouldBe(onlineDeparture.SignatureVersion);
        savedArrival.IsAccepted.ShouldBeTrue();
        savedDeparture.IsAccepted.ShouldBeTrue();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
            var flight = await db.Flights.AsNoTracking().Include(item => item.ArrivalSubmissions)
                .Include(item => item.DepartureSubmissions).SingleAsync(item => item.Id == id);
            var storedArrival = flight.ArrivalSubmissions.Single();
            var storedDeparture = flight.DepartureSubmissions.Single();
            storedArrival.CreatedByUserId.ShouldBe(storedDeparture.CreatedByUserId);
            storedArrival.UpdatedByUserId.ShouldNotBe(storedArrival.CreatedByUserId);
            storedArrival.UpdatedByUserId.ShouldBe(storedDeparture.UpdatedByUserId);
            var idText = id.ToString();
            var messages = await db.OutboxMessages.AsNoTracking().Where(message => message.Content.Contains(idText) &&
                message.Type.Contains(nameof(FlightSubmissionUpdated))).ToListAsync();
            var notices = messages.Select(message => JsonSerializer.Deserialize<FlightSubmissionUpdated>(message.Content)!).ToList();
            notices.Count.ShouldBe(2);
            notices.Select(notice => notice.SubmissionKind).Order().ShouldBe([FlightSubmissionKind.Arrival, FlightSubmissionKind.Departure]);
            notices.ShouldAllBe(notice => notice.SubmittedByStaffMemberId == author.StaffId &&
                notice.SubmittedByUserId == storedArrival.CreatedByUserId && notice.UpdatedByStaffMemberId == colleague.StaffId &&
                notice.UpdatedByUserId == storedArrival.UpdatedByUserId && notice.Action == FlightSubmissionAction.Updated);
        }
        var after = await OperationsBatchSnapshotAsync(client, id);
        after.StoredFiles.ShouldBe(before.StoredFiles);
        using var replay = await SendOperationsBatchAsync(client, id, correction, before.Detail.RowVersion);
        replay.StatusCode.ShouldBe(HttpStatusCode.OK);
        await AssertOperationsBatchUnchangedAsync(client, id, after);
    }
}
