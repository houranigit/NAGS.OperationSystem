using BuildingBlocks.Domain.Entities;
using BuildingBlocks.Domain.Results;

namespace MasterData.Domain.Tools;

/// <summary>Individual equipment row attached to a tool catalog item.</summary>
public sealed class Equipment : Entity<Guid>
{
    private Equipment() { }

    public Guid ToolId { get; private set; }
    public string FactoryId { get; private set; } = null!;
    public string SerialId { get; private set; } = null!;
    public DateOnly? CalibrationDate { get; private set; }

    internal static Result<Equipment> Create(Guid toolId, string? factoryId, string? serialId, DateOnly? calibrationDate, Guid? id = null)
    {
        var factoryCheck = ValidateIdentifier(factoryId, "Factory id", "MasterData.ToolEquipment.FactoryIdRequired", "MasterData.ToolEquipment.FactoryIdTooLong");
        if (factoryCheck.IsFailure)
            return factoryCheck.Error;

        var serialCheck = ValidateIdentifier(serialId, "Serial id", "MasterData.ToolEquipment.SerialIdRequired", "MasterData.ToolEquipment.SerialIdTooLong");
        if (serialCheck.IsFailure)
            return serialCheck.Error;

        return new Equipment
        {
            Id = id ?? Guid.NewGuid(),
            ToolId = toolId,
            FactoryId = factoryCheck.Value,
            SerialId = serialCheck.Value,
            CalibrationDate = calibrationDate
        };
    }

    internal Result Update(string? factoryId, string? serialId, DateOnly? calibrationDate)
    {
        var factoryCheck = ValidateIdentifier(factoryId, "Factory id", "MasterData.ToolEquipment.FactoryIdRequired", "MasterData.ToolEquipment.FactoryIdTooLong");
        if (factoryCheck.IsFailure)
            return factoryCheck.Error;

        var serialCheck = ValidateIdentifier(serialId, "Serial id", "MasterData.ToolEquipment.SerialIdRequired", "MasterData.ToolEquipment.SerialIdTooLong");
        if (serialCheck.IsFailure)
            return serialCheck.Error;

        FactoryId = factoryCheck.Value;
        SerialId = serialCheck.Value;
        CalibrationDate = calibrationDate;
        return Result.Success();
    }

    private static Result<string> ValidateIdentifier(string? value, string label, string requiredCode, string tooLongCode)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Error.Validation($"{label} is required.", requiredCode);

        var trimmed = value.Trim();
        if (trimmed.Length > 100)
            return Error.Validation($"{label} must be at most 100 characters.", tooLongCode);

        return trimmed;
    }
}
