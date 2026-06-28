using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Shouldly;

namespace MasterData.IntegrationTests;

public class StaffMemberApiTests(MasterDataApiFactory factory) : IClassFixture<MasterDataApiFactory>
{
    private const string Base = MasterDataApiFactory.Base;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private sealed record PagedList<T>(List<T> Items, int Page, int PageSize, long TotalCount);
    private sealed record StaffItem(Guid Id, string FullName, string EmployeeId, string Email, Guid StationId, string StationCode, Guid ManpowerTypeId, string ManpowerTypeName, bool IsActive);
    private sealed record ContractBody(DateOnly StartDate, DateOnly? EndDate);
    private sealed record LicenseBody(Guid Id, Guid LicenseId, string LicenseCode, string LicenseName, string LicenseNumber);
    private sealed record StaffDetail(Guid Id, string FullName, string EmployeeId, string Email, Guid StationId, string StationCode, string StationName,
        Guid ManpowerTypeId, string ManpowerTypeName, ContractBody? EmploymentContract, List<DayOfWeek>? WorkingDays,
        Guid? LinkedUserId, bool IsActive, DateTimeOffset CreatedAtUtc, DateTimeOffset? UpdatedAtUtc, string RowVersion, List<LicenseBody> Licenses);
    private sealed record CountryItem(Guid Id, string Name, string IsoCode, bool IsActive);
    private sealed record StationItem(Guid Id, string IataCode, string? IcaoCode, string Name, string City, Guid CountryId, string CountryName, bool IsActive);
    private sealed record LicenseItem(Guid Id, string Code, string Name, bool IsActive);

    [Fact]
    public async Task StaffMembers_without_token_returns_401()
    {
        var client = factory.CreateClient();
        (await client.GetAsync($"{Base}/staff-members")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Create_with_contract_schedule_and_licenses_round_trips()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var stationId = await CreateStationAsync(client);
        var manpowerTypeId = await CreateManpowerTypeAsync(client);
        var licenseId = await CreateLicenseAsync(client);
        var email = UniqueEmail();

        var create = await client.PostAsJsonAsync($"{Base}/staff-members", new
        {
            fullName = "Jane Technician",
            employeeId = $"EMP-{Guid.NewGuid():N}",
            email,
            stationId,
            manpowerTypeId,
            employmentContract = new { startDate = "2026-01-01", endDate = "2026-12-31" },
            workingDays = new[] { "Sunday", "Monday", "Tuesday" },
            licenses = new[]
            {
                new { id = (Guid?)null, licenseId, licenseNumber = "lic-001" }
            }
        });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = await create.Content.ReadFromJsonAsync<Guid>();

        var detail = await client.GetFromJsonAsync<StaffDetail>($"{Base}/staff-members/{id}", Json);
        detail!.FullName.ShouldBe("Jane Technician");
        detail.Email.ShouldBe(email);
        detail.StationId.ShouldBe(stationId);
        detail.ManpowerTypeId.ShouldBe(manpowerTypeId);
        detail.EmploymentContract!.StartDate.ShouldBe(new DateOnly(2026, 1, 1));
        detail.EmploymentContract.EndDate.ShouldBe(new DateOnly(2026, 12, 31));
        detail.WorkingDays.ShouldBe([DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday]);
        detail.Licenses.Count.ShouldBe(1);
        detail.Licenses[0].LicenseNumber.ShouldBe("LIC-001");
        detail.IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task Create_with_duplicate_email_returns_409()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var stationId = await CreateStationAsync(client);
        var manpowerTypeId = await CreateManpowerTypeAsync(client);
        var email = UniqueEmail();

        (await client.PostAsJsonAsync($"{Base}/staff-members", StaffPayload(email, stationId, manpowerTypeId, "First")))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        (await client.PostAsJsonAsync($"{Base}/staff-members", StaffPayload(email, stationId, manpowerTypeId, "Second")))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Create_with_inactive_station_is_rejected()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var stationId = await CreateStationAsync(client);
        var manpowerTypeId = await CreateManpowerTypeAsync(client);

        var station = await client.GetFromJsonAsync<StaffStationDetail>($"{Base}/stations/{stationId}");
        var deactivate = new HttpRequestMessage(HttpMethod.Post, $"{Base}/stations/{stationId}/deactivate");
        deactivate.Headers.TryAddWithoutValidation("If-Match", station!.RowVersion);
        (await client.SendAsync(deactivate)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await client.PostAsJsonAsync($"{Base}/staff-members", StaffPayload(UniqueEmail(), stationId, manpowerTypeId, "Test")))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_reconciles_licenses_with_if_match()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var stationId = await CreateStationAsync(client);
        var manpowerTypeId = await CreateManpowerTypeAsync(client);
        var licenseA = await CreateLicenseAsync(client);
        var licenseB = await CreateLicenseAsync(client);
        var email = UniqueEmail();

        var create = await client.PostAsJsonAsync($"{Base}/staff-members", new
        {
            fullName = "Reconcile",
            employeeId = $"EMP-{Guid.NewGuid():N}",
            email,
            stationId,
            manpowerTypeId,
            employmentContract = (object?)null,
            workingDays = (string[]?)null,
            licenses = new[] { new { id = (Guid?)null, licenseId = licenseA, licenseNumber = "a-1" } }
        });
        var id = await create.Content.ReadFromJsonAsync<Guid>();

        var before = await client.GetFromJsonAsync<StaffDetail>($"{Base}/staff-members/{id}", Json);
        var existing = before!.Licenses.Single();

        var update = new HttpRequestMessage(HttpMethod.Put, $"{Base}/staff-members/{id}")
        {
            Content = JsonContent.Create(new
            {
                fullName = "Reconcile",
                employeeId = before.EmployeeId,
                email,
                stationId,
                manpowerTypeId,
                employmentContract = (object?)null,
                workingDays = (string[]?)null,
                licenses = new[]
                {
                    new { id = (Guid?)existing.Id, licenseId = licenseA, licenseNumber = "a-2" },
                    new { id = (Guid?)null, licenseId = licenseB, licenseNumber = "b-1" }
                }
            })
        };
        update.Headers.TryAddWithoutValidation("If-Match", before.RowVersion);
        (await client.SendAsync(update)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var after = await client.GetFromJsonAsync<StaffDetail>($"{Base}/staff-members/{id}", Json);
        after!.Licenses.Count.ShouldBe(2);
        after.Licenses.ShouldContain(l => l.Id == existing.Id && l.LicenseNumber == "A-2");
        after.Licenses.ShouldContain(l => l.LicenseId == licenseB && l.LicenseNumber == "B-1");
    }

    [Fact]
    public async Task Update_with_stale_if_match_returns_409()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var stationId = await CreateStationAsync(client);
        var manpowerTypeId = await CreateManpowerTypeAsync(client);
        var email = UniqueEmail();

        var create = await client.PostAsJsonAsync($"{Base}/staff-members", StaffPayload(email, stationId, manpowerTypeId, "Stale"));
        var id = await create.Content.ReadFromJsonAsync<Guid>();

        var update = new HttpRequestMessage(HttpMethod.Put, $"{Base}/staff-members/{id}")
        {
            Content = JsonContent.Create(StaffPayload(email, stationId, manpowerTypeId, "Updated"))
        };
        update.Headers.TryAddWithoutValidation("If-Match", "\"AAAAAAAAAAA=\"");
        (await client.SendAsync(update)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Deactivate_then_activate_round_trips()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var stationId = await CreateStationAsync(client);
        var manpowerTypeId = await CreateManpowerTypeAsync(client);

        var create = await client.PostAsJsonAsync($"{Base}/staff-members", StaffPayload(UniqueEmail(), stationId, manpowerTypeId, "Lifecycle"));
        var id = await create.Content.ReadFromJsonAsync<Guid>();

        var detail = await client.GetFromJsonAsync<StaffDetail>($"{Base}/staff-members/{id}", Json);
        var deactivate = new HttpRequestMessage(HttpMethod.Post, $"{Base}/staff-members/{id}/deactivate");
        deactivate.Headers.TryAddWithoutValidation("If-Match", detail!.RowVersion);
        (await client.SendAsync(deactivate)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var afterDeactivate = await client.GetFromJsonAsync<StaffDetail>($"{Base}/staff-members/{id}", Json);
        afterDeactivate!.IsActive.ShouldBeFalse();

        var activate = new HttpRequestMessage(HttpMethod.Post, $"{Base}/staff-members/{id}/activate");
        activate.Headers.TryAddWithoutValidation("If-Match", afterDeactivate.RowVersion);
        (await client.SendAsync(activate)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await client.GetFromJsonAsync<StaffDetail>($"{Base}/staff-members/{id}", Json))!.IsActive.ShouldBeTrue();
    }

    private sealed record StaffStationDetail(Guid Id, string RowVersion);

    private static object StaffPayload(string email, Guid stationId, Guid manpowerTypeId, string name) => new
    {
        fullName = name,
        employeeId = $"EMP-{Guid.NewGuid():N}",
        email,
        stationId,
        manpowerTypeId,
        employmentContract = (object?)null,
        workingDays = (string[]?)null,
        licenses = Array.Empty<object>()
    };

    private static string UniqueEmail() => $"staff-{Guid.NewGuid():N}@example.com";

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

    private static async Task<Guid> CreateLicenseAsync(HttpClient client)
    {
        var code = await UnusedLicenseCodeAsync(client);
        var create = await client.PostAsJsonAsync($"{Base}/licenses",
            new { code, name = $"License {code}", description = (string?)null });
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

    private static async Task<string> UnusedStationIataAsync(HttpClient client)
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
        throw new InvalidOperationException("No unused station IATA code remains.");
    }

    private static async Task<string> UnusedLicenseCodeAsync(HttpClient client)
    {
        var used = await CollectAsync<LicenseItem>(client, $"{Base}/licenses", l => l.Code);
        for (var i = 0; i < 100000; i++)
        {
            var code = $"L{i:D4}";
            if (used.Add(code))
                return code;
        }
        throw new InvalidOperationException("No unused license code remains.");
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
