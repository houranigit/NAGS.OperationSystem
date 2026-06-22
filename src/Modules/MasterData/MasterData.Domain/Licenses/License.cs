using BuildingBlocks.Domain.Aggregates;
using BuildingBlocks.Domain.Results;

namespace MasterData.Domain.Licenses;

/// <summary>
/// A professional license/certification that can be assigned to staff members. Identified by a
/// unique uppercase alphanumeric code. Catalog reference data with an active/inactive lifecycle.
/// </summary>
public sealed class License : AggregateRoot<Guid>
{
    private License() { }

    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    /// <summary>Optimistic-concurrency token surfaced to clients as an ETag.</summary>
    public byte[] RowVersion { get; private set; } = [];

    public static Result<License> Create(string? code, string? name, string? description, DateTimeOffset now, Guid? id = null)
    {
        var codeCheck = NormalizeCode(code);
        if (codeCheck.IsFailure)
            return codeCheck.Error;

        var nameCheck = ValidateName(name);
        if (nameCheck.IsFailure)
            return nameCheck.Error;

        var descriptionCheck = ValidateDescription(description);
        if (descriptionCheck.IsFailure)
            return descriptionCheck.Error;

        return new License
        {
            Id = id ?? Guid.NewGuid(),
            Code = codeCheck.Value,
            Name = nameCheck.Value,
            Description = descriptionCheck.Value,
            IsActive = true,
            CreatedAtUtc = now
        };
    }

    /// <summary>Updates the editable fields. The code is immutable once assigned.</summary>
    public Result Update(string? name, string? description, DateTimeOffset now)
    {
        var nameCheck = ValidateName(name);
        if (nameCheck.IsFailure)
            return nameCheck.Error;

        var descriptionCheck = ValidateDescription(description);
        if (descriptionCheck.IsFailure)
            return descriptionCheck.Error;

        Name = nameCheck.Value;
        Description = descriptionCheck.Value;
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

    private static Result<string> NormalizeCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return Error.Validation("License code is required.", "MasterData.License.CodeRequired");

        var normalized = code.Trim().ToUpperInvariant();
        if (normalized.Length < 2 || normalized.Length > 10)
            return Error.Validation("License code must be between 2 and 10 characters.", "MasterData.License.CodeLength");

        if (!normalized.All(char.IsAsciiLetterOrDigit))
            return Error.Validation("License code must contain only letters or digits.", "MasterData.License.CodeInvalid");

        return normalized;
    }

    private static Result<string> ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Error.Validation("License name is required.", "MasterData.License.NameRequired");

        var trimmed = name.Trim();
        if (trimmed.Length > 100)
            return Error.Validation("License name must be at most 100 characters.", "MasterData.License.NameTooLong");

        return trimmed;
    }

    private static Result<string?> ValidateDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return Result.Success<string?>(null);

        var trimmed = description.Trim();
        if (trimmed.Length > 500)
            return Error.Validation("Description must be at most 500 characters.", "MasterData.License.DescriptionTooLong");

        return Result.Success<string?>(trimmed);
    }
}
