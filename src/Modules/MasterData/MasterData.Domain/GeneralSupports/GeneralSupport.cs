using BuildingBlocks.Domain.Aggregates;
using BuildingBlocks.Domain.Results;

namespace MasterData.Domain.GeneralSupports;

/// <summary>General support catalog item. Unit and duration metadata are intentionally deferred.</summary>
public sealed class GeneralSupport : AggregateRoot<Guid>
{
    private GeneralSupport() { }

    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static Result<GeneralSupport> Create(string? name, string? description, DateTimeOffset now, Guid? id = null)
    {
        var nameCheck = ValidateName(name);
        if (nameCheck.IsFailure)
            return nameCheck.Error;

        var descriptionCheck = ValidateDescription(description);
        if (descriptionCheck.IsFailure)
            return descriptionCheck.Error;

        return new GeneralSupport
        {
            Id = id ?? Guid.NewGuid(),
            Name = nameCheck.Value,
            Description = descriptionCheck.Value,
            IsActive = true,
            CreatedAtUtc = now
        };
    }

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

    private static Result<string> ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Error.Validation("General support name is required.", "MasterData.GeneralSupport.NameRequired");

        var trimmed = name.Trim();
        if (trimmed.Length > 200)
            return Error.Validation("General support name must be at most 200 characters.", "MasterData.GeneralSupport.NameTooLong");

        return trimmed;
    }

    private static Result<string?> ValidateDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return Result.Success<string?>(null);

        var trimmed = description.Trim();
        if (trimmed.Length > 500)
            return Error.Validation("Description must be at most 500 characters.", "MasterData.GeneralSupport.DescriptionTooLong");

        return Result.Success<string?>(trimmed);
    }
}
