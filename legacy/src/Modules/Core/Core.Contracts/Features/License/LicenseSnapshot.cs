namespace Core.Contracts.Features.License;

public sealed record LicenseSnapshot(
    Guid LicenseId,
    string Code);
