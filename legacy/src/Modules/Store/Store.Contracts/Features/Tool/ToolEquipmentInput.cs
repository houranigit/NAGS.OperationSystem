namespace Store.Contracts.Features.Tool;

/// <summary>
/// Equipment row passed from UI dialogs into Create/Update Tool commands. Existing rows have
/// <see cref="Id"/> set; brand-new rows leave it null.
/// </summary>
public sealed record ToolEquipmentInput(
    Guid? Id,
    string FactoryId,
    string SerialId,
    DateOnly? CalibrationDate);
