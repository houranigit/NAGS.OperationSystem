namespace Store.Contracts.Features.Tool;

public sealed record ToolEquipmentDto(
    Guid Id,
    string FactoryId,
    string SerialId,
    DateOnly? CalibrationDate);
