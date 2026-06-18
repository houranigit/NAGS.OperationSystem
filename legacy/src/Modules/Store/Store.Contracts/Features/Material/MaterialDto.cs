using Store.Contracts.Features.Unit;

namespace Store.Contracts.Features.Material;

public sealed record MaterialDto(
    Guid Id,
    string Name,
    UnitSnapshot Unit,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
