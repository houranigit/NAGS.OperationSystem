using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BuildingBlocks.Contracts.Authorization;
using Identity.Domain.Roles;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace MasterData.IntegrationTests;

/// <summary>
/// Server-side data scope for provisioned portal accounts: station staff may only read/act within
/// their linked station, customer contacts within their linked customer, and access fails closed
/// once the linked record or parent is inactive.
/// </summary>
public class DataScopeIntegrationTests(MasterDataApiFactory factory) : IClassFixture<MasterDataApiFactory>
{
    private const string Base = MasterDataApiFactory.Base;
    private const string IdentityBase = MasterDataApiFactory.IdentityBase;

    private sealed record TokenResponse(string AccessToken, DateTimeOffset ExpiresAtUtc);
    private sealed record PagedList<T>(List<T> Items, int Page, int PageSize, long TotalCount);
    private sealed record StaffItem(Guid Id, string FullName, Guid StationId);
    private sealed record StaffDetail(Guid Id, string RowVersion);
    private sealed record StationItem(Guid Id, string IataCode);
    private sealed record CustomerItem(Guid Id, string Name);
    private sealed record CustomerDetail(Guid Id, string RowVersion, List<ContactDetail> Contacts);
    private sealed record ContactDetail(Guid Id, string Name, string Email);
    private sealed record CountryOption(Guid Id, string Name, string IsoCode);

    [Fact]
    public async Task Station_staff_only_sees_and_touches_its_own_station()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();

        // Two stations, each with a staff member. One staff member gets a portal account.
        var (stationA, manpower) = (await Helpers.CreateStationAsync(admin), await Helpers.CreateManpowerTypeAsync(admin));
        var stationB = await Helpers.CreateStationAsync(admin);
        var staffInA = await Helpers.CreateStaffAsync(admin, stationA, manpower);
        var staffInB = await Helpers.CreateStaffAsync(admin, stationB, manpower);

        var scoped = await ProvisionScopedStaffClientAsync(admin, staffInA.Id, staffInA.Email);

        // List endpoints are filtered to the linked station only.
        var stations = await scoped.GetFromJsonAsync<PagedList<StationItem>>($"{Base}/stations?pageSize=100");
        stations!.Items.Select(s => s.Id).ShouldBe([stationA]);

        var staff = await scoped.GetFromJsonAsync<PagedList<StaffItem>>($"{Base}/staff-members?pageSize=100");
        staff!.Items.Select(s => s.Id).ShouldContain(staffInA.Id);
        staff.Items.ShouldNotContain(s => s.Id == staffInB.Id);

        // Cross-scope reads are forbidden, not merely empty.
        (await scoped.GetAsync($"{Base}/stations/{stationB}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await scoped.GetAsync($"{Base}/staff-members/{staffInB.Id}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var otherStaff = await admin.GetFromJsonAsync<StaffDetail>($"{Base}/staff-members/{staffInB.Id}");
        var crossScopeUpdate = new HttpRequestMessage(HttpMethod.Put, $"{Base}/staff-members/{staffInB.Id}")
        {
            Content = JsonContent.Create(new
            {
                fullName = "Cross Scope Staff",
                employeeId = $"EMP-{Guid.NewGuid():N}",
                email = $"cross-staff-{Guid.NewGuid():N}@example.com",
                stationId = stationB,
                manpowerTypeId = manpower,
                employmentContract = (object?)null,
                workingDays = (string[]?)null,
                licenses = Array.Empty<object>()
            })
        };
        crossScopeUpdate.Headers.TryAddWithoutValidation("If-Match", otherStaff!.RowVersion);
        (await scoped.SendAsync(crossScopeUpdate)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // In-scope reads succeed.
        (await scoped.GetAsync($"{Base}/stations/{stationA}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await scoped.GetAsync($"{Base}/staff-members/{staffInA.Id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Access_fails_closed_when_the_parent_station_is_deactivated()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var (station, manpower) = (await Helpers.CreateStationAsync(admin), await Helpers.CreateManpowerTypeAsync(admin));
        var staff = await Helpers.CreateStaffAsync(admin, station, manpower);

        var scoped = await ProvisionScopedStaffClientAsync(admin, staff.Id, staff.Email);
        (await scoped.GetAsync($"{Base}/stations/{station}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Admin deactivates the station; the scoped account can no longer resolve its data scope.
        var detail = await admin.GetFromJsonAsync<StationDetail>($"{Base}/stations/{station}");
        var deactivate = new HttpRequestMessage(HttpMethod.Post, $"{Base}/stations/{station}/deactivate");
        deactivate.Headers.TryAddWithoutValidation("If-Match", detail!.RowVersion);
        (await admin.SendAsync(deactivate)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await scoped.GetAsync($"{Base}/stations/{station}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await scoped.GetAsync($"{Base}/staff-members?pageSize=100")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Station_staff_cannot_deactivate_a_cross_scope_station_even_with_lifecycle_permission()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var (stationA, manpower) = (await Helpers.CreateStationAsync(admin), await Helpers.CreateManpowerTypeAsync(admin));
        var stationB = await Helpers.CreateStationAsync(admin);
        var staffInA = await Helpers.CreateStaffAsync(admin, stationA, manpower);

        var scoped = await ProvisionScopedStaffClientWithSeededRoleAsync(
            admin,
            staffInA.Id,
            staffInA.Email,
            "masterdata.stations.deactivate");

        var otherStation = await admin.GetFromJsonAsync<StationDetail>($"{Base}/stations/{stationB}");
        var deactivate = new HttpRequestMessage(HttpMethod.Post, $"{Base}/stations/{stationB}/deactivate");
        deactivate.Headers.TryAddWithoutValidation("If-Match", otherStation!.RowVersion);

        (await scoped.SendAsync(deactivate)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var after = await admin.GetFromJsonAsync<StationDetail>($"{Base}/stations/{stationB}");
        after!.IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task Station_staff_cannot_activate_a_cross_scope_station_even_with_lifecycle_permission()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var (stationA, manpower) = (await Helpers.CreateStationAsync(admin), await Helpers.CreateManpowerTypeAsync(admin));
        var stationB = await Helpers.CreateStationAsync(admin);
        var staffInA = await Helpers.CreateStaffAsync(admin, stationA, manpower);

        var stationBDetail = await admin.GetFromJsonAsync<StationDetail>($"{Base}/stations/{stationB}");
        var deactivate = new HttpRequestMessage(HttpMethod.Post, $"{Base}/stations/{stationB}/deactivate");
        deactivate.Headers.TryAddWithoutValidation("If-Match", stationBDetail!.RowVersion);
        (await admin.SendAsync(deactivate)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var scoped = await ProvisionScopedStaffClientWithSeededRoleAsync(
            admin,
            staffInA.Id,
            staffInA.Email,
            "masterdata.stations.activate");

        var inactiveStation = await admin.GetFromJsonAsync<StationDetail>($"{Base}/stations/{stationB}");
        var activate = new HttpRequestMessage(HttpMethod.Post, $"{Base}/stations/{stationB}/activate");
        activate.Headers.TryAddWithoutValidation("If-Match", inactiveStation!.RowVersion);

        (await scoped.SendAsync(activate)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var after = await admin.GetFromJsonAsync<StationDetail>($"{Base}/stations/{stationB}");
        after!.IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task Customer_contact_only_sees_and_touches_its_own_customer()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var customerA = await Helpers.CreateCustomerWithContactAsync(admin);
        var customerB = await Helpers.CreateCustomerWithContactAsync(admin);

        var scoped = await ProvisionScopedContactClientAsync(admin, customerA.CustomerId, customerA.ContactId, customerA.Email);

        var customers = await scoped.GetFromJsonAsync<PagedList<CustomerItem>>($"{Base}/customers?pageSize=100");
        customers!.Items.Select(c => c.Id).ShouldBe([customerA.CustomerId]);

        (await scoped.GetAsync($"{Base}/customers/{customerB.CustomerId}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await scoped.GetAsync($"{Base}/customers/{customerA.CustomerId}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var ownCustomer = await scoped.GetFromJsonAsync<CustomerDetail>($"{Base}/customers/{customerA.CustomerId}");
        var addOwnContact = new HttpRequestMessage(HttpMethod.Post, $"{Base}/customers/{customerA.CustomerId}/contacts")
        {
            Content = JsonContent.Create(new
            {
                name = "Scoped Contact",
                jobTitle = (string?)null,
                email = $"scoped-contact-{Guid.NewGuid():N}@example.com",
                phone = (string?)null,
                portalAccessRoleId = (Guid?)null
            })
        };
        addOwnContact.Headers.TryAddWithoutValidation("If-Match", ownCustomer!.RowVersion);
        (await scoped.SendAsync(addOwnContact)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var otherCustomer = await admin.GetFromJsonAsync<CustomerDetail>($"{Base}/customers/{customerB.CustomerId}");
        var addCrossScopeContact = new HttpRequestMessage(HttpMethod.Post, $"{Base}/customers/{customerB.CustomerId}/contacts")
        {
            Content = JsonContent.Create(new
            {
                name = "Cross Scope Contact",
                jobTitle = (string?)null,
                email = $"cross-contact-{Guid.NewGuid():N}@example.com",
                phone = (string?)null,
                portalAccessRoleId = (Guid?)null
            })
        };
        addCrossScopeContact.Headers.TryAddWithoutValidation("If-Match", otherCustomer!.RowVersion);
        (await scoped.SendAsync(addCrossScopeContact)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Access_fails_closed_when_the_parent_customer_is_deactivated()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var customer = await Helpers.CreateCustomerWithContactAsync(admin);

        var scoped = await ProvisionScopedContactClientAsync(admin, customer.CustomerId, customer.ContactId, customer.Email);
        (await scoped.GetAsync($"{Base}/customers/{customer.CustomerId}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var detail = await admin.GetFromJsonAsync<CustomerDetail>($"{Base}/customers/{customer.CustomerId}");
        var deactivate = new HttpRequestMessage(HttpMethod.Post, $"{Base}/customers/{customer.CustomerId}/deactivate");
        deactivate.Headers.TryAddWithoutValidation("If-Match", detail!.RowVersion);
        (await admin.SendAsync(deactivate)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await scoped.GetAsync($"{Base}/customers/{customer.CustomerId}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await scoped.GetAsync($"{Base}/customers?pageSize=100")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Customer_contact_can_load_country_options_for_customer_edit_form()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var customer = await Helpers.CreateCustomerWithContactAsync(admin);
        var scoped = await ProvisionScopedContactClientAsync(admin, customer.CustomerId, customer.ContactId, customer.Email);

        var options = await scoped.GetFromJsonAsync<List<CountryOption>>($"{Base}/countries/options");

        options.ShouldNotBeNull();
        options!.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Customer_contact_cannot_release_portal_email_when_removing_contact()
    {
        var admin = await factory.CreateAuthenticatedAdminClientAsync();
        var customer = await Helpers.CreateCustomerWithContactAsync(admin);
        var scoped = await ProvisionScopedContactClientAsync(admin, customer.CustomerId, customer.ContactId, customer.Email);

        var ownCustomer = await scoped.GetFromJsonAsync<CustomerDetail>($"{Base}/customers/{customer.CustomerId}");
        var remove = new HttpRequestMessage(
            HttpMethod.Post,
            $"{Base}/customers/{customer.CustomerId}/contacts/{customer.ContactId}/remove?releaseEmail=true");
        remove.Headers.TryAddWithoutValidation("If-Match", ownCustomer!.RowVersion);

        (await scoped.SendAsync(remove)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private sealed record StationDetail(Guid Id, bool IsActive, string RowVersion);

    private async Task<HttpClient> ProvisionScopedStaffClientAsync(HttpClient admin, Guid staffId, string email)
    {
        var roleId = await Helpers.CreateRoleAsync(admin, "StationStaff",
            "masterdata.stations.view", "masterdata.stations.update",
            "masterdata.staff-members.view", "masterdata.staff-members.update");

        return await ProvisionScopedStaffClientAsync(admin, staffId, email, roleId);
    }

    private async Task<HttpClient> ProvisionScopedStaffClientWithSeededRoleAsync(HttpClient admin, Guid staffId, string email, params string[] permissions)
    {
        var roleResult = Role.Create(
            $"Legacy Scoped Role {Guid.NewGuid():N}",
            description: null,
            permissions,
            UserType.StationStaff,
            DateTimeOffset.UtcNow);
        roleResult.IsSuccess.ShouldBeTrue();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.Roles.Add(roleResult.Value);
            await db.SaveChangesAsync();
        }

        return await ProvisionScopedStaffClientAsync(admin, staffId, email, roleResult.Value.Id);
    }

    private async Task<HttpClient> ProvisionScopedStaffClientAsync(HttpClient admin, Guid staffId, string email, Guid roleId)
    {
        var staff = await admin.GetFromJsonAsync<StaffDetail>($"{Base}/staff-members/{staffId}");
        var grant = new HttpRequestMessage(HttpMethod.Post, $"{Base}/staff-members/{staffId}/grant-access")
        {
            Content = JsonContent.Create(new { roleId })
        };
        grant.Headers.TryAddWithoutValidation("If-Match", staff!.RowVersion);
        (await admin.SendAsync(grant)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await factory.DrainOutboxesAsync();

        var invitationToken = await factory.GetInvitationTokenAsync(email);
        invitationToken.ShouldNotBeNull();

        const string password = "Staff#12345";
        (await admin.PostAsJsonAsync($"{IdentityBase}/auth/activate",
            new { email, invitationToken, newPassword = password }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync($"{IdentityBase}/auth/login", new { email, password });
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        var token = await login.Content.ReadFromJsonAsync<TokenResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);
        return client;
    }

    private async Task<HttpClient> ProvisionScopedContactClientAsync(HttpClient admin, Guid customerId, Guid contactId, string email)
    {
        var roleId = await Helpers.CreateRoleAsync(admin, "CustomerContact",
            "masterdata.countries.view",
            "masterdata.customers.view", "masterdata.customers.update",
            "masterdata.customer-contacts.view", "masterdata.customer-contacts.create",
            "masterdata.customer-contacts.update", "masterdata.customer-contacts.remove");

        var customer = await admin.GetFromJsonAsync<CustomerDetail>($"{Base}/customers/{customerId}");
        var grant = new HttpRequestMessage(HttpMethod.Post, $"{Base}/customers/{customerId}/contacts/{contactId}/grant-access")
        {
            Content = JsonContent.Create(new { roleId })
        };
        grant.Headers.TryAddWithoutValidation("If-Match", customer!.RowVersion);
        (await admin.SendAsync(grant)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await factory.DrainOutboxesAsync();

        var invitationToken = await factory.GetInvitationTokenAsync(email);
        invitationToken.ShouldNotBeNull();

        const string password = "Contact#12345";
        (await admin.PostAsJsonAsync($"{IdentityBase}/auth/activate",
            new { email, invitationToken, newPassword = password }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync($"{IdentityBase}/auth/login", new { email, password });
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        var token = await login.Content.ReadFromJsonAsync<TokenResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);
        return client;
    }

    private static class Helpers
    {
        public static async Task<Guid> CreateRoleAsync(HttpClient client, string compatibleUserType, params string[] permissions)
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

        public static async Task<(Guid CustomerId, Guid ContactId, string Email)> CreateCustomerWithContactAsync(HttpClient client)
        {
            var email = $"contact-{Guid.NewGuid():N}@example.com";
            var customerId = await CreateCustomerAsync(client, email);
            var detail = await client.GetFromJsonAsync<CustomerDetail>($"{Base}/customers/{customerId}");
            var contactId = detail!.Contacts.Single(c => c.Email == email).Id;
            return (customerId, contactId, email);
        }

        public static async Task<Guid> CreateCustomerAsync(HttpClient client, string? contactEmail = null)
        {
            var countryId = await CreateCountryAsync(client);
            var create = await client.PostAsJsonAsync($"{Base}/customers", new
            {
                iataCode = (string?)null,
                icaoCode = (string?)null,
                name = $"Customer {Guid.NewGuid():N}",
                countryId,
                officialEmail = (string?)null,
                officialPhone = (string?)null,
                address = new
                {
                    line1 = "1 Airport Rd",
                    line2 = (string?)null,
                    city = "Amman",
                    region = (string?)null,
                    postalCode = (string?)null
                },
                contacts = contactEmail is null
                    ? Array.Empty<object>()
                    : new object[]
                    {
                        new
                        {
                            id = (Guid?)null,
                            name = "Scoped Contact",
                            jobTitle = (string?)null,
                            email = contactEmail,
                            phone = (string?)null,
                            portalAccessRoleId = (Guid?)null
                        }
                    }
            });
            create.StatusCode.ShouldBe(HttpStatusCode.Created);
            return await create.Content.ReadFromJsonAsync<Guid>();
        }

        public static async Task<(Guid Id, string Email)> CreateStaffAsync(HttpClient client, Guid stationId, Guid manpowerTypeId)
        {
            var email = $"staff-{Guid.NewGuid():N}@example.com";
            var create = await client.PostAsJsonAsync($"{Base}/staff-members", new
            {
                fullName = "Scoped Staff",
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

        public static async Task<Guid> CreateStationAsync(HttpClient client)
        {
            var countryId = await CreateCountryAsync(client);
            var iata = await UnusedTripletAsync(client);
            var create = await client.PostAsJsonAsync($"{Base}/stations",
                new { iataCode = iata, icaoCode = (string?)null, name = $"Station {iata}", city = "City", countryId });
            create.StatusCode.ShouldBe(HttpStatusCode.Created);
            return await create.Content.ReadFromJsonAsync<Guid>();
        }

        public static async Task<Guid> CreateManpowerTypeAsync(HttpClient client)
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

        private sealed record CodeItem(Guid Id, string IataCode);
        private sealed record CountryItem(Guid Id, string IsoCode);

        private static async Task<string> UnusedTripletAsync(HttpClient client)
        {
            var used = await CollectAsync<CodeItem>(client, $"{Base}/stations", c => c.IataCode);
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
}
