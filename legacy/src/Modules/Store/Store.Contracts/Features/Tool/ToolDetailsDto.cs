namespace Store.Contracts.Features.Tool;

/// <summary>Tool with its equipments expanded — used by the Update dialog.</summary>
public sealed record ToolDetailsDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive,
    IReadOnlyList<ToolEquipmentDto> Equipments,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
