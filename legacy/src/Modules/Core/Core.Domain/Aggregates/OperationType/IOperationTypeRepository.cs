namespace Core.Domain.Aggregates.OperationType;

public interface IOperationTypeRepository
{
    Task<OperationType?> GetByIdAsync(OperationTypeId id, CancellationToken ct = default);
    Task<IReadOnlyList<OperationType>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<OperationType>> GetAllActiveAsync(CancellationToken ct = default);
    Task<bool> ExistsByNameAsync(string name, CancellationToken ct = default);
    void Add(OperationType operationType);
    void Update(OperationType operationType);
}
