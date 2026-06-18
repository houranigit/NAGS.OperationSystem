namespace Core.Contracts.Features.OperationType;

public sealed record OperationTypeSnapshot(
    Guid OperationTypeId,
    string Name);
