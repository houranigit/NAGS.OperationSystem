using Store.Contracts.Features.Unit;

namespace Store.Contracts.Features.GeneralSupport;

public sealed record GeneralSupportDto(
    Guid Id,
    string Name,
    UnitSnapshot Unit,
    bool IsDuration,
    string? Note,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
