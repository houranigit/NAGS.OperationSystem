using System.Net;
using System.Net.Http.Json;
using BuildingBlocks.Contracts.Authorization;
using Identity.Domain.Users;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace MasterData.IntegrationTests;

/// <summary>
/// Cross-module portal-access provisioning and lifecycle propagation between MasterData and Identity,
/// driven through the real outbox/inbox (drained synchronously per test).
/// </summary>
public class PortalAccessIntegrationTests(MasterDataApiFactory factory) : IClassFixture<MasterDataApiFactory>
{
    private const string Base = MasterDataApiFactory.Base;
    private const string IdentityBase = MasterDataApiFactory.IdentityBase;

    private sealed record StaffDetail(
        Guid Id,
        string FullName,
        string EmployeeId,
        string Email,
        string? PendingLoginEmail,
        string? LoginEmailChangeFailureReason,
        Guid StationId,
        Guid ManpowerTypeId,
        bool IsActive,
        Guid? LinkedUserId,
        string PortalState,
        string? PortalFailureReason,
        string RowVersion);
    private sealed record CustomerDetail(Guid Id, string? IataCode, bool IsActive, string RowVersion, List<ContactBody> Contacts);
    private sealed record ContactBody(
        Guid Id,
        string Name,
        string Email,
        string? PendingLoginEmail,
        string? LoginEmailChangeFailureReason,
        Guid? LinkedUserId,
        string PortalState,
        string? PortalFailureReason,
        bool IsActive);
    private sealed record StationDetail(Guid Id, string RowVersion);

    [Fact]
    public async Task Grant_staff_access_provisions_invited_user_and_links_back()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var roleId = await CreateRoleAsync(client, "StationStaff", "masterdata.staff-members.view");
        var (staffId, _) = await CreateStaffAsync(client);

        (await GrantStaffAccessAsync(client, staffId, roleId))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await factory.DrainOutboxesAsync();

        var staff = await client.GetFromJsonAsync<StaffDetail>($"{Base}/staff-members/{staffId}");
        staff!.LinkedUserId.ShouldNotBeNull();

        var user = await FindUserByExternalRefAsync(staffId);
        user.ShouldNotBeNull();
        user!.Status.ShouldBe(UserStatus.Invited);
        user.UserType.ShouldBe(BuildingBlocks.Contracts.Authorization.UserType.StationStaff);
        user.RoleId.ShouldBe(roleId);
        staff.LinkedUserId.ShouldBe(user.Id);
    }

    [Fact]
    public async Task Create_staff_with_portal_role_provisions_invited_user_and_links_back()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var roleId = await CreateRoleAsync(client, "StationStaff", "masterdata.staff-members.view");
        var stationId = await CreateStationAsync(client);
        var manpowerTypeId = await CreateManpowerTypeAsync(client);
        var email = $"staff-create-{Guid.NewGuid():N}@example.com";

        var create = await client.PostAsJsonAsync($"{Base}/staff-members", new
        {
            fullName = "Portal Staff Create",
            employeeId = $"EMP-{Guid.NewGuid():N}",
            email,
            stationId,
            manpowerTypeId,
            employmentContract = (object?)null,
            workingDays = (string[]?)null,
            licenses = Array.Empty<object>(),
            portalAccessRoleId = roleId
        });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var staffId = await create.Content.ReadFromJsonAsync<Guid>();

        var provisioning = await client.GetFromJsonAsync<StaffDetail>($"{Base}/staff-members/{staffId}");
        provisioning!.PortalState.ShouldBe("Provisioning");
        provisioning.LinkedUserId.ShouldBeNull();

        await factory.DrainOutboxesAsync();

        var invited = await client.GetFromJsonAsync<StaffDetail>($"{Base}/staff-members/{staffId}");
        invited!.LinkedUserId.ShouldNotBeNull();

        var user = await FindUserByExternalRefAsync(staffId);
        user.ShouldNotBeNull();
        user!.Status.ShouldBe(UserStatus.Invited);
        user.UserType.ShouldBe(BuildingBlocks.Contracts.Authorization.UserType.StationStaff);
        user.RoleId.ShouldBe(roleId);
        invited.LinkedUserId.ShouldBe(user.Id);
    }

    [Fact]
    public async Task Add_contact_with_portal_role_provisions_invited_user_and_links_back()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var roleId = await CreateRoleAsync(client, "CustomerContact", "masterdata.customers.view");
        var customerId = await CreateCustomerAsync(client);
        var before = await client.GetFromJsonAsync<CustomerDetail>($"{Base}/customers/{customerId}");
        var email = $"contact-add-{Guid.NewGuid():N}@example.com";

        var add = new HttpRequestMessage(HttpMethod.Post, $"{Base}/customers/{customerId}/contacts")
        {
            Content = JsonContent.Create(new
            {
                name = "Portal Contact Add",
                jobTitle = (string?)null,
                email,
                phone = (string?)null,
                portalAccessRoleId = roleId
            })
        };
        add.Headers.TryAddWithoutValidation("If-Match", before!.RowVersion);
        var addResponse = await client.SendAsync(add);
        addResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var contactId = await addResponse.Content.ReadFromJsonAsync<Guid>();

        await factory.DrainOutboxesAsync();

        var after = await client.GetFromJsonAsync<CustomerDetail>($"{Base}/customers/{customerId}");
        var contact = after!.Contacts.Single(c => c.Id == contactId);
        contact.LinkedUserId.ShouldNotBeNull();
        contact.PortalState.ShouldBe("Invited");

        var user = await FindUserByExternalRefAsync(contactId);
        user.ShouldNotBeNull();
        user!.Status.ShouldBe(UserStatus.Invited);
        user.UserType.ShouldBe(BuildingBlocks.Contracts.Authorization.UserType.CustomerContact);
        user.RoleId.ShouldBe(roleId);
        contact.LinkedUserId.ShouldBe(user.Id);

        var invitationToken = await factory.GetInvitationTokenAsync(email);
        invitationToken.ShouldNotBeNull();

        (await client.PostAsJsonAsync($"{IdentityBase}/auth/activate",
            new { email, invitationToken, newPassword = "Contact#12345" }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await factory.DrainOutboxesAsync();

        var activated = await client.GetFromJsonAsync<CustomerDetail>($"{Base}/customers/{customerId}");
        activated!.Contacts.Single(c => c.Id == contactId).PortalState.ShouldBe("Active");
    }

    [Fact]
    public async Task Create_customer_with_contact_portal_role_provisions_invited_user_and_links_back()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var roleId = await CreateRoleAsync(client, "CustomerContact", "masterdata.customers.view");
        var contactEmail = $"contact-create-{Guid.NewGuid():N}@example.com";

        var create = await client.PostAsJsonAsync($"{Base}/customers", new
        {
            iataCode = await UnusedCustomerIataAsync(client),
            icaoCode = (string?)null,
            name = "Portal Customer Create",
            countryId = await CreateCountryAsync(client),
            officialEmail = (string?)null,
            officialPhone = (string?)null,
            address = new { line1 = "1 Airport Rd", line2 = (string?)null, city = "Amman", region = (string?)null, postalCode = (string?)null },
            contacts = new[]
            {
                new
                {
                    id = (Guid?)null,
                    name = "Portal Contact Create",
                    jobTitle = (string?)null,
                    email = contactEmail,
                    phone = (string?)null,
                    portalAccessRoleId = (Guid?)roleId
                }
            }
        });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var customerId = await create.Content.ReadFromJsonAsync<Guid>();
        var before = await client.GetFromJsonAsync<CustomerDetail>($"{Base}/customers/{customerId}");
        var contactId = before!.Contacts.Single(c => c.Email == contactEmail).Id;

        await factory.DrainOutboxesAsync();

        var after = await client.GetFromJsonAsync<CustomerDetail>($"{Base}/customers/{customerId}");
        var contact = after!.Contacts.Single(c => c.Id == contactId);
        contact.LinkedUserId.ShouldNotBeNull();

        var user = await FindUserByExternalRefAsync(contactId);
        user.ShouldNotBeNull();
        user!.Status.ShouldBe(UserStatus.Invited);
        user.UserType.ShouldBe(BuildingBlocks.Contracts.Authorization.UserType.CustomerContact);
        user.RoleId.ShouldBe(roleId);
        contact.LinkedUserId.ShouldBe(user.Id);
    }

    [Fact]
    public async Task Portal_state_transitions_through_provisioning_invited_and_suspended()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var roleId = await CreateRoleAsync(client, "StationStaff", "masterdata.staff-members.view");
        var (staffId, _) = await CreateStaffAsync(client);

        (await GrantStaffAccessAsync(client, staffId, roleId))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Before draining, the request is in flight (Provisioning).
        var provisioning = await client.GetFromJsonAsync<StaffDetail>($"{Base}/staff-members/{staffId}");
        provisioning!.PortalState.ShouldBe("Provisioning");
        (await GrantStaffAccessAsync(client, staffId, roleId))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);

        await factory.DrainOutboxesAsync();

        var invited = await client.GetFromJsonAsync<StaffDetail>($"{Base}/staff-members/{staffId}");
        invited!.PortalState.ShouldBe("Invited");
        invited.LinkedUserId.ShouldNotBeNull();

        var invitationToken = await factory.GetInvitationTokenAsync(invited.Email);
        invitationToken.ShouldNotBeNull();

        (await client.PostAsJsonAsync($"{IdentityBase}/auth/activate",
            new { email = invited.Email, invitationToken, newPassword = "Staff#12345" }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await factory.DrainOutboxesAsync();

        var active = await client.GetFromJsonAsync<StaffDetail>($"{Base}/staff-members/{staffId}");
        active!.PortalState.ShouldBe("Active");
        var activeUser = await FindUserByExternalRefAsync(staffId);
        activeUser!.Status.ShouldBe(UserStatus.Active);

        // Suspending directly from Identity also reflects back to MasterData.
        (await client.PostAsync($"{IdentityBase}/users/{activeUser.Id}/suspend", content: null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await factory.DrainOutboxesAsync();

        var identitySuspended = await client.GetFromJsonAsync<StaffDetail>($"{Base}/staff-members/{staffId}");
        identitySuspended!.PortalState.ShouldBe("Suspended");

        (await client.PostAsync($"{IdentityBase}/users/{activeUser.Id}/restore-access", content: null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await factory.DrainOutboxesAsync();

        active = await client.GetFromJsonAsync<StaffDetail>($"{Base}/staff-members/{staffId}");
        active!.PortalState.ShouldBe("Active");

        // Deactivating the staff member suspends portal access.
        var deactivate = new HttpRequestMessage(HttpMethod.Post, $"{Base}/staff-members/{staffId}/deactivate");
        deactivate.Headers.TryAddWithoutValidation("If-Match", active.RowVersion);
        (await client.SendAsync(deactivate)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var suspended = await client.GetFromJsonAsync<StaffDetail>($"{Base}/staff-members/{staffId}");
        suspended!.PortalState.ShouldBe("Suspended");

        await factory.DrainOutboxesAsync();
        var suspendedUser = await FindUserByExternalRefAsync(staffId);
        suspendedUser!.Status.ShouldBe(UserStatus.Suspended);

        var activateStaff = new HttpRequestMessage(HttpMethod.Post, $"{Base}/staff-members/{staffId}/activate");
        activateStaff.Headers.TryAddWithoutValidation("If-Match", suspended.RowVersion);
        (await client.SendAsync(activateStaff)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await client.PostAsync($"{IdentityBase}/users/{suspendedUser.Id}/restore-access", content: null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await factory.DrainOutboxesAsync();

        var restored = await client.GetFromJsonAsync<StaffDetail>($"{Base}/staff-members/{staffId}");
        restored!.PortalState.ShouldBe("Active");
    }

    [Fact]
    public async Task Identity_lock_and_unlock_reflects_on_staff_portal_state()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var roleId = await CreateRoleAsync(client, "StationStaff", "masterdata.staff-members.view");
        var (staffId, email) = await CreateStaffAsync(client);

        (await GrantStaffAccessAsync(client, staffId, roleId))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await factory.DrainOutboxesAsync();

        var invitationToken = await factory.GetInvitationTokenAsync(email);
        invitationToken.ShouldNotBeNull();

        (await client.PostAsJsonAsync($"{IdentityBase}/auth/activate",
            new { email, invitationToken, newPassword = "LockedStaff#12345" }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await factory.DrainOutboxesAsync();

        var active = await client.GetFromJsonAsync<StaffDetail>($"{Base}/staff-members/{staffId}");
        active!.PortalState.ShouldBe("Active");
        var user = await FindUserByExternalRefAsync(staffId);
        user!.Status.ShouldBe(UserStatus.Active);

        (await client.PostAsync($"{IdentityBase}/users/{user.Id}/lock", content: null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await factory.DrainOutboxesAsync();

        var locked = await client.GetFromJsonAsync<StaffDetail>($"{Base}/staff-members/{staffId}");
        locked!.PortalState.ShouldBe("Suspended");

        (await client.PostAsync($"{IdentityBase}/users/{user.Id}/unlock", content: null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await factory.DrainOutboxesAsync();

        var unlocked = await client.GetFromJsonAsync<StaffDetail>($"{Base}/staff-members/{staffId}");
        unlocked!.PortalState.ShouldBe("Active");
    }

    [Fact]
    public async Task Failed_provisioning_is_visible_and_state_is_failed()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        // Incompatible role makes Identity reject provisioning.
        var roleId = await CreateRoleAsync(client, "SystemAdministrator", "masterdata.staff-members.view");
        var (staffId, _) = await CreateStaffAsync(client);

        (await GrantStaffAccessAsync(client, staffId, roleId))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await factory.DrainOutboxesAsync();

        var failed = await client.GetFromJsonAsync<StaffDetail>($"{Base}/staff-members/{staffId}");
        failed!.PortalState.ShouldBe("Failed");
        failed.PortalFailureReason.ShouldNotBeNullOrWhiteSpace();
        failed.LinkedUserId.ShouldBeNull();
    }

    [Fact]
    public async Task Grant_access_is_idempotent_and_never_creates_two_users()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var roleId = await CreateRoleAsync(client, "StationStaff", "masterdata.staff-members.view");
        var (staffId, _) = await CreateStaffAsync(client);

        (await GrantStaffAccessAsync(client, staffId, roleId))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await factory.DrainOutboxesAsync();

        // A second grant attempt is rejected because the record is already linked.
        (await GrantStaffAccessAsync(client, staffId, roleId))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
        await factory.DrainOutboxesAsync();

        (await CountUsersByExternalRefAsync(staffId)).ShouldBe(1);
    }

    [Fact]
    public async Task Portal_access_idempotency_is_scoped_by_user_type_and_external_reference()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var stationRoleId = await CreateRoleAsync(client, "StationStaff", "masterdata.staff-members.view");
        var contactRoleId = await CreateRoleAsync(client, "CustomerContact", "masterdata.customers.view");
        var contactEmail = $"typed-ref-{Guid.NewGuid():N}@example.com";
        var (customerId, contactId) = await CreateCustomerWithContactAsync(client, contactEmail);

        var stationUserId = await SeedInvitedUserAsync(
            UserType.StationStaff,
            contactId,
            stationRoleId,
            $"station-collision-{Guid.NewGuid():N}@example.com");

        (await GrantContactAccessAsync(client, customerId, contactId, contactRoleId))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await factory.DrainOutboxesAsync();

        var stationUser = await FindUserByIdAsync(stationUserId);
        stationUser.ShouldNotBeNull();
        stationUser!.UserType.ShouldBe(UserType.StationStaff);

        var contactUser = await FindUserByExternalRefAsync(contactId, UserType.CustomerContact);
        contactUser.ShouldNotBeNull();
        contactUser!.Id.ShouldNotBe(stationUserId);
        contactUser.Email.Value.ShouldBe(contactEmail);
        contactUser.RoleId.ShouldBe(contactRoleId);

        var customer = await client.GetFromJsonAsync<CustomerDetail>($"{Base}/customers/{customerId}");
        var contact = customer!.Contacts.Single(c => c.Id == contactId);
        contact.LinkedUserId.ShouldBe(contactUser.Id);

        (await CountUsersByExternalRefAsync(contactId)).ShouldBe(2);
    }

    [Fact]
    public async Task Grant_access_with_incompatible_role_does_not_provision_a_user()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        // A SystemAdministrator role is not compatible with a StationStaff account.
        var roleId = await CreateRoleAsync(client, "SystemAdministrator", "masterdata.staff-members.view");
        var (staffId, _) = await CreateStaffAsync(client);

        (await GrantStaffAccessAsync(client, staffId, roleId))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await factory.DrainOutboxesAsync();

        (await CountUsersByExternalRefAsync(staffId)).ShouldBe(0);
        var staff = await client.GetFromJsonAsync<StaffDetail>($"{Base}/staff-members/{staffId}");
        staff!.LinkedUserId.ShouldBeNull();
    }

    [Fact]
    public async Task Deactivating_staff_suspends_the_linked_user_and_keeps_the_link()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var roleId = await CreateRoleAsync(client, "StationStaff", "masterdata.staff-members.view");
        var (staffId, _) = await CreateStaffAsync(client);

        (await GrantStaffAccessAsync(client, staffId, roleId))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await factory.DrainOutboxesAsync();

        var staff = await client.GetFromJsonAsync<StaffDetail>($"{Base}/staff-members/{staffId}");
        var deactivate = new HttpRequestMessage(HttpMethod.Post, $"{Base}/staff-members/{staffId}/deactivate");
        deactivate.Headers.TryAddWithoutValidation("If-Match", staff!.RowVersion);
        (await client.SendAsync(deactivate)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await factory.DrainOutboxesAsync();

        var user = await FindUserByExternalRefAsync(staffId);
        user!.Status.ShouldBe(UserStatus.Suspended);

        var after = await client.GetFromJsonAsync<StaffDetail>($"{Base}/staff-members/{staffId}");
        after!.LinkedUserId.ShouldBe(user.Id);
        after.PortalState.ShouldBe("Suspended");
    }

    [Fact]
    public async Task Changing_a_linked_staff_email_requires_reverification_before_taking_effect()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var roleId = await CreateRoleAsync(client, "StationStaff", "masterdata.staff-members.view");
        var (staffId, originalEmail) = await CreateStaffAsync(client);

        (await GrantStaffAccessAsync(client, staffId, roleId))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await factory.DrainOutboxesAsync();

        var staff = await client.GetFromJsonAsync<StaffDetail>($"{Base}/staff-members/{staffId}");
        var newEmail = $"changed-{Guid.NewGuid():N}@example.com";
        var update = new HttpRequestMessage(HttpMethod.Put, $"{Base}/staff-members/{staffId}")
        {
            Content = JsonContent.Create(new
            {
                fullName = staff!.FullName,
                employeeId = staff.EmployeeId,
                email = newEmail,
                stationId = staff.StationId,
                manpowerTypeId = staff.ManpowerTypeId,
                employmentContract = (object?)null,
                workingDays = (string[]?)null,
                licenses = Array.Empty<object>()
            })
        };
        update.Headers.TryAddWithoutValidation("If-Match", staff.RowVersion);
        (await client.SendAsync(update)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await factory.DrainOutboxesAsync();

        var pendingStaff = await client.GetFromJsonAsync<StaffDetail>($"{Base}/staff-members/{staffId}");
        pendingStaff!.Email.ShouldBe(newEmail);
        pendingStaff.PendingLoginEmail.ShouldBe(newEmail);
        pendingStaff.LoginEmailChangeFailureReason.ShouldBeNull();

        // The linked user keeps the old login email until reverification, and a hashed (never
        // plaintext) verification token is recorded.
        var pending = await FindUserByExternalRefAsync(staffId);
        pending!.Email.Value.ShouldBe(originalEmail);
        pending.PendingEmail.ShouldBe(newEmail);
        pending.EmailChangeToken.ShouldNotBeNull();

        // The verification email is delivered durably to the new address; confirm with the raw token.
        var verificationToken = await factory.GetInvitationTokenAsync(newEmail);
        verificationToken.ShouldNotBeNull();

        var confirm = await client.PostAsJsonAsync($"{IdentityBase}/auth/confirm-email-change",
            new { token = verificationToken, newEmail });
        confirm.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await factory.DrainOutboxesAsync();

        var confirmed = await FindUserByExternalRefAsync(staffId);
        confirmed!.Email.Value.ShouldBe(newEmail);
        confirmed.PendingEmail.ShouldBeNull();

        var confirmedStaff = await client.GetFromJsonAsync<StaffDetail>($"{Base}/staff-members/{staffId}");
        confirmedStaff!.PendingLoginEmail.ShouldBeNull();
        confirmedStaff.LoginEmailChangeFailureReason.ShouldBeNull();
    }

    [Fact]
    public async Task Linked_staff_email_change_failure_is_reflected_on_masterdata()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var roleId = await CreateRoleAsync(client, "StationStaff", "masterdata.staff-members.view");
        var (staffId, originalEmail) = await CreateStaffAsync(client);
        var duplicateEmail = $"duplicate-{Guid.NewGuid():N}@example.com";
        await SeedInvitedUserAsync(UserType.StationStaff, Guid.NewGuid(), roleId, duplicateEmail);

        (await GrantStaffAccessAsync(client, staffId, roleId))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await factory.DrainOutboxesAsync();

        var staff = await client.GetFromJsonAsync<StaffDetail>($"{Base}/staff-members/{staffId}");
        var update = new HttpRequestMessage(HttpMethod.Put, $"{Base}/staff-members/{staffId}")
        {
            Content = JsonContent.Create(new
            {
                fullName = staff!.FullName,
                employeeId = staff.EmployeeId,
                email = duplicateEmail,
                stationId = staff.StationId,
                manpowerTypeId = staff.ManpowerTypeId,
                employmentContract = (object?)null,
                workingDays = (string[]?)null,
                licenses = Array.Empty<object>()
            })
        };
        update.Headers.TryAddWithoutValidation("If-Match", staff.RowVersion);
        (await client.SendAsync(update)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await factory.DrainOutboxesAsync();

        var failed = await client.GetFromJsonAsync<StaffDetail>($"{Base}/staff-members/{staffId}");
        failed!.Email.ShouldBe(duplicateEmail);
        failed.PendingLoginEmail.ShouldBeNull();
        failed.LoginEmailChangeFailureReason.ShouldNotBeNull();
        failed.LoginEmailChangeFailureReason!.ShouldContain("already used");

        var linkedUser = await FindUserByExternalRefAsync(staffId);
        linkedUser!.Email.Value.ShouldBe(originalEmail);
        linkedUser.PendingEmail.ShouldBeNull();
        linkedUser.EmailChangeToken.ShouldBeNull();
    }

    [Fact]
    public async Task Changing_a_linked_contact_email_requires_reverification_before_taking_effect()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var roleId = await CreateRoleAsync(client, "CustomerContact", "masterdata.customers.view");
        var originalEmail = $"contact-{Guid.NewGuid():N}@example.com";
        var (customerId, contactId) = await CreateCustomerWithContactAsync(client, originalEmail);

        (await GrantContactAccessAsync(client, customerId, contactId, roleId))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await factory.DrainOutboxesAsync();

        var customer = await client.GetFromJsonAsync<CustomerDetail>($"{Base}/customers/{customerId}");
        var contact = customer!.Contacts.Single(c => c.Id == contactId);
        var newEmail = $"changed-contact-{Guid.NewGuid():N}@example.com";
        var update = new HttpRequestMessage(HttpMethod.Put, $"{Base}/customers/{customerId}/contacts/{contactId}")
        {
            Content = JsonContent.Create(new
            {
                name = contact.Name,
                jobTitle = (string?)null,
                email = newEmail,
                phone = (string?)null
            })
        };
        update.Headers.TryAddWithoutValidation("If-Match", customer.RowVersion);
        (await client.SendAsync(update)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await factory.DrainOutboxesAsync();

        var pendingCustomer = await client.GetFromJsonAsync<CustomerDetail>($"{Base}/customers/{customerId}");
        var pendingContact = pendingCustomer!.Contacts.Single(c => c.Id == contactId);
        pendingContact.Email.ShouldBe(newEmail);
        pendingContact.PendingLoginEmail.ShouldBe(newEmail);
        pendingContact.LoginEmailChangeFailureReason.ShouldBeNull();

        var pending = await FindUserByExternalRefAsync(contactId);
        pending!.Email.Value.ShouldBe(originalEmail);
        pending.PendingEmail.ShouldBe(newEmail);
        pending.EmailChangeToken.ShouldNotBeNull();

        var verificationToken = await factory.GetInvitationTokenAsync(newEmail);
        verificationToken.ShouldNotBeNull();

        var confirm = await client.PostAsJsonAsync($"{IdentityBase}/auth/confirm-email-change",
            new { token = verificationToken, newEmail });
        confirm.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await factory.DrainOutboxesAsync();

        var confirmed = await FindUserByExternalRefAsync(contactId);
        confirmed!.Email.Value.ShouldBe(newEmail);
        confirmed.PendingEmail.ShouldBeNull();

        var confirmedCustomer = await client.GetFromJsonAsync<CustomerDetail>($"{Base}/customers/{customerId}");
        var confirmedContact = confirmedCustomer!.Contacts.Single(c => c.Id == contactId);
        confirmedContact.PendingLoginEmail.ShouldBeNull();
        confirmedContact.LoginEmailChangeFailureReason.ShouldBeNull();
    }

    [Fact]
    public async Task Permanently_removing_a_linked_contact_releases_email_for_reuse()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var roleId = await CreateRoleAsync(client, "CustomerContact", "masterdata.customers.view");
        var sharedEmail = $"reuse-{Guid.NewGuid():N}@example.com";

        var (customerId, contactId) = await CreateCustomerWithContactAsync(client, sharedEmail);
        (await GrantContactAccessAsync(client, customerId, contactId, roleId))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await factory.DrainOutboxesAsync();

        var firstUser = await FindUserByEmailAsync(sharedEmail);
        firstUser.ShouldNotBeNull();

        // Permanently remove the contact, releasing the login email.
        var customer = await client.GetFromJsonAsync<CustomerDetail>($"{Base}/customers/{customerId}");
        var remove = new HttpRequestMessage(HttpMethod.Post, $"{Base}/customers/{customerId}/contacts/{contactId}/remove?releaseEmail=true");
        remove.Headers.TryAddWithoutValidation("If-Match", customer!.RowVersion);
        (await client.SendAsync(remove)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await factory.DrainOutboxesAsync();

        var releasedUser = await FindUserByIdAsync(firstUser!.Id);
        releasedUser!.Status.ShouldBe(UserStatus.Deactivated);
        releasedUser.LoginEmailReleased.ShouldBeTrue();

        // The released email can now be reused by a brand-new identity, producing a different user.
        var (customer2, contact2) = await CreateCustomerWithContactAsync(client, sharedEmail);
        (await GrantContactAccessAsync(client, customer2, contact2, roleId))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await factory.DrainOutboxesAsync();

        var secondUser = await FindActiveUserByEmailAsync(sharedEmail);
        secondUser.ShouldNotBeNull();
        secondUser!.Id.ShouldNotBe(firstUser.Id);
    }

    private static async Task<HttpResponseMessage> GrantStaffAccessAsync(HttpClient client, Guid staffId, Guid roleId)
    {
        var staff = await client.GetFromJsonAsync<StaffDetail>($"{Base}/staff-members/{staffId}");
        var request = new HttpRequestMessage(HttpMethod.Post, $"{Base}/staff-members/{staffId}/grant-access")
        {
            Content = JsonContent.Create(new { roleId })
        };
        request.Headers.TryAddWithoutValidation("If-Match", staff!.RowVersion);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> GrantContactAccessAsync(HttpClient client, Guid customerId, Guid contactId, Guid roleId)
    {
        var customer = await client.GetFromJsonAsync<CustomerDetail>($"{Base}/customers/{customerId}");
        var request = new HttpRequestMessage(HttpMethod.Post, $"{Base}/customers/{customerId}/contacts/{contactId}/grant-access")
        {
            Content = JsonContent.Create(new { roleId })
        };
        request.Headers.TryAddWithoutValidation("If-Match", customer!.RowVersion);
        return await client.SendAsync(request);
    }

    // --- Identity assertions via the shared database ----------------------

    private async Task<User?> FindUserByExternalRefAsync(Guid externalRef)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        return await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.ExternalReferenceId == externalRef);
    }

    private async Task<User?> FindUserByExternalRefAsync(Guid externalRef, UserType userType)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        return await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserType == userType && u.ExternalReferenceId == externalRef);
    }

    private async Task<int> CountUsersByExternalRefAsync(Guid externalRef)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        return await db.Users.AsNoTracking().CountAsync(u => u.ExternalReferenceId == externalRef);
    }

    private async Task<User?> FindUserByEmailAsync(string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        return await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email.Value == email && !u.LoginEmailReleased);
    }

    private async Task<User?> FindActiveUserByEmailAsync(string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        return await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email.Value == email && !u.LoginEmailReleased);
    }

    private async Task<User?> FindUserByIdAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        return await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
    }

    private async Task<Guid> SeedInvitedUserAsync(UserType userType, Guid externalRef, Guid roleId, string email)
    {
        var emailResult = Email.Create(email);
        emailResult.IsSuccess.ShouldBeTrue();

        var invited = User.Invite(
            emailResult.Value,
            $"Seeded {userType}",
            roleId,
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow.AddHours(24),
            DateTimeOffset.UtcNow,
            userType,
            externalRef);
        invited.IsSuccess.ShouldBeTrue();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        db.Users.Add(invited.Value);
        await db.SaveChangesAsync();
        return invited.Value.Id;
    }

    // --- Setup helpers ----------------------------------------------------

    private static async Task<Guid> CreateRoleAsync(HttpClient client, string compatibleUserType, params string[] permissions)
    {
        var create = await client.PostAsJsonAsync($"{IdentityBase}/roles", new
        {
            name = $"Role {Guid.NewGuid():N}",
            description = (string?)null,
            compatibleUserType,
            permissions
        });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await create.Content.ReadFromJsonAsync<Guid>();
    }

    private static async Task<(Guid StaffId, string Email)> CreateStaffAsync(HttpClient client)
    {
        var stationId = await CreateStationAsync(client);
        var manpowerTypeId = await CreateManpowerTypeAsync(client);
        var email = $"staff-{Guid.NewGuid():N}@example.com";
        var create = await client.PostAsJsonAsync($"{Base}/staff-members", new
        {
            fullName = "Portal Staff",
            employeeId = $"EMP-{Guid.NewGuid():N}",
            email,
            stationId,
            manpowerTypeId,
            employmentContract = (object?)null,
            workingDays = (string[]?)null,
            licenses = Array.Empty<object>()
        });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await create.Content.ReadFromJsonAsync<Guid>(), email);
    }

    private static async Task<(Guid CustomerId, Guid ContactId)> CreateCustomerWithContactAsync(HttpClient client, string contactEmail)
    {
        var customerId = await CreateCustomerAsync(client, contactEmail);
        var detail = await client.GetFromJsonAsync<CustomerDetail>($"{Base}/customers/{customerId}");
        var contactId = detail!.Contacts.Single(c => c.Email == contactEmail).Id;
        return (customerId, contactId);
    }

    private static async Task<Guid> CreateCustomerAsync(HttpClient client, string? contactEmail = null)
    {
        var countryId = await CreateCountryAsync(client);
        var iata = await UnusedCustomerIataAsync(client);
        var create = await client.PostAsJsonAsync($"{Base}/customers", new
        {
            iataCode = iata,
            icaoCode = (string?)null,
            name = "Portal Customer",
            countryId,
            officialEmail = (string?)null,
            officialPhone = (string?)null,
            address = new { line1 = "1 Airport Rd", line2 = (string?)null, city = "Amman", region = (string?)null, postalCode = (string?)null },
            contacts = contactEmail is null
                ? Array.Empty<object>()
                : new object[] { new { id = (Guid?)null, name = "Portal Contact", jobTitle = (string?)null, email = contactEmail, phone = (string?)null, portalAccessRoleId = (Guid?)null } }
        });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await create.Content.ReadFromJsonAsync<Guid>();
    }

    private static async Task<Guid> CreateStationAsync(HttpClient client)
    {
        var countryId = await CreateCountryAsync(client);
        var iata = await UnusedStationIataAsync(client);
        var create = await client.PostAsJsonAsync($"{Base}/stations",
            new { iataCode = iata, icaoCode = (string?)null, name = $"Station {iata}", city = "City", countryId });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await create.Content.ReadFromJsonAsync<Guid>();
    }

    private static async Task<Guid> CreateManpowerTypeAsync(HttpClient client)
    {
        var create = await client.PostAsJsonAsync($"{Base}/manpower-types",
            new { name = $"Manpower {Guid.NewGuid():N}", description = (string?)null });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await create.Content.ReadFromJsonAsync<Guid>();
    }

    private static async Task<Guid> CreateCountryAsync(HttpClient client)
    {
        var code = await UnusedCountryCodeAsync(client);
        var create = await client.PostAsJsonAsync($"{Base}/countries", new { name = $"Country {code}", isoCode = code });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await create.Content.ReadFromJsonAsync<Guid>();
    }

    private sealed record PagedList<T>(List<T> Items, int Page, int PageSize, long TotalCount);
    private sealed record CodeItem(Guid Id, string IataCode, string? IcaoCode, string Name, bool IsActive);
    private sealed record CountryItem(Guid Id, string Name, string IsoCode, bool IsActive);

    private static async Task<string> UnusedStationIataAsync(HttpClient client) => await UnusedTripletAsync(client, $"{Base}/stations");
    private static async Task<string> UnusedCustomerIataAsync(HttpClient client) => await UnusedPairAsync(client, $"{Base}/customers");

    private static async Task<string> UnusedTripletAsync(HttpClient client, string path)
    {
        var used = await CollectAsync<CodeItem>(client, path, c => c.IataCode);
        const string letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        foreach (var a in letters)
            foreach (var b in letters)
                foreach (var c in letters)
                {
                    var code = $"{a}{b}{c}";
                    if (used.Add(code))
                        return code;
                }
        throw new InvalidOperationException("No unused IATA code remains.");
    }

    private static async Task<string> UnusedPairAsync(HttpClient client, string path)
    {
        var used = await CollectAsync<CodeItem>(client, path, c => c.IataCode);
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        foreach (var a in chars)
            foreach (var b in chars)
            {
                var code = $"{a}{b}";
                if (used.Add(code))
                    return code;
            }
        throw new InvalidOperationException("No unused IATA code remains.");
    }

    private static async Task<string> UnusedCountryCodeAsync(HttpClient client)
    {
        var used = await CollectAsync<CountryItem>(client, $"{Base}/countries", c => c.IsoCode);
        const string letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        foreach (var a in letters)
            foreach (var b in letters)
            {
                var code = $"{a}{b}";
                if (used.Add(code))
                    return code;
            }
        throw new InvalidOperationException("No unused ISO code remains.");
    }

    private static async Task<HashSet<string>> CollectAsync<T>(HttpClient client, string path, Func<T, string> selector)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        const int pageSize = 100;
        var page = 1;
        long total;
        do
        {
            var list = await client.GetFromJsonAsync<PagedList<T>>($"{path}?page={page}&pageSize={pageSize}");
            foreach (var item in list!.Items)
                used.Add(selector(item));
            total = list.TotalCount;
            page++;
        }
        while ((page - 1) * pageSize < total);
        return used;
    }
}
