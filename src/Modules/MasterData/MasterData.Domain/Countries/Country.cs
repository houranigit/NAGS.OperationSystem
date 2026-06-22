using BuildingBlocks.Domain.Aggregates;
using BuildingBlocks.Domain.Results;

namespace MasterData.Domain.Countries;

/// <summary>
/// A country reference record (ISO 3166-1 alpha-2). Long-lived master data with an active/inactive
/// lifecycle; never hard-deleted. The ISO code is trimmed and uppercased and is globally unique.
/// </summary>
public sealed class Country : AggregateRoot<Guid>
{
    private Country() { }

    public string Name { get; private set; } = null!;
    public string IsoCode { get; private set; } = null!;
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    /// <summary>Optimistic-concurrency token surfaced to clients as an ETag.</summary>
    public byte[] RowVersion { get; private set; } = [];

    public static Result<Country> Create(string? name, string? isoCode, DateTimeOffset now, Guid? id = null)
    {
        var nameCheck = ValidateName(name);
        if (nameCheck.IsFailure)
            return nameCheck.Error;

        var codeCheck = NormalizeCode(isoCode);
        if (codeCheck.IsFailure)
            return codeCheck.Error;

        return new Country
        {
            Id = id ?? Guid.NewGuid(),
            Name = nameCheck.Value,
            IsoCode = codeCheck.Value,
            IsActive = true,
            CreatedAtUtc = now
        };
    }

    public Result Update(string? name, string? isoCode, DateTimeOffset now)
    {
        var nameCheck = ValidateName(name);
        if (nameCheck.IsFailure)
            return nameCheck.Error;

        var codeCheck = NormalizeCode(isoCode);
        if (codeCheck.IsFailure)
            return codeCheck.Error;

        Name = nameCheck.Value;
        IsoCode = codeCheck.Value;
        UpdatedAtUtc = now;
        return Result.Success();
    }

    public Result Activate(DateTimeOffset now)
    {
        if (IsActive)
            return Result.Success();

        IsActive = true;
        UpdatedAtUtc = now;
        return Result.Success();
    }

    public Result Deactivate(DateTimeOffset now)
    {
        if (!IsActive)
            return Result.Success();

        IsActive = false;
        UpdatedAtUtc = now;
        return Result.Success();
    }

    private static Result<string> ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Error.Validation("Country name is required.", "MasterData.Country.NameRequired");

        var trimmed = name.Trim();
        if (trimmed.Length > 100)
            return Error.Validation("Country name must be at most 100 characters.", "MasterData.Country.NameTooLong");

        return trimmed;
    }

    private static Result<string> NormalizeCode(string? isoCode)
    {
        if (string.IsNullOrWhiteSpace(isoCode))
            return Error.Validation("Country code is required.", "MasterData.Country.CodeRequired");

        var normalized = isoCode.Trim().ToUpperInvariant();
        if (normalized.Length != 2 || !normalized.All(char.IsAsciiLetter))
            return Error.Validation("Country code must be exactly two letters (ISO 3166-1 alpha-2).", "MasterData.Country.CodeInvalid");

        return normalized;
    }
}
