using BuildingBlocks.Domain.Aggregates;
using BuildingBlocks.Domain.Results;
using Store.Domain.Events;

namespace Store.Domain.Aggregates.Tool;

/// <summary>
/// A tool used during ground/maintenance operations (e.g. AIR COMPRESSOR, AXLE JACK 50 TON).
/// Owns a child collection of <see cref="Equipment"/> instances tracked by factory id, serial id,
/// and calibration date — purely informational; pricing happens at the Tool level via
/// <c>ToolPricePlan</c>.
/// </summary>
public sealed class Tool : AggregateRoot<ToolId>
{
    private readonly List<Equipment> _equipments = [];

    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    public IReadOnlyList<Equipment> Equipments => _equipments.AsReadOnly();

    private Tool() { }

    public static Result<Tool> Create(string name, string? description = null)
    {
        var nameError = ValidateName(name);
        if (nameError is not null) return nameError;

        var descError = ValidateDescription(description);
        if (descError is not null) return descError;

        var tool = new Tool
        {
            Id = ToolId.New(),
            Name = name.Trim(),
            Description = description?.Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        tool.RaiseDomainEvent(new ToolCreatedEvent(tool.Id));
        return tool;
    }

    public Result UpdateDetails(string name, string? description)
    {
        var nameError = ValidateName(name);
        if (nameError is not null) return nameError;

        var descError = ValidateDescription(description);
        if (descError is not null) return descError;

        Name = name.Trim();
        Description = description?.Trim();
        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }

    public Result Activate()
    {
        if (IsActive)
            return Error.Conflict("Tool is already active.");

        IsActive = true;
        UpdatedAt = DateTime.UtcNow;
        RaiseDomainEvent(new ToolActivatedEvent(Id));
        return Result.Success();
    }

    public Result Deactivate()
    {
        if (!IsActive)
            return Error.Conflict("Tool is already inactive.");

        IsActive = false;
        UpdatedAt = DateTime.UtcNow;
        RaiseDomainEvent(new ToolDeactivatedEvent(Id));
        return Result.Success();
    }

    public Result<EquipmentId> AddEquipment(string factoryId, string serialId, DateOnly? calibrationDate)
    {
        if (HasEquipmentWith(factoryId, serialId))
            return Error.Conflict("An equipment with this factory id and serial id already exists on this tool.");

        var equipment = Equipment.Create(Id, factoryId, serialId, calibrationDate);
        if (equipment.IsFailure) return equipment.Error;

        _equipments.Add(equipment.Value);
        UpdatedAt = DateTime.UtcNow;
        RaiseDomainEvent(new ToolEquipmentAddedEvent(Id, equipment.Value.Id));
        return equipment.Value.Id;
    }

    public Result UpdateEquipment(EquipmentId equipmentId, string factoryId, string serialId, DateOnly? calibrationDate)
    {
        var equipment = _equipments.FirstOrDefault(e => e.Id == equipmentId);
        if (equipment is null)
            return Error.NotFound("Equipment was not found on this tool.");

        if (HasEquipmentWith(factoryId, serialId, excluding: equipmentId))
            return Error.Conflict("Another equipment with this factory id and serial id already exists on this tool.");

        var update = equipment.Update(factoryId, serialId, calibrationDate);
        if (update.IsFailure) return update;

        UpdatedAt = DateTime.UtcNow;
        RaiseDomainEvent(new ToolEquipmentUpdatedEvent(Id, equipmentId));
        return Result.Success();
    }

    public Result RemoveEquipment(EquipmentId equipmentId)
    {
        var equipment = _equipments.FirstOrDefault(e => e.Id == equipmentId);
        if (equipment is null)
            return Error.NotFound("Equipment was not found on this tool.");

        _equipments.Remove(equipment);
        UpdatedAt = DateTime.UtcNow;
        RaiseDomainEvent(new ToolEquipmentRemovedEvent(Id, equipmentId));
        return Result.Success();
    }

    private bool HasEquipmentWith(string factoryId, string serialId, EquipmentId? excluding = null)
    {
        var trimmedFactory = factoryId?.Trim() ?? string.Empty;
        var trimmedSerial = serialId?.Trim() ?? string.Empty;
        return _equipments.Any(e =>
            (excluding is null || e.Id != excluding)
            && string.Equals(e.FactoryId, trimmedFactory, StringComparison.OrdinalIgnoreCase)
            && string.Equals(e.SerialId, trimmedSerial, StringComparison.OrdinalIgnoreCase));
    }

    private static Error? ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Error.Validation("Tool name is required.");
        if (name.Length > 100)
            return Error.Validation("Tool name must not exceed 100 characters.");
        return null;
    }

    private static Error? ValidateDescription(string? description)
    {
        if (description is { Length: > 500 })
            return Error.Validation("Description must not exceed 500 characters.");
        return null;
    }
}
