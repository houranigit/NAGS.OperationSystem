using System.Net;
using System.Net.Http.Json;
using Shouldly;

namespace MasterData.IntegrationTests;

public class StationApiTests(MasterDataApiFactory factory) : IClassFixture<MasterDataApiFactory>
{
    private const string Base = MasterDataApiFactory.Base;

    private sealed record PagedList<T>(List<T> Items, int Page, int PageSize, long TotalCount);
    private sealed record StationItem(Guid Id, string IataCode, string? IcaoCode, string Name, string City, Guid CountryId, string CountryName, bool IsActive);
    private sealed record StationDetail(Guid Id, string IataCode, string? IcaoCode, string Name, string City, Guid CountryId, string CountryName, bool IsActive,
        DateTimeOffset CreatedAtUtc, DateTimeOffset? UpdatedAtUtc, string RowVersion);
    private sealed record CountryDetail(Guid Id, string Name, string IsoCode, bool IsActive, DateTimeOffset CreatedAtUtc, DateTimeOffset? UpdatedAtUtc, string RowVersion);

    [Fact]
    public async Task Stations_without_token_returns_401()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync($"{Base}/stations");
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Create_then_get_round_trips_with_country_name()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var countryId = await EnsureActiveCountryAsync(client);
        var iata = await UnusedIataAsync(client);

        var create = await client.PostAsJsonAsync($"{Base}/stations",
            new { iataCode = iata.ToLowerInvariant(), icaoCode = (string?)null, name = "Test Station", city = "Test City", countryId });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = await create.Content.ReadFromJsonAsync<Guid>();

        var detail = await client.GetFromJsonAsync<StationDetail>($"{Base}/stations/{id}");
        detail!.IataCode.ShouldBe(iata);
        detail.CountryId.ShouldBe(countryId);
        detail.CountryName.ShouldNotBeNullOrWhiteSpace();
        detail.IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task Create_with_duplicate_iata_returns_409()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var countryId = await EnsureActiveCountryAsync(client);
        var iata = await UnusedIataAsync(client);

        (await client.PostAsJsonAsync($"{Base}/stations",
            new { iataCode = iata, icaoCode = (string?)null, name = "First", city = "City", countryId }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var second = await client.PostAsJsonAsync($"{Base}/stations",
            new { iataCode = iata, icaoCode = (string?)null, name = "Second", city = "City", countryId });
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Create_with_inactive_country_is_rejected()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var countryId = await CreateCountryAsync(client);

        // Deactivate the country first.
        var country = await client.GetFromJsonAsync<CountryDetail>($"{MasterDataApiFactory.Base}/countries/{countryId}");
        var deactivate = new HttpRequestMessage(HttpMethod.Post, $"{MasterDataApiFactory.Base}/countries/{countryId}/deactivate");
        deactivate.Headers.TryAddWithoutValidation("If-Match", country!.RowVersion);
        (await client.SendAsync(deactivate)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var iata = await UnusedIataAsync(client);
        var create = await client.PostAsJsonAsync($"{Base}/stations",
            new { iataCode = iata, icaoCode = (string?)null, name = "Test", city = "City", countryId });

        create.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_with_if_match_succeeds()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var countryId = await EnsureActiveCountryAsync(client);
        var iata = await UnusedIataAsync(client);
        var id = await CreateStationAsync(client, iata, countryId);

        var before = await client.GetFromJsonAsync<StationDetail>($"{Base}/stations/{id}");
        var request = new HttpRequestMessage(HttpMethod.Put, $"{Base}/stations/{id}")
        {
            Content = JsonContent.Create(new { iataCode = iata, icaoCode = (string?)null, name = "Renamed Station", city = "New City", countryId })
        };
        request.Headers.TryAddWithoutValidation("If-Match", before!.RowVersion);
        (await client.SendAsync(request)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var after = await client.GetFromJsonAsync<StationDetail>($"{Base}/stations/{id}");
        after!.Name.ShouldBe("Renamed Station");
        after.City.ShouldBe("New City");
    }

    private sealed record StaffItem(Guid Id, string FullName, string Email);

    [Fact]
    public async Task Create_station_with_staff_is_atomic()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var countryId = await EnsureActiveCountryAsync(client);
        var iata = await UnusedIataAsync(client);
        var manpowerTypeId = await CreateManpowerTypeAsync(client);

        var create = await client.PostAsJsonAsync($"{Base}/stations", new
        {
            iataCode = iata,
            icaoCode = (string?)null,
            name = "Atomic Station",
            city = "City",
            countryId,
            staff = new[]
            {
                new { fullName = "Staff One", email = $"one-{iata}@test.com", manpowerTypeId, employmentContract = (object?)null, workingDays = (string[]?)null, licenses = Array.Empty<object>(), portalAccessRoleId = (Guid?)null },
                new { fullName = "Staff Two", email = $"two-{iata}@test.com", manpowerTypeId, employmentContract = (object?)null, workingDays = (string[]?)null, licenses = Array.Empty<object>(), portalAccessRoleId = (Guid?)null }
            }
        });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var stationId = await create.Content.ReadFromJsonAsync<Guid>();

        var staff = await client.GetFromJsonAsync<PagedList<StaffItem>>($"{Base}/staff-members?stationId={stationId}&pageSize=100");
        staff!.Items.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Create_station_with_invalid_staff_rolls_back_the_station()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var countryId = await EnsureActiveCountryAsync(client);
        var iata = await UnusedIataAsync(client);

        // One child references a non-existent manpower type; the whole create must fail and persist nothing.
        var create = await client.PostAsJsonAsync($"{Base}/stations", new
        {
            iataCode = iata,
            icaoCode = (string?)null,
            name = "Rollback Station",
            city = "City",
            countryId,
            staff = new[]
            {
                new { fullName = "Bad Staff", email = $"bad-{iata}@test.com", manpowerTypeId = Guid.NewGuid(), employmentContract = (object?)null, workingDays = (string[]?)null, licenses = Array.Empty<object>(), portalAccessRoleId = (Guid?)null }
            }
        });
        create.StatusCode.ShouldBeOneOf(HttpStatusCode.NotFound, HttpStatusCode.BadRequest);

        // The station must not exist (the failed nested create rolled back).
        var stations = await client.GetFromJsonAsync<PagedList<StationItem>>($"{Base}/stations?pageSize=100&search={iata}");
        stations!.Items.ShouldNotContain(s => s.IataCode == iata);
    }

    private static async Task<Guid> CreateManpowerTypeAsync(HttpClient client)
    {
        var create = await client.PostAsJsonAsync($"{MasterDataApiFactory.Base}/manpower-types",
            new { name = $"MT {Guid.NewGuid():N}", description = (string?)null });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await create.Content.ReadFromJsonAsync<Guid>();
    }

    private static async Task<Guid> CreateStationAsync(HttpClient client, string iata, Guid countryId)
    {
        var create = await client.PostAsJsonAsync($"{Base}/stations",
            new { iataCode = iata, icaoCode = (string?)null, name = $"Station {iata}", city = "City", countryId });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await create.Content.ReadFromJsonAsync<Guid>();
    }

    private static async Task<Guid> EnsureActiveCountryAsync(HttpClient client) => await CreateCountryAsync(client);

    private static async Task<Guid> CreateCountryAsync(HttpClient client)
    {
        var code = await UnusedCountryCodeAsync(client);
        var create = await client.PostAsJsonAsync($"{MasterDataApiFactory.Base}/countries",
            new { name = $"Country {code}", isoCode = code });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await create.Content.ReadFromJsonAsync<Guid>();
    }

    private static async Task<string> UnusedIataAsync(HttpClient client)
    {
        var used = await CollectAsync<StationItem>(client, $"{Base}/stations", s => s.IataCode);
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
        var used = await CollectAsync<CountryItem>(client, $"{MasterDataApiFactory.Base}/countries", c => c.IsoCode);
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

    private sealed record CountryItem(Guid Id, string Name, string IsoCode, bool IsActive);
}
