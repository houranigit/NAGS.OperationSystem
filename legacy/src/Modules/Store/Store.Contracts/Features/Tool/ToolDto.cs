namespace Store.Contracts.Features.Tool;

public sealed record ToolDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive,
    int EquipmentsCount,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
