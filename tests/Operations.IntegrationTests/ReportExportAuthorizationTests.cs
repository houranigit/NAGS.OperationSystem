using System.Net;
using System.Net.Http.Json;
using Shouldly;

namespace Operations.IntegrationTests;

public sealed class ReportExportAuthorizationTests(OperationsApiFactory factory)
    : IClassFixture<OperationsApiFactory>
{
    [Fact]
    public async Task Report_exports_require_their_corresponding_page_view_permission()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var viewer = await CreateViewerAsync(
            admin,
            [
                "operations.dashboard.view",
                "operations.dashboard.view-analytics",
                "operations.flights.export"
            ]);

        var flights = await viewer.GetAsync(
            $"{OperationsApiFactory.Base}/flights/export?format=csv");
        var dashboard = await viewer.GetAsync(
            $"{OperationsApiFactory.Base}/analytics-dashboard/flights/export?format=csv");
        var dashboardWorkOrder = await viewer.GetAsync(
            $"{OperationsApiFactory.Base}/analytics-dashboard/flights/{Guid.NewGuid()}/work-orders/approved/pdf");

        flights.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        dashboard.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        dashboardWorkOrder.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Report_exports_succeed_when_page_view_and_export_permissions_are_present()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var viewer = await CreateViewerAsync(
            admin,
            [
                "operations.flights.view",
                "operations.flights.export",
                "operations.dashboard.view-analytics",
                "operations.dashboard.export"
            ]);

        var flights = await viewer.GetAsync(
            $"{OperationsApiFactory.Base}/flights/export?format=csv");
        var dashboard = await viewer.GetAsync(
            $"{OperationsApiFactory.Base}/analytics-dashboard/flights/export?format=csv");
        var flightId = Guid.NewGuid();
        var dashboardWorkOrder = await viewer.GetAsync(
            $"{OperationsApiFactory.Base}/analytics-dashboard/flights/{flightId}/work-orders/approved/pdf");
        var genericWorkOrder = await viewer.GetAsync(
            $"{OperationsApiFactory.Base}/flights/{flightId}/work-orders/approved/pdf");

        flights.StatusCode.ShouldBe(HttpStatusCode.OK, await flights.Content.ReadAsStringAsync());
        dashboard.StatusCode.ShouldBe(HttpStatusCode.OK, await dashboard.Content.ReadAsStringAsync());
        dashboardWorkOrder.StatusCode.ShouldBe(
            HttpStatusCode.NotFound,
            await dashboardWorkOrder.Content.ReadAsStringAsync());
        genericWorkOrder.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Dashboard_work_order_print_requires_analytics_view_in_addition_to_export()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var viewer = await CreateViewerAsync(
            admin,
            [
                "operations.flights.view",
                "operations.dashboard.export"
            ]);

        var response = await viewer.GetAsync(
            $"{OperationsApiFactory.Base}/analytics-dashboard/flights/{Guid.NewGuid()}/work-orders/approved/pdf");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Legacy_work_order_print_remains_available_to_work_order_viewers_only()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var viewer = await CreateViewerAsync(
            admin,
            ["operations.work-orders.view"]);
        var flightId = Guid.NewGuid();

        var legacyResponse = await viewer.GetAsync(
            $"{OperationsApiFactory.Base}/flights/{flightId}/work-orders/approved/pdf");
        var dashboardResponse = await viewer.GetAsync(
            $"{OperationsApiFactory.Base}/analytics-dashboard/flights/{flightId}/work-orders/approved/pdf");

        legacyResponse.StatusCode.ShouldBe(
            HttpStatusCode.NotFound,
            await legacyResponse.Content.ReadAsStringAsync());
        dashboardResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Work_order_print_validates_the_optional_display_time_zone()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var flightId = Guid.NewGuid();

        var invalid = await admin.GetAsync(
            $"{OperationsApiFactory.Base}/flights/{flightId}/work-orders/approved/pdf?timeZoneId=Moon%2FBase");
        var utc = await admin.GetAsync(
            $"{OperationsApiFactory.Base}/flights/{flightId}/work-orders/approved/pdf?timeZoneId=UTC");
        var dstZone = await admin.GetAsync(
            $"{OperationsApiFactory.Base}/flights/{flightId}/work-orders/approved/pdf?timeZoneId=America%2FNew_York");

        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await invalid.Content.ReadAsStringAsync())
            .ShouldContain("Operations.WorkOrder.PrintTimeZoneInvalid");
        utc.StatusCode.ShouldBe(HttpStatusCode.NotFound, await utc.Content.ReadAsStringAsync());
        dstZone.StatusCode.ShouldBe(
            HttpStatusCode.NotFound,
            await dstZone.Content.ReadAsStringAsync());
    }

    private async Task<HttpClient> CreateViewerAsync(
        HttpClient admin,
        IReadOnlyList<string> permissions)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var roleResponse = await admin.PostAsJsonAsync(
            $"{OperationsApiFactory.IdentityBase}/roles",
            new
            {
                name = $"Viewer export role {suffix}",
                description = (string?)null,
                compatibleUserType = "ViewerOnly",
                permissions
            });
        roleResponse.StatusCode.ShouldBe(
            HttpStatusCode.Created,
            await roleResponse.Content.ReadAsStringAsync());
        var roleId = await roleResponse.Content.ReadFromJsonAsync<Guid>();

        var email = $"viewer-export-{suffix}@example.com";
        var inviteResponse = await admin.PostAsJsonAsync(
            $"{OperationsApiFactory.IdentityBase}/users/invite",
            new
            {
                email,
                displayName = $"Viewer Export {suffix[..8]}",
                roleId
            });
        inviteResponse.StatusCode.ShouldBe(
            HttpStatusCode.Created,
            await inviteResponse.Content.ReadAsStringAsync());

        var invitationToken = await factory.GetInvitationTokenAsync(email);
        invitationToken.ShouldNotBeNull();

        const string password = "ViewerPass#12345";
        var activation = await admin.PostAsJsonAsync(
            $"{OperationsApiFactory.IdentityBase}/auth/activate",
            new
            {
                email,
                invitationToken,
                newPassword = password
            });
        activation.StatusCode.ShouldBe(
            HttpStatusCode.NoContent,
            await activation.Content.ReadAsStringAsync());

        return await factory.CreateAuthenticatedClientAsync(email, password);
    }
}
