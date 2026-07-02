using System.Net;
using System.Net.Http.Json;
using Shouldly;

namespace MasterData.IntegrationTests;

public class CustomerApiTests(MasterDataApiFactory factory) : IClassFixture<MasterDataApiFactory>
{
    private const string Base = MasterDataApiFactory.Base;

    private sealed record PagedList<T>(List<T> Items, int Page, int PageSize, long TotalCount);
    private sealed record CustomerItem(Guid Id, string? IataCode, string? IcaoCode, string Name, Guid CountryId, string CountryName, bool IsActive, int ContactCount);
    private sealed record AddressBody(string? Line1, string? Line2, string? City, string? Region, string? PostalCode);
    private sealed record ContactBody(Guid Id, string Name, string? JobTitle, string Email, string? Phone, Guid? LinkedUserId, bool IsActive, DateTimeOffset CreatedAtUtc, DateTimeOffset? UpdatedAtUtc);
    private sealed record CustomerDetail(Guid Id, string? IataCode, string? IcaoCode, string Name, Guid CountryId, string CountryName,
        string? OfficialEmail, string? OfficialPhone, string? LogoFileReference, AddressBody Address, bool IsActive,
        DateTimeOffset CreatedAtUtc, DateTimeOffset? UpdatedAtUtc, string RowVersion, List<ContactBody> Contacts);
    private sealed record CountryItem(Guid Id, string Name, string IsoCode, bool IsActive);
    private sealed record CountryDetail(Guid Id, string Name, string IsoCode, bool IsActive, DateTimeOffset CreatedAtUtc, DateTimeOffset? UpdatedAtUtc, string RowVersion);

    private static object AddressPayload() => new { line1 = "1 Airport Rd", line2 = (string?)null, city = "Amman", region = (string?)null, postalCode = (string?)null };

    [Fact]
    public async Task Customers_without_token_returns_401()
    {
        var client = factory.CreateClient();
        (await client.GetAsync($"{Base}/customers")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Create_with_contacts_then_get_round_trips()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var countryId = await CreateCountryAsync(client);
        var iata = await UnusedIataAsync(client);

        var create = await client.PostAsJsonAsync($"{Base}/customers", new
        {
            iataCode = iata.ToLowerInvariant(),
            icaoCode = (string?)null,
            name = "Test Airline",
            countryId,
            officialEmail = "ops@test.com",
            officialPhone = "+962",
            address = AddressPayload(),
            contacts = new[]
            {
                new { id = (Guid?)null, name = "Alice", jobTitle = (string?)"Manager", email = "alice@test.com", phone = (string?)null },
                new { id = (Guid?)null, name = "Bob", jobTitle = (string?)null, email = "bob@test.com", phone = (string?)null }
            }
        });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = await create.Content.ReadFromJsonAsync<Guid>();

        var detail = await client.GetFromJsonAsync<CustomerDetail>($"{Base}/customers/{id}");
        detail!.IataCode.ShouldBe(iata);
        detail.OfficialEmail.ShouldBe("ops@test.com");
        detail.Address.City.ShouldBe("Amman");
        detail.Contacts.Count(c => c.IsActive).ShouldBe(2);
    }

    [Fact]
    public async Task Create_with_duplicate_iata_is_allowed()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var countryId = await CreateCountryAsync(client);
        var iata = await UnusedIataAsync(client);

        (await client.PostAsJsonAsync($"{Base}/customers", CustomerPayload(iata, countryId, "First")))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        (await client.PostAsJsonAsync($"{Base}/customers", CustomerPayload(iata, countryId, "Second")))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Create_without_iata_round_trips_null()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var countryId = await CreateCountryAsync(client);
        var icao = await UnusedIcaoAsync(client);

        var create = await client.PostAsJsonAsync($"{Base}/customers", new
        {
            iataCode = (string?)null,
            icaoCode = icao,
            name = "Royal Jet",
            countryId,
            officialEmail = (string?)null,
            officialPhone = (string?)null,
            address = AddressPayload(),
            contacts = Array.Empty<object>()
        });

        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = await create.Content.ReadFromJsonAsync<Guid>();
        var detail = await client.GetFromJsonAsync<CustomerDetail>($"{Base}/customers/{id}");
        detail!.IataCode.ShouldBeNull();
    }

    [Fact]
    public async Task Create_with_blank_address_fields_round_trips_nulls()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var countryId = await CreateCountryAsync(client);
        var iata = await UnusedIataAsync(client);

        var create = await client.PostAsJsonAsync($"{Base}/customers", new
        {
            iataCode = iata,
            icaoCode = (string?)null,
            name = "Legacy Blank Address",
            countryId,
            officialEmail = (string?)null,
            officialPhone = (string?)null,
            address = new { line1 = (string?)null, line2 = (string?)null, city = (string?)null, region = (string?)null, postalCode = (string?)null },
            contacts = Array.Empty<object>()
        });

        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = await create.Content.ReadFromJsonAsync<Guid>();
        var detail = await client.GetFromJsonAsync<CustomerDetail>($"{Base}/customers/{id}");
        detail!.Address.Line1.ShouldBeNull();
        detail.Address.City.ShouldBeNull();
    }

    [Fact]
    public async Task Create_with_duplicate_contact_email_returns_409()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var countryId = await CreateCountryAsync(client);
        var iata = await UnusedIataAsync(client);

        var create = await client.PostAsJsonAsync($"{Base}/customers", new
        {
            iataCode = iata,
            icaoCode = (string?)null,
            name = "Dup Contacts",
            countryId,
            officialEmail = (string?)null,
            officialPhone = (string?)null,
            address = AddressPayload(),
            contacts = new[]
            {
                new { id = (Guid?)null, name = "A", jobTitle = (string?)null, email = "same@test.com", phone = (string?)null },
                new { id = (Guid?)null, name = "B", jobTitle = (string?)null, email = "SAME@test.com", phone = (string?)null }
            }
        });

        create.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Create_with_inactive_country_is_rejected()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var countryId = await CreateCountryAsync(client);

        var country = await client.GetFromJsonAsync<CountryDetail>($"{Base}/countries/{countryId}");
        var deactivate = new HttpRequestMessage(HttpMethod.Post, $"{Base}/countries/{countryId}/deactivate");
        deactivate.Headers.TryAddWithoutValidation("If-Match", country!.RowVersion);
        (await client.SendAsync(deactivate)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var iata = await UnusedIataAsync(client);
        (await client.PostAsJsonAsync($"{Base}/customers", CustomerPayload(iata, countryId, "Test")))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Customer_update_ignores_contacts_and_dedicated_endpoints_manage_them()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var countryId = await CreateCountryAsync(client);
        var iata = await UnusedIataAsync(client);

        var create = await client.PostAsJsonAsync($"{Base}/customers", new
        {
            iataCode = iata,
            icaoCode = (string?)null,
            name = "Reconcile",
            countryId,
            officialEmail = (string?)null,
            officialPhone = (string?)null,
            address = AddressPayload(),
            contacts = new[]
            {
                new { id = (Guid?)null, name = "Alice", jobTitle = (string?)null, email = "alice@test.com", phone = (string?)null },
                new { id = (Guid?)null, name = "Bob", jobTitle = (string?)null, email = "bob@test.com", phone = (string?)null }
            }
        });
        var id = await create.Content.ReadFromJsonAsync<Guid>();

        var before = await client.GetFromJsonAsync<CustomerDetail>($"{Base}/customers/{id}");
        var alice = before!.Contacts.Single(c => c.Email == "alice@test.com");
        var bob = before.Contacts.Single(c => c.Email == "bob@test.com");

        // A customer update changes only customer fields; contacts are untouched even if the body
        // were to include them.
        var update = new HttpRequestMessage(HttpMethod.Put, $"{Base}/customers/{id}")
        {
            Content = JsonContent.Create(new
            {
                iataCode = iata,
                icaoCode = (string?)null,
                name = "Reconcile Renamed",
                countryId,
                officialEmail = (string?)null,
                officialPhone = (string?)null,
                address = AddressPayload()
            })
        };
        update.Headers.TryAddWithoutValidation("If-Match", before.RowVersion);
        (await client.SendAsync(update)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var afterUpdate = await client.GetFromJsonAsync<CustomerDetail>($"{Base}/customers/{id}");
        afterUpdate!.Name.ShouldBe("Reconcile Renamed");
        afterUpdate.Contacts.Count(c => c.IsActive).ShouldBe(2); // Unchanged by the customer update.

        // Dedicated endpoints manage contacts: update Alice, add Carol, terminally remove Bob.
        var editAlice = new HttpRequestMessage(HttpMethod.Put, $"{Base}/customers/{id}/contacts/{alice.Id}")
        {
            Content = JsonContent.Create(new { name = "Alice Updated", jobTitle = "Director", email = "alice@test.com", phone = (string?)null })
        };
        editAlice.Headers.TryAddWithoutValidation("If-Match", afterUpdate.RowVersion);
        (await client.SendAsync(editAlice)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var afterAlice = await client.GetFromJsonAsync<CustomerDetail>($"{Base}/customers/{id}");
        var addCarol = new HttpRequestMessage(HttpMethod.Post, $"{Base}/customers/{id}/contacts")
        {
            Content = JsonContent.Create(new { name = "Carol", jobTitle = (string?)null, email = "carol@test.com", phone = (string?)null })
        };
        addCarol.Headers.TryAddWithoutValidation("If-Match", afterAlice!.RowVersion);
        (await client.SendAsync(addCarol)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var afterCarol = await client.GetFromJsonAsync<CustomerDetail>($"{Base}/customers/{id}");
        var removeBob = new HttpRequestMessage(HttpMethod.Post, $"{Base}/customers/{id}/contacts/{bob.Id}/remove");
        removeBob.Headers.TryAddWithoutValidation("If-Match", afterCarol!.RowVersion);
        (await client.SendAsync(removeBob)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var after = await client.GetFromJsonAsync<CustomerDetail>($"{Base}/customers/{id}");
        var activeAfter = after!.Contacts.Where(c => c.IsActive).ToList();
        activeAfter.Count.ShouldBe(2);
        activeAfter.ShouldContain(c => c.Email == "alice@test.com" && c.Name == "Alice Updated");
        activeAfter.ShouldContain(c => c.Email == "carol@test.com");
        after.Contacts.ShouldContain(c => c.Email == "bob@test.com" && !c.IsActive);
    }

    [Fact]
    public async Task Add_contact_endpoint_appends_contact()
    {
        var client = await factory.CreateAuthenticatedAdminClientAsync();
        var countryId = await CreateCountryAsync(client);
        var iata = await UnusedIataAsync(client);

        var create = await client.PostAsJsonAsync($"{Base}/customers", CustomerPayload(iata, countryId, "AddContact"));
        var id = await create.Content.ReadFromJsonAsync<Guid>();

        var before = await client.GetFromJsonAsync<CustomerDetail>($"{Base}/customers/{id}");
        var add = new HttpRequestMessage(HttpMethod.Post, $"{Base}/customers/{id}/contacts")
        {
            Content = JsonContent.Create(new { name = "Dave", jobTitle = (string?)null, email = "dave@test.com", phone = (string?)null })
        };
        add.Headers.TryAddWithoutValidation("If-Match", before!.RowVersion);
        (await client.SendAsync(add)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var after = await client.GetFromJsonAsync<CustomerDetail>($"{Base}/customers/{id}");
        after!.Contacts.ShouldContain(c => c.Email == "dave@test.com" && c.IsActive);
    }

    private static object CustomerPayload(string iata, Guid countryId, string name) => new
    {
        iataCode = iata,
        icaoCode = (string?)null,
        name,
        countryId,
        officialEmail = (string?)null,
        officialPhone = (string?)null,
        address = AddressPayload(),
        contacts = Array.Empty<object>()
    };

    private static async Task<Guid> CreateCountryAsync(HttpClient client)
    {
        var code = await UnusedCountryCodeAsync(client);
        var create = await client.PostAsJsonAsync($"{Base}/countries", new { name = $"Country {code}", isoCode = code });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await create.Content.ReadFromJsonAsync<Guid>();
    }

    private static async Task<string> UnusedIataAsync(HttpClient client)
    {
        var used = await CollectAsync<CustomerItem>(client, $"{Base}/customers", c => c.IataCode ?? string.Empty);
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        foreach (var a in chars)
            foreach (var b in chars)
            {
                var code = $"{a}{b}";
                if (used.Add(code))
                    return code;
            }
        throw new InvalidOperationException("No unused customer IATA code remains.");
    }

    private static async Task<string> UnusedIcaoAsync(HttpClient client)
    {
        var used = await CollectAsync<CustomerItem>(client, $"{Base}/customers", c => c.IcaoCode ?? string.Empty);
        const string letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        foreach (var a in letters)
            foreach (var b in letters)
                foreach (var c in letters)
                {
                    var code = $"{a}{b}{c}";
                    if (used.Add(code))
                        return code;
                }
        throw new InvalidOperationException("No unused customer ICAO code remains.");
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
