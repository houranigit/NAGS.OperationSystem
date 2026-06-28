using System.Net;
using System.Net.Http.Json;
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

    private sealed record StaffDetail(Guid Id, string FullName, string Email, Guid StationId, bool IsActive, Guid? LinkedUserId, string PortalState, string? PortalFailureReason, string RowVersion);
    private sealed record CustomerDetail(Guid Id, string? IataCode, bool IsActive, string RowVersion, List<ContactBody> Contacts);
    private sealed record ContactBody(Guid Id, string Name, string Email, Guid? LinkedUserId, bool IsActive);
    private sealed record StationDetail(Guid Id, string RowVersion);

    [Fact]
    public async Task Grant_staff_access_provisions_invited_user_and_links_back()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var roleId = await CreateRoleAsync(client, "StationStaff", "masterdata.staff-members.view");
        var (staffId, _) = await CreateStaffAsync(client);

        (await client.PostAsJsonAsync($"{Base}/staff-members/{staffId}/grant-access", new { roleId }))
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
    public async Task Portal_state_transitions_through_provisioning_invited_and_suspended()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var roleId = await CreateRoleAsync(client, "StationStaff", "masterdata.staff-members.view");
        var (staffId, _) = await CreateStaffAsync(client);

        (await client.PostAsJsonAsync($"{Base}/staff-members/{staffId}/grant-access", new { roleId }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Before draining, the request is in flight (Provisioning).
        var provisioning = await client.GetFromJsonAsync<StaffDetail>($"{Base}/staff-members/{staffId}");
        provisioning!.PortalState.ShouldBe("Provisioning");

        await factory.DrainOutboxesAsync();

        var invited = await client.GetFromJsonAsync<StaffDetail>($"{Base}/staff-members/{staffId}");
        invited!.PortalState.ShouldBe("Invited");
        invited.LinkedUserId.ShouldNotBeNull();

        // Deactivating the staff member suspends portal access.
        var deactivate = new HttpRequestMessage(HttpMethod.Post, $"{Base}/staff-members/{staffId}/deactivate");
        deactivate.Headers.TryAddWithoutValidation("If-Match", invited.RowVersion);
        (await client.SendAsync(deactivate)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var suspended = await client.GetFromJsonAsync<StaffDetail>($"{Base}/staff-members/{staffId}");
        suspended!.PortalState.ShouldBe("Suspended");
    }

    [Fact]
    public async Task Failed_provisioning_is_visible_and_state_is_failed()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        // Incompatible role makes Identity reject provisioning.
        var roleId = await CreateRoleAsync(client, "SystemAdministrator", "masterdata.staff-members.view");
        var (staffId, _) = await CreateStaffAsync(client);

        (await client.PostAsJsonAsync($"{Base}/staff-members/{staffId}/grant-access", new { roleId }))
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

        (await client.PostAsJsonAsync($"{Base}/staff-members/{staffId}/grant-access", new { roleId }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await factory.DrainOutboxesAsync();

        // A second grant attempt is rejected because the record is already linked.
        (await client.PostAsJsonAsync($"{Base}/staff-members/{staffId}/grant-access", new { roleId }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
        await factory.DrainOutboxesAsync();

        (await CountUsersByExternalRefAsync(staffId)).ShouldBe(1);
    }

    [Fact]
    public async Task Grant_access_with_incompatible_role_does_not_provision_a_user()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        // A SystemAdministrator role is not compatible with a StationStaff account.
        var roleId = await CreateRoleAsync(client, "SystemAdministrator", "masterdata.staff-members.view");
        var (staffId, _) = await CreateStaffAsync(client);

        (await client.PostAsJsonAsync($"{Base}/staff-members/{staffId}/grant-access", new { roleId }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await factory.DrainOutboxesAsync();

        (await CountUsersByExternalRefAsync(staffId)).ShouldBe(0);
        var staff = await client.GetFromJsonAsync<StaffDetail>($"{Base}/staff-members/{staffId}");
        staff!.LinkedUserId.ShouldBeNull();
    }

    [Fact]
    public async Task Deactivating_staff_deactivates_the_linked_user()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var roleId = await CreateRoleAsync(client, "StationStaff", "masterdata.staff-members.view");
        var (staffId, _) = await CreateStaffAsync(client);

        (await client.PostAsJsonAsync($"{Base}/staff-members/{staffId}/grant-access", new { roleId }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await factory.DrainOutboxesAsync();

        var staff = await client.GetFromJsonAsync<StaffDetail>($"{Base}/staff-members/{staffId}");
        var deactivate = new HttpRequestMessage(HttpMethod.Post, $"{Base}/staff-members/{staffId}/deactivate");
        deactivate.Headers.TryAddWithoutValidation("If-Match", staff!.RowVersion);
        (await client.SendAsync(deactivate)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await factory.DrainOutboxesAsync();

        var user = await FindUserByExternalRefAsync(staffId);
        user!.Status.ShouldBe(UserStatus.Deactivated);
    }

    [Fact]
    public async Task Changing_a_linked_staff_email_requires_reverification_before_taking_effect()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var roleId = await CreateRoleAsync(client, "StationStaff", "masterdata.staff-members.view");
        var (staffId, originalEmail) = await CreateStaffAsync(client);

        (await client.PostAsJsonAsync($"{Base}/staff-members/{staffId}/grant-access", new { roleId }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await factory.DrainOutboxesAsync();

        var staff = await client.GetFromJsonAsync<StaffDetail>($"{Base}/staff-members/{staffId}");
        var newEmail = $"changed-{Guid.NewGuid():N}@example.com";
        var manpowerTypeId = await CreateManpowerTypeAsync(client);
        var update = new HttpRequestMessage(HttpMethod.Put, $"{Base}/staff-members/{staffId}")
        {
            Content = JsonContent.Create(new
            {
                fullName = staff!.FullName,
                email = newEmail,
                stationId = staff.StationId,
                manpowerTypeId,
                employmentContract = (object?)null,
                workingDays = (string[]?)null,
                licenses = Array.Empty<object>()
            })
        };
        update.Headers.TryAddWithoutValidation("If-Match", staff.RowVersion);
        (await client.SendAsync(update)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await factory.DrainOutboxesAsync();

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

        var confirmed = await FindUserByExternalRefAsync(staffId);
        confirmed!.Email.Value.ShouldBe(newEmail);
        confirmed.PendingEmail.ShouldBeNull();
    }

    [Fact]
    public async Task Permanently_removing_a_linked_contact_releases_email_for_reuse()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var roleId = await CreateRoleAsync(client, "CustomerContact", "masterdata.customers.view");
        var sharedEmail = $"reuse-{Guid.NewGuid():N}@example.com";

        var (customerId, contactId) = await CreateCustomerWithContactAsync(client, sharedEmail);
        (await client.PostAsJsonAsync($"{Base}/customers/{customerId}/contacts/{contactId}/grant-access", new { roleId }))
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
        (await client.PostAsJsonAsync($"{Base}/customers/{customer2}/contacts/{contact2}/grant-access", new { roleId }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await factory.DrainOutboxesAsync();

        var secondUser = await FindActiveUserByEmailAsync(sharedEmail);
        secondUser.ShouldNotBeNull();
        secondUser!.Id.ShouldNotBe(firstUser.Id);
    }

    // --- Identity assertions via the shared database ----------------------

    private async Task<User?> FindUserByExternalRefAsync(Guid externalRef)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        return await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.ExternalReferenceId == externalRef);
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
            contacts = new[] { new { id = (Guid?)null, name = "Portal Contact", jobTitle = (string?)null, email = contactEmail, phone = (string?)null } }
        });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var customerId = await create.Content.ReadFromJsonAsync<Guid>();

        var detail = await client.GetFromJsonAsync<CustomerDetail>($"{Base}/customers/{customerId}");
        var contactId = detail!.Contacts.Single(c => c.Email == contactEmail).Id;
        return (customerId, contactId);
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
