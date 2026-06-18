namespace Store.Contracts.Features.Unit;

public sealed record UnitDto(
    Guid Id,
    string Code,
    string Name,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
