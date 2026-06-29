using BuildingBlocks.Domain.Aggregates;
using BuildingBlocks.Domain.Results;

namespace MasterData.Domain.Tools;

/// <summary>Tool catalog item with informational equipment rows.</summary>
public sealed class Tool : AggregateRoot<Guid>
{
    private readonly List<Equipment> _equipments = [];

    private Tool() { }

    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyList<Equipment> Equipments => _equipments.AsReadOnly();

    public static Result<Tool> Create(string? name, string? description, DateTimeOffset now, Guid? id = null)
    {
        var nameCheck = ValidateName(name);
        if (nameCheck.IsFailure)
            return nameCheck.Error;

        var descriptionCheck = ValidateDescription(description);
        if (descriptionCheck.IsFailure)
            return descriptionCheck.Error;

        return new Tool
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

    public Result<Equipment> AddEquipment(string? factoryId, string? serialId, DateOnly? calibrationDate, DateTimeOffset now, Guid? id = null)
    {
        var equipment = Equipment.Create(Id, factoryId, serialId, calibrationDate, id);
        if (equipment.IsFailure)
            return equipment.Error;

        if (HasEquipmentWith(equipment.Value.FactoryId, equipment.Value.SerialId, id))
            return Error.Conflict("An equipment row with this factory id and serial id already exists on this tool.", "MasterData.ToolEquipment.Duplicate");

        _equipments.Add(equipment.Value);
        UpdatedAtUtc = now;
        return equipment.Value;
    }

    public Result UpdateEquipment(Guid equipmentId, string? factoryId, string? serialId, DateOnly? calibrationDate, DateTimeOffset now)
    {
        var equipment = _equipments.FirstOrDefault(e => e.Id == equipmentId);
        if (equipment is null)
            return Error.NotFound("Equipment row not found.", "MasterData.ToolEquipment.NotFound");

        var update = equipment.Update(factoryId, serialId, calibrationDate);
        if (update.IsFailure)
            return update;

        if (HasEquipmentWith(equipment.FactoryId, equipment.SerialId, equipmentId))
            return Error.Conflict("An equipment row with this factory id and serial id already exists on this tool.", "MasterData.ToolEquipment.Duplicate");

        UpdatedAtUtc = now;
        return Result.Success();
    }

    public Result RemoveEquipment(Guid equipmentId, DateTimeOffset now)
    {
        var equipment = _equipments.FirstOrDefault(e => e.Id == equipmentId);
        if (equipment is null)
            return Error.NotFound("Equipment row not found.", "MasterData.ToolEquipment.NotFound");

        _equipments.Remove(equipment);
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

    private bool HasEquipmentWith(string factoryId, string serialId, Guid? excluding = null) =>
        _equipments.Any(e =>
            (excluding is null || e.Id != excluding)
            && string.Equals(e.FactoryId, factoryId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(e.SerialId, serialId, StringComparison.OrdinalIgnoreCase));

    private static Result<string> ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Error.Validation("Tool name is required.", "MasterData.Tool.NameRequired");

        var trimmed = name.Trim();
        if (trimmed.Length > 100)
            return Error.Validation("Tool name must be at most 100 characters.", "MasterData.Tool.NameTooLong");

        return trimmed;
    }

    private static Result<string?> ValidateDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return Result.Success<string?>(null);

        var trimmed = description.Trim();
        if (trimmed.Length > 500)
            return Error.Validation("Description must be at most 500 characters.", "MasterData.Tool.DescriptionTooLong");

        return Result.Success<string?>(trimmed);
    }
}
