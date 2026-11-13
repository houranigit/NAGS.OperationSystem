using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Operations.Application.Contracts;
using Shouldly;

namespace Operations.IntegrationTests;

public sealed partial class FlightVisibilityScopeTests
{
    [Fact]
    public async Task Operational_writes_without_arrival_reject_individual_and_combined_input_without_publication()
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var actor = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view", "operations.flights.operate"]);
        using var client = actor.Client;
        var id = await ScheduleFlightAsync(admin, refs.StationId, refs, "8790", [actor.StaffId]);
        var before = await OperationsBatchSnapshotAsync(client, id);
        var service = ServiceInput(refs.ServiceId, actor.StaffId);
        var task = TaskInput(actor.StaffId);
        var departure = new SaveFlightDepartureRequest(DateTimeOffset.UtcNow, null, null, null);
        using var serviceResponse = await SendServiceAsync(client, id, Guid.NewGuid(), service, before.Detail.RowVersion);
        using var taskResponse = await SendTaskAsync(client, id, Guid.NewGuid(), task, before.Detail.RowVersion);
        using var departureRequest = new HttpRequestMessage(HttpMethod.Put, $"{Base}/flights/{id}/departure")
            { Content = JsonContent.Create(departure) };
        departureRequest.Headers.TryAddWithoutValidation("If-Match", before.Detail.RowVersion);
        using var departureResponse = await client.SendAsync(departureRequest);
        using var batchResponse = await SendOperationsBatchAsync(client, id,
            new(Departure: departure, ServiceChanges: [new(Guid.NewGuid(), service)], TaskChanges: [new(Guid.NewGuid(), task)]),
            before.Detail.RowVersion);
        foreach (var response in new[] { serviceResponse, taskResponse, departureResponse, batchResponse })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
                .ShouldBe("Operations.Flight.ArrivalRequired");
        }
        await AssertOperationsBatchUnchangedAsync(client, id, before);
    }

    [Fact]
    public async Task Combined_arrival_adjacent_repeated_services_and_unsigned_departure_roundtrip_and_replay_lowercase_input()
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var actor = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view", "operations.flights.operate"]);
        using var client = actor.Client;
        var id = await ScheduleFlightAsync(admin, refs.StationId, refs, "8791", [actor.StaffId]);
        var aircraft = await OperationsBatchAircraftAsync(admin); var now = DateTimeOffset.UtcNow;
        var first = OperationsBatchClosedService(refs.ServiceId, actor.StaffId, now.AddMinutes(-50), now.AddMinutes(-35))
            with { CustomerReferenceNumber = " cust-a " };
        var second = OperationsBatchClosedService(refs.ServiceId, actor.StaffId, now.AddMinutes(-35), now.AddMinutes(-20));
        var batch = new SaveFlightOperationsBatchRequest(
            new(now.AddHours(-1), aircraft, " hz-ab1 ", " pkg-b ", null), new(now.AddMinutes(-5), null, null, "Unsigned"),
            [new(Guid.NewGuid(), first), new(Guid.NewGuid(), second)]);
        var initial = await OperationsBatchDetailAsync(client, id);
        using var response = await SendOperationsBatchAsync(client, id, batch, initial.RowVersion);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var saved = (await response.Content.ReadFromJsonAsync<FlightDetailDto>())!;
        saved.ArrivalSubmissions!.Single().AircraftTailNumber.ShouldBe("HZ-AB1");
        saved.ArrivalSubmissions.Single().CustomerWorkReferenceNumber.ShouldBe("PKG-B");
        saved.Services!.Single(item => item.Id == batch.ServiceChanges![0].RecordId).CustomerReferenceNumber.ShouldBe("CUST-A");
        var departure = saved.DepartureSubmissions!.Single();
        departure.HasSignature.ShouldBeFalse(); departure.SignatureVersion.ShouldBeNull();
        departure.CreatedByDisplayName.ShouldNotBeNullOrWhiteSpace(); departure.UpdatedByDisplayName.ShouldBeNull();
        using var missingSignature = await client.GetAsync($"{Base}/flights/mobile/{id}/departure-signatures/{departure.Id}" +
            $"?expectedUpdatedAt={Uri.EscapeDataString(departure.CreatedAtUtc.ToString("O"))}");
        missingSignature.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var beforeReplay = await OperationsBatchSnapshotAsync(client, id);
        using var replay = await SendOperationsBatchAsync(client, id, batch, initial.RowVersion);
        replay.StatusCode.ShouldBe(HttpStatusCode.OK);
        await AssertOperationsBatchUnchangedAsync(client, id, beforeReplay);
    }
}
