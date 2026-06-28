using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace MasterData.IntegrationTests;

/// <summary>
/// Server-side data scope for a provisioned StationStaff account: it may only read/act within its
/// linked station, and access fails closed once the linked record or parent station is inactive.
/// </summary>
public class DataScopeIntegrationTests(MasterDataApiFactory factory) : IClassFixture<MasterDataApiFactory>
{
    private const string Base = MasterDataApiFactory.Base;
    private const string IdentityBase = MasterDataApiFactory.IdentityBase;

    private sealed record TokenResponse(string AccessToken, DateTimeOffset ExpiresAtUtc);
    private sealed record PagedList<T>(List<T> Items, int Page, int PageSize, long TotalCount);
    private sealed record StaffItem(Guid Id, string FullName, Guid StationId);
    private sealed record StationItem(Guid Id, string IataCode);

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

    private sealed record StationDetail(Guid Id, string RowVersion);

    private async Task<HttpClient> ProvisionScopedStaffClientAsync(HttpClient admin, Guid staffId, string email)
    {
        var roleId = await Helpers.CreateRoleAsync(admin, "StationStaff",
            "masterdata.stations.view", "masterdata.stations.update",
            "masterdata.staff-members.view", "masterdata.staff-members.update");

        (await admin.PostAsJsonAsync($"{Base}/staff-members/{staffId}/grant-access", new { roleId }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
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
