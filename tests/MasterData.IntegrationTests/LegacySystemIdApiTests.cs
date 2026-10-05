using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using MasterData.Contracts.Seeding;
using MasterData.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace MasterData.IntegrationTests;

public sealed class LegacySystemIdApiTests(MasterDataApiFactory factory) : IClassFixture<MasterDataApiFactory>
{
    private const string Base = MasterDataApiFactory.Base;
    private static int _stationSequence;

    [Theory]
    [InlineData("services")]
    [InlineData("tools")]
    [InlineData("materials")]
    [InlineData("general-supports")]
    [InlineData("customers")]
    [InlineData("stations")]
    [InlineData("manpower-types")]
    [InlineData("operation-types")]
    [InlineData("aircraft-types")]
    [InlineData("staff-members")]
    public async Task Legacy_mapping_round_trips_preserves_omitted_values_and_clears_explicit_blank(string route)
    {
        using var client = await factory.CreateAuthenticatedAdminClientAsync();
        var payload = await BuildPayloadAsync(client, route);
        payload["legacySystemId"] = "  00123  ";
        var create = await client.PostAsJsonAsync($"{Base}/{route}", payload);
        create.StatusCode.ShouldBe(HttpStatusCode.Created, await create.Content.ReadAsStringAsync());
        var id = await create.Content.ReadFromJsonAsync<Guid>();
        var url = $"{Base}/{route}/{id}";
        var detail = await DetailAsync(client, url);
        detail.GetProperty("legacySystemId").GetString().ShouldBe("00123");
        if (route == "tools")
            payload["equipments"] = detail.GetProperty("equipments").Clone();

        payload.Remove("legacySystemId");
        await UpdateAsync(client, url, detail, payload);
        detail = await DetailAsync(client, url);
        detail.GetProperty("legacySystemId").GetString().ShouldBe("00123");

        payload["legacySystemId"] = "  XYZ123  ";
        await UpdateAsync(client, url, detail, payload);
        detail = await DetailAsync(client, url);
        detail.GetProperty("legacySystemId").GetString().ShouldBe("XYZ123");

        payload["legacySystemId"] = string.Empty;
        await UpdateAsync(client, url, detail, payload);
        detail = await DetailAsync(client, url);
        detail.GetProperty("legacySystemId").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Protected_per_landing_mapping_is_editable_but_its_name_and_lifecycle_stay_protected()
    {
        using var client = await factory.CreateAuthenticatedAdminClientAsync();
        var url = $"{Base}/services/{WellKnownMasterDataIds.AircraftPerLandingService}";
        var before = await DetailAsync(client, url);
        var mapping = new Dictionary<string, object?>
        {
            ["name"] = before.GetProperty("name").GetString(),
            ["description"] = before.GetProperty("description").Clone(),
            ["legacySystemId"] = "PL-OLD"
        };
        await UpdateAsync(client, url, before, mapping);
        var detail = await DetailAsync(client, url);
        detail.GetProperty("legacySystemId").GetString().ShouldBe("PL-OLD");
        detail.GetProperty("name").GetString().ShouldBe("Aircraft Per Landing");

        mapping["name"] = "Renamed protected service";
        using var update = new HttpRequestMessage(HttpMethod.Put, url) { Content = JsonContent.Create(mapping) };
        update.Headers.TryAddWithoutValidation("If-Match", detail.GetProperty("rowVersion").GetString());
        (await client.SendAsync(update)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using var deactivate = new HttpRequestMessage(HttpMethod.Post, url + "/deactivate");
        deactivate.Headers.TryAddWithoutValidation("If-Match", detail.GetProperty("rowVersion").GetString());
        (await client.SendAsync(deactivate)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Migration_adds_ten_nullable_200_character_columns_and_preserves_existing_rows_with_null_mapping()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MasterDataDbContext>();
        var migrator = db.Database.GetService<IMigrator>();
        var serviceId = Guid.NewGuid();
        await migrator.MigrateAsync("20260920192822_MasterData_AtaChapters");
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($$"""
                INSERT INTO [masterdata].[services] ([Id], [Name], [IsActive], [CreatedAtUtc])
                VALUES ({{serviceId}}, {{"Pre-migration " + serviceId.ToString("N")}}, 1, SYSUTCDATETIME());
                """);
            var script = migrator.GenerateScript(
                "20260920192822_MasterData_AtaChapters",
                "20261005005402_MasterData_LegacySystemIds",
                MigrationsSqlGenerationOptions.Idempotent);
            for (var attempt = 0; attempt < 2; attempt++)
            {
                foreach (var batch in Regex.Split(script, @"(?im)^\s*GO\s*$").Where(batch => !string.IsNullOrWhiteSpace(batch)))
                    await db.Database.ExecuteSqlRawAsync(batch);
            }
            var service = await db.Services.AsNoTracking().SingleAsync(x => x.Id == serviceId);
            service.LegacySystemId.ShouldBeNull();
            service.IsActive.ShouldBeTrue();

            var mappedColumnCount = await db.Database.SqlQueryRaw<int>("""
                SELECT COUNT(*) AS [Value]
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA = 'masterdata' AND COLUMN_NAME = 'LegacySystemId'
                  AND IS_NULLABLE = 'YES' AND DATA_TYPE = 'nvarchar' AND CHARACTER_MAXIMUM_LENGTH = 200
                """).SingleAsync();
            mappedColumnCount.ShouldBe(10);
        }
        finally
        {
            await db.Database.MigrateAsync();
        }
    }

    private static async Task<Dictionary<string, object?>> BuildPayloadAsync(HttpClient client, string route)
    {
        var unique = Guid.NewGuid().ToString("N");
        var payload = new Dictionary<string, object?> { ["name"] = $"Legacy mapping {route} {unique}", ["description"] = null };
        if (route is "customers" or "stations" or "staff-members")
        {
            var countries = await client.GetFromJsonAsync<JsonElement>($"{Base}/countries/options");
            var countryId = countries.EnumerateArray().First(x => x.GetProperty("isoCode").GetString() == "SA")
                .GetProperty("id").GetGuid();
            if (route == "customers")
            {
                payload["countryId"] = countryId;
                payload["address"] = new { line1 = "Airport Road", city = "Riyadh" };
            }
            else if (route == "stations")
            {
                payload["countryId"] = countryId;
                payload["iataCode"] = NextStationCode();
            }
            else
            {
                var stationId = await CreateAsync(client, "stations", new
                {
                    iataCode = NextStationCode(), name = "Legacy staff station " + unique, countryId
                });
                var manpowerTypeId = await CreateAsync(client, "manpower-types", new { name = "Legacy staff type " + unique });
                payload.Remove("name");
                payload["fullName"] = "Legacy staff " + unique;
                payload["employeeId"] = "EMP-" + unique;
                payload["email"] = unique + "@example.com";
                payload["stationId"] = stationId;
                payload["manpowerTypeId"] = manpowerTypeId;
                payload["licenses"] = Array.Empty<object>();
            }
        }
        if (route == "aircraft-types")
        {
            payload.Remove("name");
            payload["manufacturer"] = "Airbus";
            payload["model"] = "A" + unique[..10];
        }
        if (route == "tools")
            payload["equipments"] = new[] { new { factoryId = "F-001", serialId = "S-001", calibrationDate = (DateOnly?)null } };
        return payload;
    }

    private static string NextStationCode()
    {
        var sequence = Interlocked.Increment(ref _stationSequence);
        return $"L{(char)('A' + sequence / 26)}{(char)('A' + sequence % 26)}";
    }

    private static async Task<Guid> CreateAsync(HttpClient client, string route, object payload)
    {
        var response = await client.PostAsJsonAsync($"{Base}/{route}", payload);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    private static async Task<JsonElement> DetailAsync(HttpClient client, string url) =>
        await client.GetFromJsonAsync<JsonElement>(url);

    private static async Task UpdateAsync(HttpClient client, string url, JsonElement before, object payload)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, url) { Content = JsonContent.Create(payload) };
        request.Headers.TryAddWithoutValidation("If-Match", before.GetProperty("rowVersion").GetString());
        var response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
    }
}
