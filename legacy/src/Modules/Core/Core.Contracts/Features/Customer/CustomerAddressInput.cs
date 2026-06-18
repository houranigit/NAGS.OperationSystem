namespace Core.Contracts.Features.Customer;

public sealed record CustomerAddressInput(
    string Line1,
    string? Line2,
    string City,
    string? PostalCode,
    Guid CountryId);
