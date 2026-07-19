using BuildingBlocks.Domain.Aggregates;
using BuildingBlocks.Domain.Results;

namespace MasterData.Domain.Services;

/// <summary>Operational service catalog item with active/inactive lifecycle.</summary>
public sealed class Service : AggregateRoot<Guid>
{
    private Service() { }

    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static Result<Service> Create(string? name, string? description, DateTimeOffset now, Guid? id = null)
    {
        var nameCheck = ValidateName(name);
        if (nameCheck.IsFailure)
            return nameCheck.Error;

        var descriptionCheck = ValidateDescription(description);
        if (descriptionCheck.IsFailure)
            return descriptionCheck.Error;

        return new Service
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

    /// <summary>Advances optimistic concurrency when an allowance involving this service changes.</summary>
    public void Touch(DateTimeOffset now) => UpdatedAtUtc = now;

    private static Result<string> ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Error.Validation("Service name is required.", "MasterData.Service.NameRequired");

        var trimmed = name.Trim();
        if (trimmed.Length > 100)
            return Error.Validation("Service name must be at most 100 characters.", "MasterData.Service.NameTooLong");

        return trimmed;
    }

    private static Result<string?> ValidateDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return Result.Success<string?>(null);

        var trimmed = description.Trim();
        if (trimmed.Length > 500)
            return Error.Validation("Description must be at most 500 characters.", "MasterData.Service.DescriptionTooLong");

        return Result.Success<string?>(trimmed);
    }
}
