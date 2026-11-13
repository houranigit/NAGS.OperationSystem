using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Operations.Application.Contracts;
using Operations.Infrastructure.Persistence;
using Shouldly;

namespace Operations.IntegrationTests;

public sealed partial class FlightVisibilityScopeTests
{
    [Fact]
    public async Task History_is_account_wide_saved_work_recency_and_text_filter_never_counts_viewing_or_other_accounts()
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var refs = await SetupMasterDataAsync(admin);
        var employee = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view", "operations.flights.operate"]);
        var colleague = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view", "operations.flights.operate"]);
        using var client = employee.Client; using var other = colleague.Client;
        var userId = (await client.GetFromJsonAsync<JsonElement>($"{IdentityBase}/me")).GetProperty("id").GetGuid();
        var firstId = Guid.NewGuid(); var otherId = Guid.NewGuid(); var latestId = Guid.NewGuid();
        var firstInput = AdHocDetails(refs.CustomerId) with { FlightNumber = "9001" };
        using (var created = await CreateMobileAdHocAsync(client, firstId, firstInput)) created.StatusCode.ShouldBe(HttpStatusCode.OK);
        using (var created = await CreateMobileAdHocAsync(other, otherId, AdHocDetails(refs.CustomerId) with { FlightNumber = "9002" }))
            created.StatusCode.ShouldBe(HttpStatusCode.OK);
        var viewedId = await ScheduleFlightAsync(admin, refs.StationId, refs, "9003", [employee.StaffId]);
        (await client.GetAsync($"{Base}/flights/mobile/{viewedId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync($"{Base}/flights/mobile/{otherId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        using (var created = await CreateMobileAdHocAsync(client, latestId, AdHocDetails(refs.CustomerId) with { FlightNumber = "9004" }))
            created.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var secondDevice = factory.CreateClient(); secondDevice.DefaultRequestHeaders.Authorization = client.DefaultRequestHeaders.Authorization;
        var history = (await secondDevice.GetFromJsonAsync<MobileFlightPageDto>($"{Base}/flights/mobile/history?search=900&pageSize=100"))!;
        history.TotalCount.ShouldBe(2); history.Items.Select(item => item.Id).ShouldBe([latestId, firstId]);
        history.Items.ShouldAllBe(item => item.LastInteractionAtUtc.HasValue && item.CreatedByUserId == userId && item.CreatedByDisplayName != null);
        var none = (await client.GetFromJsonAsync<MobileFlightPageDto>($"{Base}/flights/mobile/history?search=9003"))!;
        none.Items.ShouldBeEmpty();
        var otherHistory = (await other.GetFromJsonAsync<MobileFlightPageDto>($"{Base}/flights/mobile/history?search=900"))!;
        otherHistory.Items.ShouldHaveSingleItem().Id.ShouldBe(otherId);
        var first = await OperationsBatchDetailAsync(client, firstId);
        using (var update = await UpdateMobileAdHocAsync(client, firstId,
            firstInput with { ScheduledDepartureUtc = firstInput.ScheduledDepartureUtc.AddMinutes(1) }, first.RowVersion))
            update.StatusCode.ShouldBe(HttpStatusCode.OK);
        var afterChange = (await secondDevice.GetFromJsonAsync<MobileFlightPageDto>($"{Base}/flights/mobile/history?search=900"))!;
        afterChange.Items[0].Id.ShouldBe(firstId); var recordedAt = afterChange.Items[0].LastInteractionAtUtc;
        using (var retry = await CreateMobileAdHocAsync(client, firstId, firstInput)) retry.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync($"{Base}/flights/mobile/{firstId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var afterReads = (await secondDevice.GetFromJsonAsync<MobileFlightPageDto>($"{Base}/flights/mobile/history?search=900"))!;
        afterReads.Items[0].LastInteractionAtUtc.ShouldBe(recordedAt); afterReads.TotalCount.ShouldBe(2);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        (await db.FlightUserActivities.AsNoTracking().CountAsync(item => item.UserId == userId)).ShouldBe(2);
    }
}
