using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Operations.Application.Contracts;
using Operations.Domain.Flights;
using Operations.Infrastructure.Persistence;
using Shouldly;

namespace Operations.IntegrationTests;

public sealed partial class FlightVisibilityScopeTests
{
    [Fact]
    public async Task Concurrent_work_order_numbers_are_unique_per_station_and_reused_after_unlock()
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var station = await SetupMasterDataAsync(admin);
        var otherStation = station with { StationId = await CreateStationAsync(admin, station.CountryId) };
        var now = DateTimeOffset.UtcNow;
        var first = await CancelFlightAsync(station, "8761");
        var second = await CancelFlightAsync(station, "8762");
        var third = await CancelFlightAsync(station, "8763");
        var independent = await CancelFlightAsync(otherStation, "8764");

        var barrier = new ApprovalSaveBarrier(first.Id, second.Id);
        using var concurrentHost = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddDbContext<OperationsDbContext>(options => options.AddInterceptors(barrier))));
        using var concurrentClient = concurrentHost.CreateClient();
        concurrentClient.DefaultRequestHeaders.Authorization = admin.DefaultRequestHeaders.Authorization;

        var responses = await Task.WhenAll(
            WritePortalOperationsAsync(concurrentClient, first.Id, HttpMethod.Put, "approval", null, first.Version),
            WritePortalOperationsAsync(concurrentClient, second.Id, HttpMethod.Put, "approval", null, second.Version));
        using var firstResponse = responses[0];
        using var secondResponse = responses[1];
        responses.Select(response => response.StatusCode).ShouldBe([HttpStatusCode.OK, HttpStatusCode.Conflict], ignoreOrder: true);
        var (winnerResponse, conflictResponse, loser) = firstResponse.StatusCode == HttpStatusCode.OK
            ? (firstResponse, secondResponse, second)
            : (secondResponse, firstResponse, first);
        var winner = (await winnerResponse.Content.ReadFromJsonAsync<FlightDetailDto>())!;
        (await conflictResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
            .ShouldBe("General.ConcurrencyConflict");
        var rejected = await PortalOperationsDetailAsync(admin, loser.Id);
        rejected.ApprovalStatus.ShouldBe(FlightApprovalStatus.NotApproved);
        rejected.WorkOrderNumber.ShouldBeNull();
        rejected.RowVersion.ShouldBe(loser.Version);

        var firstNumber = $"{winner.StationIata}-0001";
        winner.WorkOrderNumber.ShouldBe(firstNumber);
        var retried = await ApprovePortalFlightAsync(admin, loser.Id, rejected.RowVersion);
        retried.WorkOrderNumber.ShouldBe($"{winner.StationIata}-0002");
        var otherApproval = await ApprovePortalFlightAsync(admin, independent.Id, independent.Version);
        otherApproval.WorkOrderNumber.ShouldBe($"{otherApproval.StationIata}-0001");

        using var unlock = await WritePortalOperationsAsync(admin, winner.Id, HttpMethod.Delete, "approval", null, winner.RowVersion);
        unlock.StatusCode.ShouldBe(HttpStatusCode.OK);
        var unlocked = (await unlock.Content.ReadFromJsonAsync<FlightDetailDto>())!;
        await AssertPortalOperationsConflictAsync(
            await WritePortalOperationsAsync(admin, winner.Id, HttpMethod.Put, "approval", null, winner.RowVersion),
            "General.ConcurrencyConflict");
        (await PortalOperationsDetailAsync(admin, winner.Id)).WorkOrderNumber.ShouldBeNull();
        var reused = await ApprovePortalFlightAsync(admin, third.Id, third.Version);
        reused.WorkOrderNumber.ShouldBe(firstNumber);
        var reapproved = await ApprovePortalFlightAsync(admin, winner.Id, unlocked.RowVersion);
        reapproved.WorkOrderNumber.ShouldBe($"{winner.StationIata}-0003");

        var list = (await admin.GetFromJsonAsync<PagedList<FlightListItemDto>>($"{Base}/flights?stationId={station.StationId}"))!;
        list.Items.Single(item => item.Id == winner.Id).WorkOrderNumber.ShouldBe(reapproved.WorkOrderNumber);
        list.Items.Single(item => item.Id == third.Id).WorkOrderNumber.ShouldBe(firstNumber);

        async Task<(Guid Id, string Version)> CancelFlightAsync(MasterDataRefs refs, string number)
        {
            var id = await SchedulePortalFlightAtAsync(admin, refs, number, now, []);
            var initial = await PortalOperationsDetailAsync(admin, id);
            using var cancel = await WritePortalOperationsAsync(admin, id, HttpMethod.Put, "cancellation",
                new SaveFlightCancellationRequest(now, "Canceled before service"), initial.RowVersion);
            cancel.StatusCode.ShouldBe(HttpStatusCode.OK);
            return (id, (await cancel.Content.ReadFromJsonAsync<FlightCancellationSavedDto>())!.RowVersion);
        }
    }

    private sealed class ApprovalSaveBarrier(Guid firstFlightId, Guid secondFlightId) : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource _bothReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context is OperationsDbContext db && db.ChangeTracker.Entries<Flight>().Any(entry =>
                entry.State == EntityState.Modified && entry.Entity.ApprovalSequence is not null &&
                (entry.Entity.Id == firstFlightId || entry.Entity.Id == secondFlightId)))
            {
                // Reach the write boundary only after both requests have independently allocated the same number.
                if (Interlocked.Increment(ref _arrivals) == 2)
                    _bothReady.TrySetResult();
                await _bothReady.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            }

            return result;
        }
    }
}
