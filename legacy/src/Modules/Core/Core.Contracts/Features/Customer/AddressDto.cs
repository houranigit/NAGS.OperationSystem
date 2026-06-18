using Core.Contracts.Features.Country;

namespace Core.Contracts.Features.Customer;

public sealed record AddressDto(
    string? Line1,
    string? Line2,
    string? City,
    string? PostalCode,
    CountryDto? Country);
