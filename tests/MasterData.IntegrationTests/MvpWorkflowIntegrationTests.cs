using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Shouldly;

namespace MasterData.IntegrationTests;

public class MvpWorkflowIntegrationTests(MasterDataApiFactory factory) : IClassFixture<MasterDataApiFactory>
{
    private const string Base = MasterDataApiFactory.Base;
    private const string IdentityBase = MasterDataApiFactory.IdentityBase;

    private sealed record TokenResponse(string AccessToken, DateTimeOffset ExpiresAtUtc);
    private sealed record LoginResponse(bool MfaRequired, string? MfaToken, string? AccessToken, DateTimeOffset? ExpiresAtUtc);
    private sealed record EnrollmentResponse(string Secret, string OtpAuthUri);
    private sealed record InvitedResponse(Guid Id, string Email, string DeliveryStatus);
    private sealed record PagedList<T>(List<T> Items, int Page, int PageSize, long TotalCount);
    private sealed record CountryItem(Guid Id, string Name, string IsoCode, bool IsActive);
    private sealed record StationItem(Guid Id, string IataCode, string? IcaoCode, string Name, string? City, Guid CountryId, string CountryName, bool IsActive);
    private sealed record StationDetail(Guid Id, string IataCode, string? IcaoCode, string Name, string? City, Guid CountryId, string CountryName, bool IsActive, string RowVersion);

    [Fact]
    public async Task Invited_administrator_can_activate_and_manage_master_data()
    {
        var seededAdmin = await factory.CreateAuthenticatedAdminClientAsync();
        var roleId = await CreateMasterDataAdministratorRoleAsync(seededAdmin);
        var email = $"mvp-admin-{Guid.NewGuid():N}@nags.sa";
        const string password = "MvpAdmin#12345";

        var invite = await seededAdmin.PostAsJsonAsync($"{IdentityBase}/users/invite",
            new { email, displayName = "MVP Administrator", roleId });
        invite.StatusCode.ShouldBe(HttpStatusCode.Created);
        var invited = await invite.Content.ReadFromJsonAsync<InvitedResponse>();
        invited!.Email.ShouldBe(email);

        var invitationToken = await factory.GetInvitationTokenAsync(email);
        invitationToken.ShouldNotBeNull();

        (await seededAdmin.PostAsJsonAsync($"{IdentityBase}/auth/activate",
            new { email, invitationToken, newPassword = password }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var invitedAdmin = await LoginAndEnrollMfaAsync(email, password);

        var countryId = await CreateCountryAsync(invitedAdmin);
        var stationCode = await UnusedStationIataAsync(invitedAdmin);
        var createStation = await invitedAdmin.PostAsJsonAsync($"{Base}/stations", new
        {
            iataCode = stationCode.ToLowerInvariant(),
            icaoCode = (string?)null,
            name = "MVP Station",
            city = "Amman",
            countryId
        });
        createStation.StatusCode.ShouldBe(HttpStatusCode.Created);
        var stationId = await createStation.Content.ReadFromJsonAsync<Guid>();

        var before = await invitedAdmin.GetFromJsonAsync<StationDetail>($"{Base}/stations/{stationId}");
        before!.IataCode.ShouldBe(stationCode);
        before.Name.ShouldBe("MVP Station");
        before.IsActive.ShouldBeTrue();

        var updateStation = new HttpRequestMessage(HttpMethod.Put, $"{Base}/stations/{stationId}")
        {
            Content = JsonContent.Create(new
            {
                iataCode = stationCode,
                icaoCode = (string?)null,
                name = "MVP Station Renamed",
                city = "Aqaba",
                countryId
            })
        };
        updateStation.Headers.TryAddWithoutValidation("If-Match", before.RowVersion);
        (await invitedAdmin.SendAsync(updateStation)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var after = await invitedAdmin.GetFromJsonAsync<StationDetail>($"{Base}/stations/{stationId}");
        after!.Name.ShouldBe("MVP Station Renamed");
        after.City.ShouldBe("Aqaba");

        var deactivateStation = new HttpRequestMessage(HttpMethod.Post, $"{Base}/stations/{stationId}/deactivate");
        deactivateStation.Headers.TryAddWithoutValidation("If-Match", after.RowVersion);
        (await invitedAdmin.SendAsync(deactivateStation)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var inactiveStations = await invitedAdmin.GetFromJsonAsync<PagedList<StationItem>>(
            $"{Base}/stations?isActive=false&search={stationCode}");
        inactiveStations!.Items.ShouldContain(s => s.Id == stationId && !s.IsActive);
    }

    private static async Task<Guid> CreateMasterDataAdministratorRoleAsync(HttpClient admin)
    {
        var create = await admin.PostAsJsonAsync($"{IdentityBase}/roles", new
        {
            name = $"MVP MasterData Admin {Guid.NewGuid():N}",
            description = (string?)null,
            compatibleUserType = "SystemAdministrator",
            permissions = new[]
            {
                "masterdata.countries.view",
                "masterdata.countries.create",
                "masterdata.stations.view",
                "masterdata.stations.create",
                "masterdata.stations.update",
                "masterdata.stations.deactivate"
            }
        });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await create.Content.ReadFromJsonAsync<Guid>();
    }

    private async Task<HttpClient> LoginAndEnrollMfaAsync(string email, string password)
    {
        var client = factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync($"{IdentityBase}/auth/login", new { email, password });
        loginResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var login = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        login.ShouldNotBeNull();
        login!.MfaRequired.ShouldBeFalse();
        login.AccessToken.ShouldNotBeNullOrWhiteSpace();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var enrollmentResponse = await client.PostAsync($"{IdentityBase}/auth/mfa/enroll", content: null);
        enrollmentResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var enrollment = await enrollmentResponse.Content.ReadFromJsonAsync<EnrollmentResponse>();
        enrollment.ShouldNotBeNull();

        var confirm = await client.PostAsJsonAsync($"{IdentityBase}/auth/mfa/confirm",
            new { code = Totp(enrollment!.Secret) });
        confirm.StatusCode.ShouldBe(HttpStatusCode.OK);

        var refresh = await client.PostAsync($"{IdentityBase}/auth/refresh", content: null);
        refresh.StatusCode.ShouldBe(HttpStatusCode.OK);
        var token = await refresh.Content.ReadFromJsonAsync<TokenResponse>();
        token.ShouldNotBeNull();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);

        return client;
    }

    private static async Task<Guid> CreateCountryAsync(HttpClient client)
    {
        var isoCode = await UnusedCountryCodeAsync(client);
        var create = await client.PostAsJsonAsync($"{Base}/countries",
            new { name = $"MVP Country {isoCode}", isoCode });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await create.Content.ReadFromJsonAsync<Guid>();
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

        throw new InvalidOperationException("No unused IATA code remains.");
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
        } while (used.Count < total);

        return used;
    }

    private static string Totp(string base32Secret)
    {
        var key = Base32Decode(base32Secret);
        var counter = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        var counterBytes = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(counterBytes);

        var hash = HMACSHA1.HashData(key, counterBytes);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24)
            | ((hash[offset + 1] & 0xFF) << 16)
            | ((hash[offset + 2] & 0xFF) << 8)
            | (hash[offset + 3] & 0xFF);

        return (binary % 1_000_000).ToString("D6");
    }

    private static byte[] Base32Decode(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = 0;
        var value = 0;
        var output = new List<byte>();

        foreach (var c in input.TrimEnd('=').ToUpperInvariant())
        {
            var index = alphabet.IndexOf(c);
            if (index < 0)
                continue;

            value = (value << 5) | index;
            bits += 5;

            if (bits < 8)
                continue;

            output.Add((byte)((value >> (bits - 8)) & 0xFF));
            bits -= 8;
        }

        return [.. output];
    }
}
