namespace Core.Contracts.Features.License;

public sealed record LicenseSelectOption(
    Guid Id,
    string Code,
    string Name);
