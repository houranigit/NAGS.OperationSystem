using BuildingBlocks.Domain.Aggregates;
using BuildingBlocks.Domain.Results;
using Store.Domain.Aggregates.Unit;
using Store.Domain.Events;

namespace Store.Domain.Aggregates.Material;

/// <summary>
/// A consumable material item (e.g. AERO SHELL FLUID 41, METHYL ETHYL KETONE) measured in
/// a user-managed <see cref="Unit"/> (ROLL / GALLON / KG / TIN / CAN ...).
/// </summary>
public sealed class Material : AggregateRoot<MaterialId>
{
    public string Name { get; private set; } = null!;
    public UnitId UnitId { get; private set; } = null!;
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private Material() { }

    public static Result<Material> Create(string name, UnitId unitId)
    {
        ArgumentNullException.ThrowIfNull(unitId);

        var nameError = ValidateName(name);
        if (nameError is not null) return nameError;

        var material = new Material
        {
            Id = MaterialId.New(),
            Name = name.Trim(),
            UnitId = unitId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        material.RaiseDomainEvent(new MaterialCreatedEvent(material.Id));
        return material;
    }

    public Result UpdateDetails(string name, UnitId unitId)
    {
        ArgumentNullException.ThrowIfNull(unitId);

        var nameError = ValidateName(name);
        if (nameError is not null) return nameError;

        Name = name.Trim();
        UnitId = unitId;
        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }

    public Result Activate()
    {
        if (IsActive)
            return Error.Conflict("Material is already active.");

        IsActive = true;
        UpdatedAt = DateTime.UtcNow;
        RaiseDomainEvent(new MaterialActivatedEvent(Id));
        return Result.Success();
    }

    public Result Deactivate()
    {
        if (!IsActive)
            return Error.Conflict("Material is already inactive.");

        IsActive = false;
        UpdatedAt = DateTime.UtcNow;
        RaiseDomainEvent(new MaterialDeactivatedEvent(Id));
        return Result.Success();
    }

    private static Error? ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Error.Validation("Material name is required.");
        if (name.Length > 200)
            return Error.Validation("Material name must not exceed 200 characters.");
        return null;
    }
}
