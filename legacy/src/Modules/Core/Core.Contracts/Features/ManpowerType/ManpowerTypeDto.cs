namespace Core.Contracts.Features.ManpowerType;

public sealed record ManpowerTypeDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
