using BuildingBlocks.Domain.Results;
using BuildingBlocks.Domain.ValueObjects;

namespace MasterData.Domain.Customers;

/// <summary>
/// The official address of a <see cref="Customer"/>. Legacy customer records may contain a partial
/// or entirely blank address, so every address field is optional. The owning Customer's Country
/// reference is authoritative, so the address never stores a second Country.
/// </summary>
public sealed class Address : ValueObject
{
    private Address() { }

    public string? Line1 { get; private set; }
    public string? Line2 { get; private set; }
    public string? City { get; private set; }
    public string? Region { get; private set; }
    public string? PostalCode { get; private set; }

    public static Result<Address> Create(string? line1, string? line2, string? city, string? region, string? postalCode)
    {
        var trimmedLine1 = Normalize(line1);
        if (trimmedLine1 is { Length: > 200 })
            return Error.Validation("Address line 1 must be at most 200 characters.", "MasterData.Address.Line1TooLong");

        var trimmedCity = Normalize(city);
        if (trimmedCity is { Length: > 100 })
            return Error.Validation("Address city must be at most 100 characters.", "MasterData.Address.CityTooLong");

        var trimmedLine2 = Normalize(line2);
        if (trimmedLine2 is { Length: > 200 })
            return Error.Validation("Address line 2 must be at most 200 characters.", "MasterData.Address.Line2TooLong");

        var trimmedRegion = Normalize(region);
        if (trimmedRegion is { Length: > 100 })
            return Error.Validation("Address region must be at most 100 characters.", "MasterData.Address.RegionTooLong");

        var trimmedPostal = Normalize(postalCode);
        if (trimmedPostal is { Length: > 20 })
            return Error.Validation("Postal code must be at most 20 characters.", "MasterData.Address.PostalCodeTooLong");

        return new Address
        {
            Line1 = trimmedLine1,
            Line2 = trimmedLine2,
            City = trimmedCity,
            Region = trimmedRegion,
            PostalCode = trimmedPostal
        };
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Line1;
        yield return Line2;
        yield return City;
        yield return Region;
        yield return PostalCode;
    }
}
