using BuildingBlocks.Domain.Aggregates;
using BuildingBlocks.Domain.Results;
using MasterData.Contracts.Resources;

namespace MasterData.Domain.Materials;

/// <summary>Material catalog item with configurable quantity or duration usage.</summary>
public sealed class Material : AggregateRoot<Guid>
{
    private Material() { }

    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public ResourceCalculationType CalculationType { get; private set; } = ResourceCalculationType.Quantity;
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static Result<Material> Create(
        string? name,
        string? description,
        DateTimeOffset now,
        Guid? id = null,
        ResourceCalculationType calculationType = ResourceCalculationType.Quantity)
    {
        var nameCheck = ValidateName(name);
        if (nameCheck.IsFailure)
            return nameCheck.Error;

        var descriptionCheck = ValidateDescription(description);
        if (descriptionCheck.IsFailure)
            return descriptionCheck.Error;

        var calculationTypeCheck = ValidateCalculationType(calculationType);
        if (calculationTypeCheck.IsFailure)
            return calculationTypeCheck.Error;

        return new Material
        {
            Id = id ?? Guid.NewGuid(),
            Name = nameCheck.Value,
            Description = descriptionCheck.Value,
            CalculationType = calculationTypeCheck.Value,
            IsActive = true,
            CreatedAtUtc = now
        };
    }

    public Result Update(
        string? name,
        string? description,
        DateTimeOffset now,
        ResourceCalculationType? calculationType = null)
    {
        var nameCheck = ValidateName(name);
        if (nameCheck.IsFailure)
            return nameCheck.Error;

        var descriptionCheck = ValidateDescription(description);
        if (descriptionCheck.IsFailure)
            return descriptionCheck.Error;

        var calculationTypeCheck = ValidateCalculationType(calculationType ?? CalculationType);
        if (calculationTypeCheck.IsFailure)
            return calculationTypeCheck.Error;

        Name = nameCheck.Value;
        Description = descriptionCheck.Value;
        CalculationType = calculationTypeCheck.Value;
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
            return Error.Validation("Material name is required.", "MasterData.Material.NameRequired");

        var trimmed = name.Trim();
        if (trimmed.Length > 200)
            return Error.Validation("Material name must be at most 200 characters.", "MasterData.Material.NameTooLong");

        return trimmed;
    }

    private static Result<string?> ValidateDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return Result.Success<string?>(null);

        var trimmed = description.Trim();
        if (trimmed.Length > 500)
            return Error.Validation("Description must be at most 500 characters.", "MasterData.Material.DescriptionTooLong");

        return Result.Success<string?>(trimmed);
    }

    private static Result<ResourceCalculationType> ValidateCalculationType(ResourceCalculationType calculationType) =>
        calculationType is ResourceCalculationType.Quantity or ResourceCalculationType.Duration
            ? calculationType
            : Error.Validation(
                "Calculation type must be Quantity or Duration.",
                "MasterData.Material.CalculationTypeInvalid");
}
