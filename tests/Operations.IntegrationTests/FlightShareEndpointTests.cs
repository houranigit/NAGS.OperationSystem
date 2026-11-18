using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Operations.Application.Contracts;
using Operations.Contracts;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
using Operations.Infrastructure.Persistence;
using Shouldly;

namespace Operations.IntegrationTests;

public sealed partial class FlightVisibilityScopeTests
{
    [Fact]
    public async Task Combined_share_feed_defaults_to_all_and_pages_visible_posts_in_one_global_order()
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync(); var refs = await SetupMasterDataAsync(admin);
        var first = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view", "operations.flights.operate"]);
        var second = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view", "operations.flights.operate"]);
        var third = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view"]);
        using var author = first.Client; using var colleague = second.Client; using var thirdReader = third.Client;
        var flightId = await ScheduleFlightAsync(admin, refs.StationId, refs, "9802", [first.StaffId, second.StaffId]);
        var endpoint = $"{Base}/flights/mobile/{flightId}/shares";
        async Task<FlightSharePostDto> Publish(HttpClient client, PublishFlightShareRequest input)
        {
            using var response = await client.PutAsJsonAsync($"{endpoint}/{Guid.NewGuid()}", input);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            return (await response.Content.ReadFromJsonAsync<FlightSharePostDto>())!;
        }
        var ownPrivate = await Publish(author, new("My sent post", [third.StaffId]));
        var publicPost = await Publish(colleague, new("Station post"));
        var targeted = await Publish(colleague, new("Targeted to first", [first.StaffId]));
        var hidden = await Publish(colleague, new("Private for third", [third.StaffId]));
        var ownPublic = await Publish(author, new("My public post"));
        var feed = $"{Base}/flights/mobile/shares";
        var page1 = (await author.GetFromJsonAsync<MobileFlightSharePageDto>($"{feed}?pageSize=2"))!;
        var page2 = (await author.GetFromJsonAsync<MobileFlightSharePageDto>($"{feed}?direction=all&page=2&pageSize=2"))!;
        page1.TotalCount.ShouldBe(4); page1.HasNextPage.ShouldBeTrue(); page2.HasNextPage.ShouldBeFalse();
        var visible = page1.Items.Concat(page2.Items).ToList();
        visible.Select(item => item.Id).ShouldBe([ownPublic.Id, targeted.Id, publicPost.Id, ownPrivate.Id]);
        visible.ShouldNotContain(item => item.Id == hidden.Id); visible.Select(item => item.Id).Distinct().Count().ShouldBe(4);
        visible.ShouldAllBe(item => !string.IsNullOrWhiteSpace(item.StationName));
        (await author.GetFromJsonAsync<MobileFlightSharePageDto>($"{feed}?direction=received"))!.TotalCount.ShouldBe(2);
        (await author.GetFromJsonAsync<MobileFlightSharePageDto>($"{feed}?direction=sent"))!.TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task Share_posts_use_sql_paging_targeted_and_public_audiences_without_granting_assignment_or_work_rights()
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync(); var refs = await SetupMasterDataAsync(admin);
        var author = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view", "operations.flights.operate"]);
        var recipient = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view", "operations.flights.operate"]);
        var other = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view", "operations.flights.operate"]);
        using var publisher = author.Client; using var reader = recipient.Client; using var otherReader = other.Client;
        var flightId = await ScheduleFlightAsync(admin, refs.StationId, refs, "9800", [author.StaffId]);
        await SeedWorkArrivalAsync(admin, publisher, flightId);
        var detailUrl = $"{Base}/flights/mobile/{flightId}";
        var authorBefore = (await publisher.GetFromJsonAsync<FlightDetailDto>(detailUrl))!;
        var recipientBefore = (await reader.GetFromJsonAsync<FlightDetailDto>(detailUrl))!;
        authorBefore.CanShare.ShouldBeTrue(); recipientBefore.CanShare.ShouldBeFalse();
        var id = Guid.NewGuid(); var path = $"{detailUrl}/shares/{id}";
        var body = new PublishFlightShareRequest("  Please continue the work  ", [recipient.StaffId]);
        // Concurrent duplicate HTTP attempts must recover one immutable post, not create two.
        var responses = await Task.WhenAll(publisher.PutAsJsonAsync(path, body), publisher.PutAsJsonAsync(path, body));
        foreach (var response in responses) response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var first = (await responses[0].Content.ReadFromJsonAsync<FlightSharePostDto>())!;
        var second = (await responses[1].Content.ReadFromJsonAsync<FlightSharePostDto>())!;
        first.Id.ShouldBe(id); second.Id.ShouldBe(id); second.CreatedAtUtc.ShouldBe(first.CreatedAtUtc);
        first.Message.ShouldBe("Please continue the work"); first.AuthorStaffMemberId.ShouldBe(author.StaffId);
        first.Recipients.ShouldHaveSingleItem().StaffMemberId.ShouldBe(recipient.StaffId); first.IsStationWide.ShouldBeFalse();
        foreach (var response in responses) response.Dispose();
        using var conflict = await publisher.PutAsJsonAsync(path, body with { Message = "Different" });
        conflict.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using var publicPost = await publisher.PutAsJsonAsync($"{detailUrl}/shares/{Guid.NewGuid()}", new PublishFlightShareRequest());
        publicPost.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var repeated = await publisher.PutAsJsonAsync($"{detailUrl}/shares/{Guid.NewGuid()}", body);
        repeated.StatusCode.ShouldBe(HttpStatusCode.OK);
        var feeds = $"{Base}/flights/mobile/shares";
        var received = (await reader.GetFromJsonAsync<MobileFlightSharePageDto>($"{feeds}?direction=received&pageSize=2"))!;
        received.TotalCount.ShouldBe(3); received.Items.Count.ShouldBe(2); received.HasNextPage.ShouldBeTrue();
        var next = (await reader.GetFromJsonAsync<MobileFlightSharePageDto>($"{feeds}?direction=received&page=2&pageSize=2"))!;
        received.Items.Concat(next.Items).Select(item => item.Id).Distinct().Count().ShouldBe(3);
        var own = (await publisher.GetFromJsonAsync<MobileFlightSharePageDto>($"{feeds}?direction=received"))!; own.Items.ShouldBeEmpty();
        var sent = (await publisher.GetFromJsonAsync<MobileFlightSharePageDto>($"{feeds}?direction=sent"))!;
        sent.Items.Count.ShouldBe(3); sent.Items.Select(item => item.CreatedAtUtc).ShouldBe(sent.Items.Select(item => item.CreatedAtUtc).OrderDescending());
        var otherReceived = (await otherReader.GetFromJsonAsync<MobileFlightSharePageDto>(feeds))!;
        otherReceived.Items.ShouldHaveSingleItem().IsStationWide.ShouldBeTrue();
        (await reader.GetFromJsonAsync<MobileFlightSharePageDto>($"{feeds}?search=NO-MATCH"))!.Items.ShouldBeEmpty();
        var authorAfter = (await publisher.GetFromJsonAsync<FlightDetailDto>(detailUrl))!;
        var recipientAfter = (await reader.GetFromJsonAsync<FlightDetailDto>(detailUrl))!;
        authorAfter.RowVersion.ShouldBe(authorBefore.RowVersion); authorAfter.UpdatedAtUtc.ShouldBe(authorBefore.UpdatedAtUtc);
        authorAfter.AssignedEmployees.ShouldHaveSingleItem().StaffMemberId.ShouldBe(author.StaffId);
        recipientAfter.RowVersion.ShouldBe(recipientBefore.RowVersion);
        recipientAfter.CanSaveArrival.ShouldBe(recipientBefore.CanSaveArrival);
        recipientAfter.CanAddServices.ShouldBe(recipientBefore.CanAddServices); recipientAfter.CanAddTasks.ShouldBe(recipientBefore.CanAddTasks);
        recipientAfter.CanSaveReturnsToRamp.ShouldBe(recipientBefore.CanSaveReturnsToRamp); recipientAfter.CanShare.ShouldBeFalse();
        var assigned = (await reader.GetFromJsonAsync<MobileFlightPageDto>($"{Base}/flights/mobile?category=MyFlights"))!;
        assigned.Items.ShouldBeEmpty();
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        (await db.FlightShares.CountAsync(item => item.FlightId == flightId)).ShouldBe(3);
        var shareEvents = (await db.OutboxMessages.Where(item => item.Type.StartsWith("Operations.Contracts.FlightShared,")).ToListAsync())
            .Select(item => JsonSerializer.Deserialize<FlightShared>(item.Content)!).Where(item => item.FlightId == flightId).ToList();
        // Both concurrent attempts commit just one direct-recipient event with the immutable post.
        shareEvents.Where(item => item.ShareId == id).ShouldHaveSingleItem().StaffMemberId.ShouldBe(recipient.StaffId);
        shareEvents.ShouldNotContain(item => item.StaffMemberId == author.StaffId);
        // Only the earlier Arrival created a work interaction; posting is not operational work.
        (await db.FlightUserActivities.CountAsync(item => item.FlightId == flightId)).ShouldBe(1);
    }

    [Fact]
    public async Task Share_routes_reject_wrong_publishers_recipients_and_station_scope_and_allow_exact_canceled_replay()
    {
        using var admin = await factory.CreateAuthenticatedAdminClientAsync(); var refs = await SetupMasterDataAsync(admin);
        var author = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view", "operations.flights.operate"]);
        var recipient = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view", "operations.flights.operate"]);
        var viewOnly = await CreateStaffLoginAsync(admin, refs, ["operations.flights.view"]);
        var otherRefs = await SetupMasterDataAsync(admin);
        var elsewhere = await CreateStaffLoginAsync(admin, otherRefs, ["operations.flights.view", "operations.flights.operate"]);
        using var publisher = author.Client; using var receiver = recipient.Client; using var viewer = viewOnly.Client; using var outsider = elsewhere.Client;
        var flightId = await ScheduleFlightAsync(admin, refs.StationId, refs, "9801", [author.StaffId, viewOnly.StaffId]);
        var detailUrl = $"{Base}/flights/mobile/{flightId}"; var id = Guid.NewGuid(); var path = $"{detailUrl}/shares/{id}";
        var input = new PublishFlightShareRequest(null, [recipient.StaffId]);
        using var denied = await receiver.PutAsJsonAsync(path, input); denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var permissionDenied = await viewer.PutAsJsonAsync(path, input); permissionDenied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var wrongStation = await outsider.PutAsJsonAsync(path, input); wrongStation.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var wrongRecipients = await publisher.PutAsJsonAsync(path, new PublishFlightShareRequest(null, [elsewhere.StaffId]));
        wrongRecipients.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var invalidMessage = await publisher.PutAsJsonAsync(path, new PublishFlightShareRequest(new string('x', 2001)));
        invalidMessage.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var accepted = await publisher.PutAsJsonAsync(path, input); accepted.StatusCode.ShouldBe(HttpStatusCode.OK);
        var otherFeed = (await outsider.GetFromJsonAsync<MobileFlightSharePageDto>($"{Base}/flights/mobile/shares"))!; otherFeed.Items.ShouldBeEmpty();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
            var flight = await db.Flights.Include(item => item.CancellationSubmissions).Include(item => item.ArrivalSubmissions)
                .Include(item => item.DepartureSubmissions).SingleAsync(item => item.Id == flightId);
            flight.SubmitCancellation(new(author.StaffId, "Author", "E1"), CancellationDetails.Create(DateTimeOffset.UtcNow, null).Value,
                Guid.NewGuid(), DateTimeOffset.UtcNow).IsSuccess.ShouldBeTrue();
            await db.SaveChangesAsync();
        }
        using var replay = await publisher.PutAsJsonAsync(path, input); replay.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var canceled = await publisher.PutAsJsonAsync($"{detailUrl}/shares/{Guid.NewGuid()}", input);
        canceled.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await publisher.GetFromJsonAsync<FlightDetailDto>(detailUrl))!.CanShare.ShouldBeFalse();
        using var invalidDirection = await receiver.GetAsync($"{Base}/flights/mobile/shares?direction=other");
        invalidDirection.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
