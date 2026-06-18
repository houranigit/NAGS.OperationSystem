using BuildingBlocks.Domain.Results;
using BuildingBlocks.Domain.ValueObjects;
using Core.Domain.Aggregates.Country;
using CountryEntity = global::Core.Domain.Aggregates.Country.Country;

namespace Core.Domain.ValueObjects;

public sealed class Address : ValueObject
{
    public string Line1 { get; }
    public string? Line2 { get; }
    public string City { get; }
    public string? PostalCode { get; }
    public CountryId CountryId { get; }
    public CountryEntity? Country { get; private set; }

    private Address(string line1, string? line2, string city, string? postalCode, CountryId countryId)
    {
        Line1 = line1;
        Line2 = line2;
        City = city;
        PostalCode = postalCode;
        CountryId = countryId;
    }

    public static Result<Address> Create(string? line1, string? line2, string? city, string? postalCode, CountryId countryId)
    {
        if (string.IsNullOrWhiteSpace(line1))
            return Error.Validation("Address line 1 is required.");

        if (line1.Length > 200)
            return Error.Validation("Address line 1 must not exceed 200 characters.");

        if (line2 is not null && line2.Length > 200)
            return Error.Validation("Address line 2 must not exceed 200 characters.");

        if (string.IsNullOrWhiteSpace(city))
            return Error.Validation("City is required.");

        if (city.Length > 100)
            return Error.Validation("City must not exceed 100 characters.");

        if (postalCode is not null && postalCode.Length > 20)
            return Error.Validation("Postal code must not exceed 20 characters.");

        return new Address(
            line1.Trim(),
            line2?.Trim(),
            city.Trim(),
            postalCode?.Trim(),
            countryId);
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Line1;
        yield return Line2 ?? string.Empty;
        yield return City;
        yield return PostalCode ?? string.Empty;
        yield return CountryId;
    }
}
