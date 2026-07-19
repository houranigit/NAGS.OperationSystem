using System.Net;
using System.Net.Http.Json;
using Shouldly;

namespace MasterData.IntegrationTests;

public class ManpowerTypeApiTests(MasterDataApiFactory factory) : IClassFixture<MasterDataApiFactory>
{
    private const string Base = MasterDataApiFactory.Base;

    private sealed record Detail(Guid Id, string Name, string? Description, bool IsActive,
        DateTimeOffset CreatedAtUtc, DateTimeOffset? UpdatedAtUtc, string RowVersion);
    private sealed record Option(Guid Id, string Name);
    private sealed record ServiceDetail(Guid Id, string RowVersion);
    private sealed record ServiceAllowance(Guid ServiceId, string Name, bool IsActive, bool IsAllowed);
    private sealed record ManpowerTypeAllowance(Guid ManpowerTypeId, string Name, bool IsActive, bool IsAllowed);

    [Fact]
    public async Task Manpower_types_without_token_returns_401()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync($"{Base}/manpower-types");
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Create_then_get_round_trips()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var name = $"Mechanic {Guid.NewGuid():N}";

        var create = await client.PostAsJsonAsync($"{Base}/manpower-types", new { name, description = "Test" });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = await create.Content.ReadFromJsonAsync<Guid>();

        var detail = await client.GetFromJsonAsync<Detail>($"{Base}/manpower-types/{id}");
        detail!.Name.ShouldBe(name);
        detail.IsActive.ShouldBeTrue();
        detail.RowVersion.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Create_duplicate_name_returns_409()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var name = $"Loadmaster {Guid.NewGuid():N}";

        (await client.PostAsJsonAsync($"{Base}/manpower-types", new { name, description = (string?)null }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var second = await client.PostAsJsonAsync($"{Base}/manpower-types", new { name, description = (string?)null });
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Update_without_if_match_is_rejected()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var id = await CreateAsync(client);

        var response = await client.PutAsJsonAsync($"{Base}/manpower-types/{id}", new { name = "Renamed", description = (string?)null });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_with_if_match_succeeds()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var id = await CreateAsync(client);
        var before = await client.GetFromJsonAsync<Detail>($"{Base}/manpower-types/{id}");

        var newName = $"Updated {Guid.NewGuid():N}";
        var request = new HttpRequestMessage(HttpMethod.Put, $"{Base}/manpower-types/{id}")
        {
            Content = JsonContent.Create(new { name = newName, description = "Updated" })
        };
        request.Headers.TryAddWithoutValidation("If-Match", before!.RowVersion);
        (await client.SendAsync(request)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var after = await client.GetFromJsonAsync<Detail>($"{Base}/manpower-types/{id}");
        after!.Name.ShouldBe(newName);
        after.RowVersion.ShouldNotBe(before.RowVersion);
    }

    [Fact]
    public async Task Deactivate_removes_from_active_options()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var id = await CreateAsync(client);
        var detail = await client.GetFromJsonAsync<Detail>($"{Base}/manpower-types/{id}");

        var deactivate = new HttpRequestMessage(HttpMethod.Post, $"{Base}/manpower-types/{id}/deactivate");
        deactivate.Headers.TryAddWithoutValidation("If-Match", detail!.RowVersion);
        (await client.SendAsync(deactivate)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var options = await client.GetFromJsonAsync<List<Option>>($"{Base}/manpower-types/options");
        options!.ShouldNotContain(o => o.Id == id);
    }

    [Fact]
    public async Task Service_allowances_start_empty_and_reconcile_symmetrically_from_both_detail_pages()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var manpowerTypeId = await CreateAsync(client);
        var serviceCreate = await client.PostAsJsonAsync($"{Base}/services",
            new { name = $"Service {Guid.NewGuid():N}", description = (string?)null });
        serviceCreate.StatusCode.ShouldBe(HttpStatusCode.Created);
        var serviceId = await serviceCreate.Content.ReadFromJsonAsync<Guid>();

        var initial = await client.GetFromJsonAsync<List<ServiceAllowance>>(
            $"{Base}/manpower-types/{manpowerTypeId}/service-allowances");
        initial!.Single(item => item.ServiceId == serviceId).IsAllowed.ShouldBeFalse();
        var serviceBefore = await client.GetFromJsonAsync<ServiceDetail>($"{Base}/services/{serviceId}");

        var manpowerType = await client.GetFromJsonAsync<Detail>($"{Base}/manpower-types/{manpowerTypeId}");
        var allow = new HttpRequestMessage(HttpMethod.Put, $"{Base}/manpower-types/{manpowerTypeId}/service-allowances")
        {
            Content = JsonContent.Create(new { serviceIds = new[] { serviceId } })
        };
        allow.Headers.TryAddWithoutValidation("If-Match", manpowerType!.RowVersion);
        (await client.SendAsync(allow)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var fromService = await client.GetFromJsonAsync<List<ManpowerTypeAllowance>>(
            $"{Base}/services/{serviceId}/manpower-type-allowances");
        fromService!.Single(item => item.ManpowerTypeId == manpowerTypeId).IsAllowed.ShouldBeTrue();

        var staleOppositeSide = new HttpRequestMessage(HttpMethod.Put, $"{Base}/services/{serviceId}/manpower-type-allowances")
        {
            Content = JsonContent.Create(new { manpowerTypeIds = Array.Empty<Guid>() })
        };
        staleOppositeSide.Headers.TryAddWithoutValidation("If-Match", serviceBefore!.RowVersion);
        (await client.SendAsync(staleOppositeSide)).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var service = await client.GetFromJsonAsync<ServiceDetail>($"{Base}/services/{serviceId}");
        var clear = new HttpRequestMessage(HttpMethod.Put, $"{Base}/services/{serviceId}/manpower-type-allowances")
        {
            Content = JsonContent.Create(new { manpowerTypeIds = Array.Empty<Guid>() })
        };
        clear.Headers.TryAddWithoutValidation("If-Match", service!.RowVersion);
        (await client.SendAsync(clear)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var cleared = await client.GetFromJsonAsync<List<ServiceAllowance>>(
            $"{Base}/manpower-types/{manpowerTypeId}/service-allowances");
        cleared!.Single(item => item.ServiceId == serviceId).IsAllowed.ShouldBeFalse();
    }

    [Fact]
    public async Task Allowance_update_rejects_a_stale_parent_version()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var manpowerTypeId = await CreateAsync(client);
        var before = await client.GetFromJsonAsync<Detail>($"{Base}/manpower-types/{manpowerTypeId}");

        async Task<HttpResponseMessage> SendAsync()
        {
            var request = new HttpRequestMessage(HttpMethod.Put, $"{Base}/manpower-types/{manpowerTypeId}/service-allowances")
            {
                Content = JsonContent.Create(new { serviceIds = Array.Empty<Guid>() })
            };
            request.Headers.TryAddWithoutValidation("If-Match", before!.RowVersion);
            return await client.SendAsync(request);
        }

        (await SendAsync()).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await SendAsync()).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    private static async Task<Guid> CreateAsync(HttpClient client)
    {
        var create = await client.PostAsJsonAsync($"{Base}/manpower-types",
            new { name = $"Type {Guid.NewGuid():N}", description = (string?)null });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await create.Content.ReadFromJsonAsync<Guid>();
    }
}
