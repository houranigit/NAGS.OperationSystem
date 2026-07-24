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

        flights.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        dashboard.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
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

        flights.StatusCode.ShouldBe(HttpStatusCode.OK, await flights.Content.ReadAsStringAsync());
        dashboard.StatusCode.ShouldBe(HttpStatusCode.OK, await dashboard.Content.ReadAsStringAsync());
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
