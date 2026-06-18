namespace Core.Contracts.Features.Service;

public sealed record ServiceDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
