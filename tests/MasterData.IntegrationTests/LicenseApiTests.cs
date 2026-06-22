using System.Net;
using System.Net.Http.Json;
using Shouldly;

namespace MasterData.IntegrationTests;

public class LicenseApiTests(MasterDataApiFactory factory) : IClassFixture<MasterDataApiFactory>
{
    private const string Base = MasterDataApiFactory.Base;

    private sealed record PagedList<T>(List<T> Items, int Page, int PageSize, long TotalCount);
    private sealed record ListItem(Guid Id, string Code, string Name, string? Description, bool IsActive);
    private sealed record Detail(Guid Id, string Code, string Name, string? Description, bool IsActive,
        DateTimeOffset CreatedAtUtc, DateTimeOffset? UpdatedAtUtc, string RowVersion);

    [Fact]
    public async Task Licenses_without_token_returns_401()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync($"{Base}/licenses");
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Create_uppercases_code_and_round_trips()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var code = await UnusedCodeAsync(client);

        var create = await client.PostAsJsonAsync($"{Base}/licenses",
            new { code = code.ToLowerInvariant(), name = $"License {code}", description = (string?)null });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = await create.Content.ReadFromJsonAsync<Guid>();

        var detail = await client.GetFromJsonAsync<Detail>($"{Base}/licenses/{id}");
        detail!.Code.ShouldBe(code);
    }

    [Fact]
    public async Task Create_duplicate_code_returns_409()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var code = await UnusedCodeAsync(client);

        (await client.PostAsJsonAsync($"{Base}/licenses", new { code, name = $"First {code}", description = (string?)null }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var second = await client.PostAsJsonAsync($"{Base}/licenses", new { code, name = $"Second {code}", description = (string?)null });
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Create_with_invalid_code_returns_400()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();

        var response = await client.PostAsJsonAsync($"{Base}/licenses", new { code = "A-1", name = "Bad", description = (string?)null });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_with_if_match_changes_name_but_not_code()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var id = await CreateAsync(client);
        var before = await client.GetFromJsonAsync<Detail>($"{Base}/licenses/{id}");

        var request = new HttpRequestMessage(HttpMethod.Put, $"{Base}/licenses/{id}")
        {
            Content = JsonContent.Create(new { name = "Renamed License", description = "Updated" })
        };
        request.Headers.TryAddWithoutValidation("If-Match", before!.RowVersion);
        (await client.SendAsync(request)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var after = await client.GetFromJsonAsync<Detail>($"{Base}/licenses/{id}");
        after!.Name.ShouldBe("Renamed License");
        after.Code.ShouldBe(before.Code);
    }

    private static async Task<Guid> CreateAsync(HttpClient client)
    {
        var code = await UnusedCodeAsync(client);
        var create = await client.PostAsJsonAsync($"{Base}/licenses",
            new { code, name = $"License {code}", description = (string?)null });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await create.Content.ReadFromJsonAsync<Guid>();
    }

    // License codes are 2-10 alphanumeric chars; use a unique 4-char code to avoid collisions.
    private static async Task<string> UnusedCodeAsync(HttpClient client)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        const int pageSize = 100;
        var page = 1;
        long total;
        do
        {
            var list = await client.GetFromJsonAsync<PagedList<ListItem>>($"{Base}/licenses?page={page}&pageSize={pageSize}");
            foreach (var item in list!.Items)
                used.Add(item.Code);
            total = list.TotalCount;
            page++;
        }
        while ((page - 1) * pageSize < total);

        for (var i = 0; i < 100000; i++)
        {
            var code = $"L{i:D3}";
            if (used.Add(code))
                return code;
        }

        throw new InvalidOperationException("No unused license code remains for the test.");
    }
}
