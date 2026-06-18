using Core.Contracts.Features.OperationType;

namespace Core.Contracts.Readers;

public interface IOperationTypeReader
{
    Task<OperationTypeSnapshot?> GetByIdAsync(Guid operationTypeId, CancellationToken cancellationToken = default);

    /// <summary>True when an operation type with this id exists AND is active.</summary>
    Task<bool> ExistsActiveAsync(Guid operationTypeId, CancellationToken cancellationToken = default);
}
