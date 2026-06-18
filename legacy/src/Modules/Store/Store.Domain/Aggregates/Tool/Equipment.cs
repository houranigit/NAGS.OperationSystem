using BuildingBlocks.Domain.Entities;
using BuildingBlocks.Domain.Results;

namespace Store.Domain.Aggregates.Tool;

/// <summary>
/// Individual equipment unit attached to a <see cref="Tool"/> (factory id, serial id, calibration date).
/// Purely informational — calibration tracking only; never priced individually.
/// </summary>
public sealed class Equipment : Entity<EquipmentId>
{
    public ToolId ToolId { get; private set; } = null!;
    public string FactoryId { get; private set; } = null!;
    public string SerialId { get; private set; } = null!;
    public DateOnly? CalibrationDate { get; private set; }

    private Equipment() { }

    internal static Result<Equipment> Create(
        ToolId toolId,
        string factoryId,
        string serialId,
        DateOnly? calibrationDate)
    {
        var fError = ValidateIdentifier(factoryId, nameof(factoryId), "Factory id");
        if (fError is not null) return fError;

        var sError = ValidateIdentifier(serialId, nameof(serialId), "Serial id");
        if (sError is not null) return sError;

        return new Equipment
        {
            Id = EquipmentId.New(),
            ToolId = toolId,
            FactoryId = factoryId.Trim(),
            SerialId = serialId.Trim(),
            CalibrationDate = calibrationDate
        };
    }

    internal Result Update(string factoryId, string serialId, DateOnly? calibrationDate)
    {
        var fError = ValidateIdentifier(factoryId, nameof(factoryId), "Factory id");
        if (fError is not null) return fError;

        var sError = ValidateIdentifier(serialId, nameof(serialId), "Serial id");
        if (sError is not null) return sError;

        FactoryId = factoryId.Trim();
        SerialId = serialId.Trim();
        CalibrationDate = calibrationDate;
        return Result.Success();
    }

    private static Error? ValidateIdentifier(string value, string field, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Error.Validation($"{label} is required.");
        if (value.Length > 100)
            return Error.Validation($"{label} must not exceed 100 characters.");
        return null;
    }
}
