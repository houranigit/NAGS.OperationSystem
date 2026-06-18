namespace Core.Contracts.Features.OperationType;

public sealed record OperationTypeDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
