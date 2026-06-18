using BuildingBlocks.Domain.Aggregates;
using BuildingBlocks.Domain.Results;
using Core.Domain.Events;

namespace Core.Domain.Aggregates.License;

public sealed class License : AggregateRoot<LicenseId>
{
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private License() { }

    public static Result<License> Create(string code, string name, string? description = null)
    {
        var codeError = ValidateCode(code);
        if (codeError is not null)
            return codeError;

        if (string.IsNullOrWhiteSpace(name))
            return Error.Validation("License name is required.");

        if (name.Length > 100)
            return Error.Validation("License name must not exceed 100 characters.");

        if (description?.Length > 500)
            return Error.Validation("Description must not exceed 500 characters.");

        var license = new License
        {
            Id = LicenseId.New(),
            Code = code.Trim().ToUpperInvariant(),
            Name = name.Trim(),
            Description = description?.Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        license.RaiseDomainEvent(new LicenseCreatedEvent(license.Id));
        return license;
    }

    public Result Activate()
    {
        if (IsActive)
            return Error.Conflict("License is already active.");

        IsActive = true;
        RaiseDomainEvent(new LicenseActivatedEvent(Id));
        return Result.Success();
    }

    public Result Deactivate()
    {
        if (!IsActive)
            return Error.Conflict("License is already inactive.");

        IsActive = false;
        RaiseDomainEvent(new LicenseDeactivatedEvent(Id));
        return Result.Success();
    }

    public Result UpdateDetails(string name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Error.Validation("License name is required.");

        if (name.Length > 100)
            return Error.Validation("License name must not exceed 100 characters.");

        if (description?.Length > 500)
            return Error.Validation("Description must not exceed 500 characters.");

        Name = name.Trim();
        Description = description?.Trim();
        return Result.Success();
    }

    private static Error? ValidateCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return Error.Validation("License code is required.");

        var normalized = code.Trim();

        if (normalized.Length < 2 || normalized.Length > 10)
            return Error.Validation("License code must be between 2 and 10 characters.");

        if (!normalized.All(char.IsAsciiLetterOrDigit))
            return Error.Validation("License code must contain only letters or digits.");

        return null;
    }
}
