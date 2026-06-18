namespace Core.Contracts.Features.Country;

public sealed record CountrySnapshot(
    Guid CountryId,
    string Code);
