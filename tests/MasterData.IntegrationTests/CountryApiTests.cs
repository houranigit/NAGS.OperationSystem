using System.Net;
using System.Net.Http.Json;
using Shouldly;

namespace MasterData.IntegrationTests;

public class CountryApiTests(MasterDataApiFactory factory) : IClassFixture<MasterDataApiFactory>
{
    private const string Base = MasterDataApiFactory.Base;

    private sealed record PagedList<T>(List<T> Items, int Page, int PageSize, long TotalCount);
    private sealed record CountryItem(Guid Id, string Name, string IsoCode, bool IsActive);
    private sealed record CountryDetail(Guid Id, string Name, string IsoCode, bool IsActive,
        DateTimeOffset CreatedAtUtc, DateTimeOffset? UpdatedAtUtc, string RowVersion);
    private sealed record CountryOption(Guid Id, string Name, string IsoCode);

    [Fact]
    public async Task Countries_without_token_returns_401()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync($"{Base}/countries");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Seeded_iso_baseline_is_returned()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();

        var list = await client.GetFromJsonAsync<PagedList<CountryItem>>($"{Base}/countries?pageSize=1");

        // The ISO baseline seed inserts well over 100 countries.
        list!.TotalCount.ShouldBeGreaterThan(100);

        var jordan = await client.GetFromJsonAsync<PagedList<CountryItem>>($"{Base}/countries?search=JO&pageSize=100");
        jordan!.Items.ShouldContain(c => c.IsoCode == "JO");
    }

    [Fact]
    public async Task Create_then_get_round_trips()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var code = await UnusedCodeAsync(client);

        var create = await client.PostAsJsonAsync($"{Base}/countries", new { name = $"Test {code}", isoCode = code });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = await create.Content.ReadFromJsonAsync<Guid>();

        var detail = await client.GetFromJsonAsync<CountryDetail>($"{Base}/countries/{id}");
        detail!.IsoCode.ShouldBe(code);
        detail.IsActive.ShouldBeTrue();
        detail.RowVersion.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Create_duplicate_iso_code_returns_409()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var code = await UnusedCodeAsync(client);

        var first = await client.PostAsJsonAsync($"{Base}/countries", new { name = $"First {code}", isoCode = code });
        first.StatusCode.ShouldBe(HttpStatusCode.Created);

        var second = await client.PostAsJsonAsync($"{Base}/countries", new { name = $"Second {code}", isoCode = code });
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Update_without_if_match_is_rejected()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var id = await CreateCountryAsync(client);
        var detail = await client.GetFromJsonAsync<CountryDetail>($"{Base}/countries/{id}");

        var response = await client.PutAsJsonAsync($"{Base}/countries/{id}", new { name = "Renamed", isoCode = detail!.IsoCode });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_with_if_match_succeeds_and_changes_rowversion()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var id = await CreateCountryAsync(client);

        var before = await client.GetFromJsonAsync<CountryDetail>($"{Base}/countries/{id}");

        var request = new HttpRequestMessage(HttpMethod.Put, $"{Base}/countries/{id}")
        {
            Content = JsonContent.Create(new { name = "Renamed Country", isoCode = before!.IsoCode })
        };
        request.Headers.TryAddWithoutValidation("If-Match", before.RowVersion);

        var response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var after = await client.GetFromJsonAsync<CountryDetail>($"{Base}/countries/{id}");
        after!.Name.ShouldBe("Renamed Country");
        after.RowVersion.ShouldNotBe(before.RowVersion);
    }

    [Fact]
    public async Task Update_with_stale_if_match_returns_409()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var id = await CreateCountryAsync(client);

        var before = await client.GetFromJsonAsync<CountryDetail>($"{Base}/countries/{id}");

        // First update consumes the current rowversion.
        var first = new HttpRequestMessage(HttpMethod.Put, $"{Base}/countries/{id}")
        {
            Content = JsonContent.Create(new { name = "First Rename", isoCode = before!.IsoCode })
        };
        first.Headers.TryAddWithoutValidation("If-Match", before.RowVersion);
        (await client.SendAsync(first)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Second update reuses the now-stale rowversion.
        var second = new HttpRequestMessage(HttpMethod.Put, $"{Base}/countries/{id}")
        {
            Content = JsonContent.Create(new { name = "Second Rename", isoCode = before.IsoCode })
        };
        second.Headers.TryAddWithoutValidation("If-Match", before.RowVersion);
        (await client.SendAsync(second)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Deactivate_removes_country_from_active_options()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var id = await CreateCountryAsync(client);
        var detail = await client.GetFromJsonAsync<CountryDetail>($"{Base}/countries/{id}");

        var deactivate = new HttpRequestMessage(HttpMethod.Post, $"{Base}/countries/{id}/deactivate");
        deactivate.Headers.TryAddWithoutValidation("If-Match", detail!.RowVersion);
        (await client.SendAsync(deactivate)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var options = await client.GetFromJsonAsync<List<CountryOption>>($"{Base}/countries/options");
        options!.ShouldNotContain(o => o.Id == id);
    }

    private static async Task<Guid> CreateCountryAsync(HttpClient client)
    {
        var code = await UnusedCodeAsync(client);
        var create = await client.PostAsJsonAsync($"{Base}/countries", new { name = $"Country {code}", isoCode = code });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await create.Content.ReadFromJsonAsync<Guid>();
    }

    // ISO codes are 2 letters; pick a combination not yet present so creates never collide with seeds.
    // The list endpoint clamps page size to 100, so page through every row to build the used-code set.
    private static async Task<string> UnusedCodeAsync(HttpClient client)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        const int pageSize = 100;
        var page = 1;
        long total;
        do
        {
            var list = await client.GetFromJsonAsync<PagedList<CountryItem>>($"{Base}/countries?page={page}&pageSize={pageSize}");
            foreach (var item in list!.Items)
                used.Add(item.IsoCode);
            total = list.TotalCount;
            page++;
        }
        while ((page - 1) * pageSize < total);

        const string letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        foreach (var first in letters)
        {
            foreach (var second in letters)
            {
                var code = $"{first}{second}";
                if (used.Add(code))
                    return code;
            }
        }

        throw new InvalidOperationException("No unused 2-letter ISO code remains for the test.");
    }
}
