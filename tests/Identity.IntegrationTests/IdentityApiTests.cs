using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using BuildingBlocks.Application.Messaging;
using Identity.Application.Abstractions;
using Identity.Domain.Roles;
using Identity.Domain.Sessions;
using Identity.Domain.Users;
using Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;

namespace Identity.IntegrationTests;

public class IdentityApiTests(IdentityApiFactory factory) : IClassFixture<IdentityApiFactory>
{
    private const string Base = IdentityApiTestData.Base;

    private sealed record TokenResponse(string AccessToken, DateTimeOffset ExpiresAtUtc);
    private sealed record InvitedResponse(Guid Id, string Email, string DeliveryStatus);
    private sealed record PagedList<T>(List<T> Items, int Page, int PageSize, long TotalCount);
    private sealed record RoleItem(Guid Id, string Name);
    private sealed record RoleDetailItem(Guid Id, string Name, string? Description, List<string> Permissions);
    private sealed record UserDetailItem(
        Guid Id,
        Guid RoleId,
        string UserType,
        Guid? ExternalReferenceId,
        string PortalSource,
        string RowVersion);
    private sealed record MeResponse(
        Guid Id,
        Guid RoleId,
        string UserType,
        Guid? ExternalReferenceId,
        string PortalSource,
        List<string> Permissions);

    [Fact]
    public async Task Login_with_valid_admin_credentials_returns_access_token()
    {
        var client = factory.CreateClient();

        var token = await IdentityApiTestData.LoginAsAdminAsync(client, factory);

        token.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Login_with_wrong_password_returns_401()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync($"{Base}/auth/login",
            new { email = IdentityApiFactory.AdminEmail, password = "wrong" });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Roles_without_token_returns_401()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync($"{Base}/roles");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Admin_can_create_and_list_roles()
    {
        var client = await IdentityApiTestData.CreateAuthenticatedAdminClientAsync(factory);

        var roleName = $"Role-{Guid.NewGuid():N}";
        var create = await client.PostAsJsonAsync($"{Base}/roles",
            new { name = roleName, description = "test", compatibleUserType = "SystemAdministrator", permissions = new[] { "identity.roles.view" } });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);

        var list = await client.GetFromJsonAsync<PagedList<RoleItem>>($"{Base}/roles?pageSize=100");
        list!.Items.ShouldContain(r => r.Name == roleName);
    }

    [Fact]
    public async Task Admin_can_atomically_update_role_details_and_permissions()
    {
        var client = await IdentityApiTestData.CreateAuthenticatedAdminClientAsync(factory);

        var originalName = $"RoleEditor-{Guid.NewGuid():N}";
        var create = await client.PostAsJsonAsync($"{Base}/roles",
            new
            {
                name = originalName,
                description = "before",
                compatibleUserType = "SystemAdministrator",
                permissions = new[] { "identity.roles.view" }
            });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var roleId = await create.Content.ReadFromJsonAsync<Guid>();

        var updatedName = $"RoleEditorUpdated-{Guid.NewGuid():N}";
        var update = await client.PutAsJsonAsync($"{Base}/roles/{roleId}/editor",
            new
            {
                name = updatedName,
                description = "after",
                permissions = new[] { "identity.roles.view", "identity.users.view" }
            });

        update.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var detail = await client.GetFromJsonAsync<RoleDetailItem>($"{Base}/roles/{roleId}");
        detail.ShouldNotBeNull();
        detail!.Name.ShouldBe(updatedName);
        detail.Description.ShouldBe("after");
        detail.Permissions.ShouldBe(["identity.roles.view", "identity.users.view"], ignoreOrder: true);
    }

    [Fact]
    public async Task Creating_role_without_compatible_user_type_returns_400()
    {
        var client = await IdentityApiTestData.CreateAuthenticatedAdminClientAsync(factory);

        var roleName = $"MissingType-{Guid.NewGuid():N}";
        var create = await client.PostAsJsonAsync($"{Base}/roles",
            new { name = roleName, description = "test", permissions = new[] { "identity.roles.view" } });

        create.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Inviting_direct_admin_can_select_admin_compatible_role()
    {
        var client = await IdentityApiTestData.CreateAuthenticatedAdminClientAsync(factory);

        var roleName = $"InviteRole-{Guid.NewGuid():N}";
        var createRole = await client.PostAsJsonAsync($"{Base}/roles",
            new { name = roleName, description = (string?)null, compatibleUserType = "SystemAdministrator", permissions = Array.Empty<string>() });
        createRole.StatusCode.ShouldBe(HttpStatusCode.Created);
        var roleId = await createRole.Content.ReadFromJsonAsync<Guid>();

        var email = $"selected-role-{Guid.NewGuid():N}@nags.sa";
        var invite = await client.PostAsJsonAsync($"{Base}/users/invite",
            new { email, displayName = "Selected Role User", roleId });
        invite.StatusCode.ShouldBe(HttpStatusCode.Created);
        var invited = await invite.Content.ReadFromJsonAsync<InvitedResponse>();
        invited.ShouldNotBeNull();

        var detail = await client.GetFromJsonAsync<UserDetailItem>($"{Base}/users/{invited!.Id}");
        detail!.RoleId.ShouldBe(roleId);
        detail.UserType.ShouldBe("SystemAdministrator");
    }

    [Fact]
    public async Task Viewer_role_can_be_invited_directly_activated_and_logged_in()
    {
        var admin = await IdentityApiTestData.CreateAuthenticatedAdminClientAsync(factory);
        var roleName = $"ViewerRole-{Guid.NewGuid():N}";
        var createRole = await admin.PostAsJsonAsync($"{Base}/roles",
            new
            {
                name = roleName,
                description = "CEO dashboard viewer",
                compatibleUserType = "ViewerOnly",
                permissions = new[] { "operations.dashboard.view" }
            });
        createRole.StatusCode.ShouldBe(HttpStatusCode.Created);
        var roleId = await createRole.Content.ReadFromJsonAsync<Guid>();

        var email = $"viewer-{Guid.NewGuid():N}@nags.sa";
        var invite = await admin.PostAsJsonAsync($"{Base}/users/invite",
            new { email, displayName = "CEO Viewer", roleId });
        invite.StatusCode.ShouldBe(HttpStatusCode.Created);
        var invited = await invite.Content.ReadFromJsonAsync<InvitedResponse>();
        invited.ShouldNotBeNull();

        var detail = await admin.GetFromJsonAsync<UserDetailItem>($"{Base}/users/{invited!.Id}");
        detail.ShouldNotBeNull();
        detail!.RoleId.ShouldBe(roleId);
        detail.UserType.ShouldBe("ViewerOnly");
        detail.ExternalReferenceId.ShouldBeNull();
        detail.PortalSource.ShouldBe("Direct");

        var invitationToken = await factory.GetInvitationTokenAsync(email);
        invitationToken.ShouldNotBeNull();
        var activate = await admin.PostAsJsonAsync($"{Base}/auth/activate",
            new { email, invitationToken, newPassword = "Viewer#12345" });
        activate.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var viewer = factory.CreateClient();
        var login = await viewer.PostAsJsonAsync($"{Base}/auth/login",
            new { email, password = "Viewer#12345" });
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        var tokens = await login.Content.ReadFromJsonAsync<TokenResponse>();
        tokens.ShouldNotBeNull();
        viewer.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);

        var me = await viewer.GetFromJsonAsync<MeResponse>($"{Base}/me");
        me.ShouldNotBeNull();
        me!.Id.ShouldBe(invited.Id);
        me.RoleId.ShouldBe(roleId);
        me.UserType.ShouldBe("ViewerOnly");
        me.ExternalReferenceId.ShouldBeNull();
        me.PortalSource.ShouldBe("Direct");
        me.Permissions.ShouldBe(["operations.dashboard.view"]);
    }

    [Fact]
    public async Task Invited_direct_account_transition_replaces_the_activation_credential()
    {
        var admin = await IdentityApiTestData.CreateAuthenticatedAdminClientAsync(factory);
        var viewerRoleResponse = await admin.PostAsJsonAsync($"{Base}/roles",
            new
            {
                name = $"InvitedViewer-{Guid.NewGuid():N}",
                description = "Invitation transition viewer",
                compatibleUserType = "ViewerOnly",
                permissions = new[] { "operations.dashboard.view" }
            });
        viewerRoleResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var viewerRoleId = await viewerRoleResponse.Content.ReadFromJsonAsync<Guid>();
        var adminRoleId = await CreateAdministratorRoleAsync(
            admin,
            $"InvitedAdmin-{Guid.NewGuid():N}");

        var email = $"invited-transition-{Guid.NewGuid():N}@nags.sa";
        var invite = await admin.PostAsJsonAsync($"{Base}/users/invite",
            new { email, displayName = "Invited Transition", roleId = viewerRoleId });
        invite.StatusCode.ShouldBe(HttpStatusCode.Created);
        var invited = await invite.Content.ReadFromJsonAsync<InvitedResponse>();
        var originalToken = await factory.GetInvitationTokenAsync(email);
        originalToken.ShouldNotBeNullOrWhiteSpace();

        var before = await admin.GetFromJsonAsync<UserDetailItem>(
            $"{Base}/users/{invited!.Id}");
        var change = await SendAccessChangeAsync(
            admin,
            invited.Id,
            adminRoleId,
            before!.RowVersion);
        change.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var replacementToken = await factory.GetInvitationTokenAsync(email);
        replacementToken.ShouldNotBeNullOrWhiteSpace();
        replacementToken.ShouldNotBe(originalToken);

        var oldActivation = await admin.PostAsJsonAsync($"{Base}/auth/activate",
            new
            {
                email,
                invitationToken = originalToken,
                newPassword = "Invited#12345"
            });
        oldActivation.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var replacementActivation = await admin.PostAsJsonAsync($"{Base}/auth/activate",
            new
            {
                email,
                invitationToken = replacementToken,
                newPassword = "Invited#12345"
            });
        replacementActivation.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var promoted = factory.CreateClient();
        var login = await promoted.PostAsJsonAsync($"{Base}/auth/login",
            new { email, password = "Invited#12345" });
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        var tokens = await login.Content.ReadFromJsonAsync<TokenResponse>();
        promoted.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
        var me = await promoted.GetFromJsonAsync<MeResponse>($"{Base}/me");
        me!.UserType.ShouldBe("SystemAdministrator");
        me.RoleId.ShouldBe(adminRoleId);
    }

    [Fact]
    public async Task Direct_account_can_transition_admin_to_viewer_and_back_with_immediate_session_invalidation()
    {
        var admin = await IdentityApiTestData.CreateAuthenticatedAdminClientAsync(factory);

        var adminRoleResponse = await admin.PostAsJsonAsync($"{Base}/roles",
            new
            {
                name = $"AccessAdmin-{Guid.NewGuid():N}",
                description = "Direct transition test administrator",
                compatibleUserType = "SystemAdministrator",
                permissions = new[] { "identity.users.view", "identity.users.update" }
            });
        adminRoleResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var adminRoleId = await adminRoleResponse.Content.ReadFromJsonAsync<Guid>();

        var viewerRoleResponse = await admin.PostAsJsonAsync($"{Base}/roles",
            new
            {
                name = $"AccessViewer-{Guid.NewGuid():N}",
                description = "Direct transition test viewer",
                compatibleUserType = "ViewerOnly",
                permissions = new[] { "operations.dashboard.view" }
            });
        viewerRoleResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var viewerRoleId = await viewerRoleResponse.Content.ReadFromJsonAsync<Guid>();

        var email = $"access-transition-{Guid.NewGuid():N}@nags.sa";
        const string password = "Transition#12345";
        var invite = await admin.PostAsJsonAsync($"{Base}/users/invite",
            new { email, displayName = "Access Transition User", roleId = adminRoleId });
        invite.StatusCode.ShouldBe(HttpStatusCode.Created);
        var invited = await invite.Content.ReadFromJsonAsync<InvitedResponse>();
        invited.ShouldNotBeNull();

        var invitationToken = await factory.GetInvitationTokenAsync(email);
        invitationToken.ShouldNotBeNull();
        var activate = await admin.PostAsJsonAsync($"{Base}/auth/activate",
            new { email, invitationToken, newPassword = password });
        activate.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var target = factory.CreateClient();
        var initialLogin = await target.PostAsJsonAsync($"{Base}/auth/login", new { email, password });
        initialLogin.StatusCode.ShouldBe(HttpStatusCode.OK);
        var initialToken = await initialLogin.Content.ReadFromJsonAsync<TokenResponse>();
        target.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", initialToken!.AccessToken);

        // Prime live token validation before the downgrade. The next call must still fail
        // immediately; a positive validation result may not survive an access change.
        (await target.GetAsync($"{Base}/me")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var beforeDemotion = await admin.GetFromJsonAsync<UserDetailItem>($"{Base}/users/{invited!.Id}");
        beforeDemotion.ShouldNotBeNull();
        var demote = await SendAccessChangeAsync(
            admin,
            invited.Id,
            viewerRoleId,
            beforeDemotion!.RowVersion);
        demote.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var afterDemotion = await admin.GetFromJsonAsync<UserDetailItem>($"{Base}/users/{invited.Id}");
        afterDemotion!.RoleId.ShouldBe(viewerRoleId);
        afterDemotion.UserType.ShouldBe("ViewerOnly");
        afterDemotion.ExternalReferenceId.ShouldBeNull();
        afterDemotion.PortalSource.ShouldBe("Direct");

        // Both the warmed access token and its refresh session are invalid immediately.
        (await target.PutAsJsonAsync(
            $"{Base}/users/{invited.Id}",
            new { displayName = "Old administrator token must fail" }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await target.PostAsync($"{Base}/auth/refresh", content: null))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var viewer = factory.CreateClient();
        var viewerLogin = await viewer.PostAsJsonAsync($"{Base}/auth/login", new { email, password });
        viewerLogin.StatusCode.ShouldBe(HttpStatusCode.OK);
        var viewerToken = await viewerLogin.Content.ReadFromJsonAsync<TokenResponse>();
        viewer.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", viewerToken!.AccessToken);
        var viewerMe = await viewer.GetFromJsonAsync<MeResponse>($"{Base}/me");
        viewerMe!.RoleId.ShouldBe(viewerRoleId);
        viewerMe.UserType.ShouldBe("ViewerOnly");
        viewerMe.Permissions.ShouldBe(["operations.dashboard.view"]);

        // Successful sign-in updates LastLoginAtUtc and therefore the user's rowversion. Reload
        // before making the next administrative access decision.
        var beforePromotion = await admin.GetFromJsonAsync<UserDetailItem>(
            $"{Base}/users/{invited.Id}");
        beforePromotion.ShouldNotBeNull();
        var promote = await SendAccessChangeAsync(
            admin,
            invited.Id,
            adminRoleId,
            beforePromotion!.RowVersion);
        promote.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await viewer.GetAsync($"{Base}/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var promoted = factory.CreateClient();
        var promotedLogin = await promoted.PostAsJsonAsync($"{Base}/auth/login", new { email, password });
        promotedLogin.StatusCode.ShouldBe(HttpStatusCode.OK);
        var promotedToken = await promotedLogin.Content.ReadFromJsonAsync<TokenResponse>();
        promoted.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", promotedToken!.AccessToken);
        var promotedMe = await promoted.GetFromJsonAsync<MeResponse>($"{Base}/me");
        promotedMe!.RoleId.ShouldBe(adminRoleId);
        promotedMe.UserType.ShouldBe("SystemAdministrator");
        promotedMe.Permissions.ShouldBe(["identity.users.view", "identity.users.update"], ignoreOrder: true);
    }

    [Fact]
    public async Task Access_change_rejects_stale_rowversion_but_exact_retry_is_idempotent()
    {
        var admin = await IdentityApiTestData.CreateAuthenticatedAdminClientAsync(factory);
        var firstRole = await CreateAdministratorRoleAsync(admin, $"FirstAccess-{Guid.NewGuid():N}");
        var secondRole = await CreateAdministratorRoleAsync(admin, $"SecondAccess-{Guid.NewGuid():N}");
        var thirdRole = await CreateAdministratorRoleAsync(admin, $"ThirdAccess-{Guid.NewGuid():N}");

        var email = $"stale-access-{Guid.NewGuid():N}@nags.sa";
        var invite = await admin.PostAsJsonAsync($"{Base}/users/invite",
            new { email, displayName = "Stale Access User", roleId = firstRole });
        var invited = await invite.Content.ReadFromJsonAsync<InvitedResponse>();
        var before = await admin.GetFromJsonAsync<UserDetailItem>($"{Base}/users/{invited!.Id}");

        var missingPrecondition = await admin.PutAsJsonAsync(
            $"{Base}/users/{invited.Id}/role",
            new { roleId = secondRole });
        missingPrecondition.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var firstChange = await SendAccessChangeAsync(admin, invited.Id, secondRole, before!.RowVersion);
        firstChange.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Repeating the already-applied target is a successful no-op even with the original token.
        var retry = await SendAccessChangeAsync(admin, invited.Id, secondRole, before.RowVersion);
        retry.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // A different decision made from the same stale screen must not overwrite the first.
        var staleChange = await SendAccessChangeAsync(admin, invited.Id, thirdRole, before.RowVersion);
        staleChange.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var after = await admin.GetFromJsonAsync<UserDetailItem>($"{Base}/users/{invited.Id}");
        after!.RoleId.ShouldBe(secondRole);
        after.UserType.ShouldBe("SystemAdministrator");
    }

    [Fact]
    public async Task Concurrent_system_administrators_cannot_demote_each_other_with_stale_live_authority()
    {
        var bootstrapAdmin = await IdentityApiTestData.CreateAuthenticatedAdminClientAsync(factory);
        var viewerRoleResponse = await bootstrapAdmin.PostAsJsonAsync($"{Base}/roles",
            new
            {
                name = $"RaceViewer-{Guid.NewGuid():N}",
                description = "Concurrent access transition target",
                compatibleUserType = "ViewerOnly",
                permissions = new[] { "operations.dashboard.view" }
            });
        viewerRoleResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var viewerRoleId = await viewerRoleResponse.Content.ReadFromJsonAsync<Guid>();

        var raceBarrier = new AccessManagementRaceBarrier(requiredArrivals: 2);
        await using var racingApp = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.Replace(ServiceDescriptor.Scoped<IIdentityDbContext>(serviceProvider =>
                    new BarrierIdentityDbContext(
                        serviceProvider.GetRequiredService<IdentityDbContext>(),
                        raceBarrier)))));

        var first = await InviteActivateAndLoginSystemAdministratorAsync(
            bootstrapAdmin,
            "First Race Administrator",
            racingApp.CreateClient);
        var second = await InviteActivateAndLoginSystemAdministratorAsync(
            bootstrapAdmin,
            "Second Race Administrator",
            racingApp.CreateClient);

        // Sign-in changes each user's rowversion, so capture both preconditions only after both
        // actors hold their final access tokens.
        var firstBefore = await bootstrapAdmin.GetFromJsonAsync<UserDetailItem>(
            $"{Base}/users/{first.UserId}");
        var secondBefore = await bootstrapAdmin.GetFromJsonAsync<UserDetailItem>(
            $"{Base}/users/{second.UserId}");
        firstBefore.ShouldNotBeNull();
        secondBefore.ShouldNotBeNull();
        firstBefore!.RoleId.ShouldBe(secondBefore!.RoleId);
        firstBefore.UserType.ShouldBe("SystemAdministrator");
        secondBefore.UserType.ShouldBe("SystemAdministrator");
        var protectedSystemRoleId = firstBefore.RoleId;

        // Hold both handlers immediately before the real SQL access-management transaction. This
        // proves both requests have already passed endpoint authorization before either can win the
        // database lock and demote the other actor.
        var firstRequest =
            SendAccessChangeAsync(
                first.Client,
                second.UserId,
                viewerRoleId,
                secondBefore.RowVersion);
        var secondRequest =
            SendAccessChangeAsync(
                second.Client,
                first.UserId,
                viewerRoleId,
                firstBefore.RowVersion);

        try
        {
            await raceBarrier.AllArrived.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            raceBarrier.Release();
        }

        // The winner demotes the other actor. Once the loser acquires the serialized lock, its
        // live role must be revalidated and the now-stale administrator authority must be rejected.
        var responses = await Task.WhenAll(firstRequest, secondRequest);

        responses.Count(response => response.StatusCode == HttpStatusCode.NoContent)
            .ShouldBe(1);
        responses.Single(response => response.StatusCode != HttpStatusCode.NoContent)
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var firstAfter = await bootstrapAdmin.GetFromJsonAsync<UserDetailItem>(
            $"{Base}/users/{first.UserId}");
        var secondAfter = await bootstrapAdmin.GetFromJsonAsync<UserDetailItem>(
            $"{Base}/users/{second.UserId}");
        var finalUsers = new[] { firstAfter!, secondAfter! };

        finalUsers.Count(user =>
                user.UserType == "SystemAdministrator" &&
                user.RoleId == protectedSystemRoleId)
            .ShouldBe(1);
        finalUsers.Count(user =>
                user.UserType == "ViewerOnly" &&
                user.RoleId == viewerRoleId)
            .ShouldBe(1);
    }

    [Fact]
    public async Task Viewer_role_without_a_portal_page_is_rejected()
    {
        var client = await IdentityApiTestData.CreateAuthenticatedAdminClientAsync(factory);

        var create = await client.PostAsJsonAsync($"{Base}/roles",
            new
            {
                name = $"EmptyViewerRole-{Guid.NewGuid():N}",
                description = (string?)null,
                compatibleUserType = "ViewerOnly",
                permissions = new[] { "identity.sessions.view", "operations.dashboard.export" }
            });

        create.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Inviting_direct_admin_rejects_non_admin_compatible_role()
    {
        var client = await IdentityApiTestData.CreateAuthenticatedAdminClientAsync(factory);

        var roleName = $"StationRole-{Guid.NewGuid():N}";
        var createRole = await client.PostAsJsonAsync($"{Base}/roles",
            new { name = roleName, description = (string?)null, compatibleUserType = "StationStaff", permissions = Array.Empty<string>() });
        createRole.StatusCode.ShouldBe(HttpStatusCode.Created);
        var roleId = await createRole.Content.ReadFromJsonAsync<Guid>();

        var invite = await client.PostAsJsonAsync($"{Base}/users/invite",
            new { email = $"bad-role-{Guid.NewGuid():N}@nags.sa", displayName = "Bad Role User", roleId });

        invite.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task User_without_required_permission_gets_403()
    {
        var client = await IdentityApiTestData.CreateAuthenticatedAdminClientAsync(factory);

        // A role with no permissions (admin-compatible, so it can be assigned to a created admin).
        var roleName = $"NoPerms-{Guid.NewGuid():N}";
        var createRole = await client.PostAsJsonAsync($"{Base}/roles",
            new { name = roleName, description = (string?)null, compatibleUserType = "SystemAdministrator", permissions = Array.Empty<string>() });
        var roleId = await createRole.Content.ReadFromJsonAsync<Guid>();

        // Invite directly into the empty administrator role so we can prove permission enforcement
        // without giving the account full access first.
        var email = $"limited-{Guid.NewGuid():N}@nags.sa";
        var invite = await client.PostAsJsonAsync($"{Base}/users/invite",
            new { email, displayName = "Limited User", roleId });
        var invited = await invite.Content.ReadFromJsonAsync<InvitedResponse>();
        invited.ShouldNotBeNull();

        var invitationToken = await factory.GetInvitationTokenAsync(email);
        invitationToken.ShouldNotBeNull();

        var activate = await client.PostAsJsonAsync($"{Base}/auth/activate",
            new { email, invitationToken, newPassword = "Limited#12345" });
        activate.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Log in as the now-limited user and attempt a permission-gated endpoint.
        var limitedClient = factory.CreateClient();
        var login = await limitedClient.PostAsJsonAsync($"{Base}/auth/login",
            new { email, password = "Limited#12345" });
        var limitedToken = (await login.Content.ReadFromJsonAsync<TokenResponse>())!.AccessToken;
        limitedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", limitedToken);

        var forbidden = await limitedClient.GetAsync($"{Base}/roles");
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Released_login_email_can_activate_and_login_as_new_invited_user()
    {
        var client = await IdentityApiTestData.CreateAuthenticatedAdminClientAsync(factory);
        var email = $"released-login-{Guid.NewGuid():N}@nags.sa";

        var firstInvite = await client.PostAsJsonAsync($"{Base}/users/invite",
            new { email, displayName = "Released Login Original" });
        firstInvite.StatusCode.ShouldBe(HttpStatusCode.Created);
        var first = await firstInvite.Content.ReadFromJsonAsync<InvitedResponse>();
        first.ShouldNotBeNull();

        var firstToken = await factory.GetInvitationTokenAsync(email);
        firstToken.ShouldNotBeNull();

        var firstActivate = await client.PostAsJsonAsync($"{Base}/auth/activate",
            new { email, invitationToken = firstToken, newPassword = "Original#12345" });
        firstActivate.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await ReleaseLoginEmailAsync(first!.Id);

        var secondInvite = await client.PostAsJsonAsync($"{Base}/users/invite",
            new { email, displayName = "Released Login Replacement" });
        secondInvite.StatusCode.ShouldBe(HttpStatusCode.Created);
        var second = await secondInvite.Content.ReadFromJsonAsync<InvitedResponse>();
        second.ShouldNotBeNull();
        second!.Id.ShouldNotBe(first.Id);

        var secondToken = await factory.GetInvitationTokenAsync(email);
        secondToken.ShouldNotBeNull();

        var secondActivate = await client.PostAsJsonAsync($"{Base}/auth/activate",
            new { email, invitationToken = secondToken, newPassword = "Replacement#12345" });
        secondActivate.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var loginClient = factory.CreateClient();
        var login = await loginClient.PostAsJsonAsync($"{Base}/auth/login",
            new { email, password = "Replacement#12345" });

        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        var token = await login.Content.ReadFromJsonAsync<TokenResponse>();
        token!.AccessToken.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Weak_activation_password_is_rejected_without_consuming_invitation()
    {
        var client = await IdentityApiTestData.CreateAuthenticatedAdminClientAsync(factory);
        var email = $"weak-activation-{Guid.NewGuid():N}@nags.sa";

        var invite = await client.PostAsJsonAsync($"{Base}/users/invite",
            new { email, displayName = "Weak Activation User" });
        invite.StatusCode.ShouldBe(HttpStatusCode.Created);

        var invitationToken = await factory.GetInvitationTokenAsync(email);
        invitationToken.ShouldNotBeNull();

        var weakActivate = await client.PostAsJsonAsync($"{Base}/auth/activate",
            new { email, invitationToken, newPassword = "password123!" });
        weakActivate.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var strongActivate = await client.PostAsJsonAsync($"{Base}/auth/activate",
            new { email, invitationToken, newPassword = "Strong#12345" });
        strongActivate.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var loginClient = factory.CreateClient();
        var login = await loginClient.PostAsJsonAsync($"{Base}/auth/login",
            new { email, password = "Strong#12345" });
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Invite_keeps_user_when_invitation_delivery_fails()
    {
        await using var failingApp = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.Replace(ServiceDescriptor.Scoped<IInvitationNotifier, ThrowingInvitationNotifier>())));

        var client = failingApp.CreateClient();
        var token = await IdentityApiTestData.LoginAsAdminAsync(client, factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var email = $"failed-delivery-{Guid.NewGuid():N}@nags.sa";
        var invite = await client.PostAsJsonAsync($"{Base}/users/invite",
            new { email, displayName = "Failed Delivery User" });

        invite.StatusCode.ShouldBe(HttpStatusCode.Created);
        var invited = await invite.Content.ReadFromJsonAsync<InvitedResponse>();
        invited.ShouldNotBeNull();
        invited!.DeliveryStatus.ShouldBe("Failed");

        var detail = await client.GetFromJsonAsync<UserDetailItem>($"{Base}/users/{invited.Id}");
        detail!.Id.ShouldBe(invited.Id);
    }

    [Fact]
    public async Task Failed_resend_invitation_keeps_existing_activation_token_valid()
    {
        var admin = await IdentityApiTestData.CreateAuthenticatedAdminClientAsync(factory);
        var email = $"resend-failed-{Guid.NewGuid():N}@nags.sa";

        var invite = await admin.PostAsJsonAsync($"{Base}/users/invite",
            new { email, displayName = "Resend Failure User" });
        invite.StatusCode.ShouldBe(HttpStatusCode.Created);
        var invited = await invite.Content.ReadFromJsonAsync<InvitedResponse>();

        var originalToken = await factory.GetInvitationTokenAsync(email);
        originalToken.ShouldNotBeNull();

        await using var failingApp = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.Replace(ServiceDescriptor.Scoped<IInvitationNotifier, ThrowingInvitationNotifier>())));

        var failingClient = failingApp.CreateClient();
        var adminToken = await IdentityApiTestData.LoginAsAdminAsync(failingClient, factory);
        failingClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var resend = await failingClient.PostAsync($"{Base}/users/{invited!.Id}/resend-invitation", content: null);
        resend.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);

        var activate = await admin.PostAsJsonAsync($"{Base}/auth/activate",
            new { email, invitationToken = originalToken, newPassword = "OriginalStillWorks#12345" });
        activate.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Seeded_demo_data_supports_multi_page_users_and_roles_lists()
    {
        var client = await IdentityApiTestData.CreateAuthenticatedAdminClientAsync(factory);

        var rolesBefore = await client.GetFromJsonAsync<PagedList<RoleItem>>($"{Base}/roles?page=1&pageSize=1");
        var usersBefore = await client.GetFromJsonAsync<PagedList<UserItem>>($"{Base}/users?page=1&pageSize=1");

        await IdentityApiTestData.SeedDemoRolesAsync(client);
        await IdentityApiTestData.SeedDemoUsersAsync(factory, client);

        const int pageSize = 20;
        var expectedRoleTotal = rolesBefore!.TotalCount + IdentityApiTestData.DemoRoleCount;
        var expectedUserTotal = usersBefore!.TotalCount + IdentityApiTestData.DemoUserCount;

        var rolesPage1 = await client.GetFromJsonAsync<PagedList<RoleItem>>($"{Base}/roles?page=1&pageSize={pageSize}");
        rolesPage1!.TotalCount.ShouldBe(expectedRoleTotal);
        rolesPage1.Items.Count.ShouldBe(pageSize);
        rolesPage1.Page.ShouldBe(1);
        rolesPage1.PageSize.ShouldBe(pageSize);

        var rolesPage2 = await client.GetFromJsonAsync<PagedList<RoleItem>>($"{Base}/roles?page=2&pageSize={pageSize}");
        rolesPage2!.TotalCount.ShouldBe(expectedRoleTotal);
        rolesPage2.Items.Count.ShouldBe(pageSize);

        var rolesLastPage = (int)Math.Ceiling(expectedRoleTotal / (double)pageSize);
        var rolesFinalPageSize = (int)(expectedRoleTotal - (pageSize * (rolesLastPage - 1)));
        var rolesPageLast = await client.GetFromJsonAsync<PagedList<RoleItem>>(
            $"{Base}/roles?page={rolesLastPage}&pageSize={pageSize}");
        rolesPageLast!.TotalCount.ShouldBe(expectedRoleTotal);
        rolesPageLast.Items.Count.ShouldBe(rolesFinalPageSize);

        var usersPage1 = await client.GetFromJsonAsync<PagedList<UserItem>>($"{Base}/users?page=1&pageSize={pageSize}");
        usersPage1!.TotalCount.ShouldBe(expectedUserTotal);
        usersPage1.Items.Count.ShouldBe(pageSize);

        var usersPage2 = await client.GetFromJsonAsync<PagedList<UserItem>>($"{Base}/users?page=2&pageSize={pageSize}");
        usersPage2!.TotalCount.ShouldBe(expectedUserTotal);
        usersPage2.Items.Count.ShouldBe(pageSize);

        var usersLastPage = (int)Math.Ceiling(expectedUserTotal / (double)pageSize);
        var usersFinalPageSize = (int)(expectedUserTotal - (pageSize * (usersLastPage - 1)));
        var usersPageLast = await client.GetFromJsonAsync<PagedList<UserItem>>(
            $"{Base}/users?page={usersLastPage}&pageSize={pageSize}");
        usersPageLast!.TotalCount.ShouldBe(expectedUserTotal);
        usersPageLast.Items.Count.ShouldBe(usersFinalPageSize);

        var search = await client.GetFromJsonAsync<PagedList<UserItem>>(
            $"{Base}/users?page=1&pageSize={pageSize}&search=Demo%20User%20042");
        search!.TotalCount.ShouldBe(1);
        search.Items.Single().DisplayName.ShouldBe(IdentityApiTestData.DemoUserDisplayName(42));
    }

    private sealed record UserItem(Guid Id, string Email, string DisplayName);

    private static async Task<Guid> CreateAdministratorRoleAsync(HttpClient admin, string name)
    {
        var response = await admin.PostAsJsonAsync($"{Base}/roles",
            new
            {
                name,
                description = (string?)null,
                compatibleUserType = "SystemAdministrator",
                permissions = new[] { "identity.users.view" }
            });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    private static Task<HttpResponseMessage> SendAccessChangeAsync(
        HttpClient client,
        Guid userId,
        Guid roleId,
        string rowVersion)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"{Base}/users/{userId}/role")
        {
            Content = JsonContent.Create(new { roleId })
        };
        request.Headers.TryAddWithoutValidation("If-Match", rowVersion);
        return client.SendAsync(request);
    }

    private async Task<(Guid UserId, HttpClient Client)> InviteActivateAndLoginSystemAdministratorAsync(
        HttpClient inviter,
        string displayName,
        Func<HttpClient>? createClient = null)
    {
        var unique = Guid.NewGuid().ToString("N");
        var email = $"race-admin-{unique}@nags.sa";
        var password = $"Race#{unique[..12]}Aa1";

        var invite = await inviter.PostAsJsonAsync($"{Base}/users/invite",
            new { email, displayName });
        invite.StatusCode.ShouldBe(HttpStatusCode.Created);
        var invited = await invite.Content.ReadFromJsonAsync<InvitedResponse>();
        invited.ShouldNotBeNull();

        var invitationToken = await factory.GetInvitationTokenAsync(email);
        invitationToken.ShouldNotBeNull();
        var activate = await inviter.PostAsJsonAsync($"{Base}/auth/activate",
            new { email, invitationToken, newPassword = password });
        activate.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var client = createClient?.Invoke() ?? factory.CreateClient();
        var login = await client.PostAsJsonAsync($"{Base}/auth/login", new { email, password });
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        var tokens = await login.Content.ReadFromJsonAsync<TokenResponse>();
        tokens.ShouldNotBeNull();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);

        var me = await client.GetFromJsonAsync<MeResponse>($"{Base}/me");
        me.ShouldNotBeNull();
        me!.Id.ShouldBe(invited!.Id);
        me.UserType.ShouldBe("SystemAdministrator");

        return (invited.Id, client);
    }

    private sealed class AccessManagementRaceBarrier(int requiredArrivals)
    {
        private readonly TaskCompletionSource _allArrived =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;

        public Task AllArrived => _allArrived.Task;

        public async Task ArriveAndWaitAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _arrivals) == requiredArrivals)
                _allArrived.TrySetResult();

            await _release.Task.WaitAsync(cancellationToken);
        }

        public void Release() => _release.TrySetResult();
    }

    private sealed class BarrierIdentityDbContext(
        IdentityDbContext inner,
        AccessManagementRaceBarrier barrier) : IIdentityDbContext
    {
        public DbSet<User> Users => inner.Users;
        public DbSet<Role> Roles => inner.Roles;
        public DbSet<UserSession> Sessions => inner.Sessions;
        public DbSet<OutboxMessage> OutboxMessages => inner.OutboxMessages;
        public DbSet<InboxMessage> InboxMessages => inner.InboxMessages;

        public async Task<IIdentityTransaction> BeginAccessManagementTransactionAsync(
            CancellationToken cancellationToken = default)
        {
            await barrier.ArriveAndWaitAsync(cancellationToken);
            return await inner.BeginAccessManagementTransactionAsync(cancellationToken);
        }

        public Task<IIdentityTransaction> BeginSessionFamilyTransactionAsync(
            Guid familyId,
            CancellationToken cancellationToken = default) =>
            inner.BeginSessionFamilyTransactionAsync(familyId, cancellationToken);

        public Task AcquireSessionFamilyLockAsync(
            Guid familyId,
            CancellationToken cancellationToken = default) =>
            inner.AcquireSessionFamilyLockAsync(familyId, cancellationToken);

        public void SetOriginalRowVersion<TEntity>(TEntity entity, byte[] rowVersion)
            where TEntity : class =>
            inner.SetOriginalRowVersion(entity, rowVersion);

        public Task ReloadAsync<TEntity>(
            TEntity entity,
            CancellationToken cancellationToken = default)
            where TEntity : class =>
            inner.ReloadAsync(entity, cancellationToken);

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            inner.SaveChangesAsync(cancellationToken);
    }

    private async Task ReleaseLoginEmailAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var user = await db.Users.FirstAsync(u => u.Id == userId);
        user.ReleaseLoginEmail(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
    }

    private sealed class ThrowingInvitationNotifier : IInvitationNotifier
    {
        public Task SendInvitationAsync(
            string email,
            string displayName,
            Guid userId,
            string invitationToken,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Synthetic invitation delivery failure.");
    }
}
