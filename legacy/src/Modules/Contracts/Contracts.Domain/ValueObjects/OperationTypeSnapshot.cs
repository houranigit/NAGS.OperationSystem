using BuildingBlocks.Domain.Results;
using BuildingBlocks.Domain.ValueObjects;

namespace Contracts.Domain.ValueObjects;

/// <summary>Point-in-time copy of a Core operation type.</summary>
public sealed class OperationTypeSnapshot : ValueObject
{
    public Guid OperationTypeId { get; private set; }
    public string Name { get; private set; } = null!;

    private OperationTypeSnapshot() { }

    private OperationTypeSnapshot(Guid operationTypeId, string name)
    {
        OperationTypeId = operationTypeId;
        Name = name;
    }

    public static Result<OperationTypeSnapshot> Create(Guid operationTypeId, string? name)
    {
        if (operationTypeId == Guid.Empty)
            return Error.Validation("OperationTypeId is required.");

        if (string.IsNullOrWhiteSpace(name))
            return Error.Validation("Operation type name is required.");

        if (name.Length > 100)
            return Error.Validation("Operation type name must not exceed 100 characters.");

        return new OperationTypeSnapshot(operationTypeId, name.Trim());
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return OperationTypeId;
        yield return Name;
    }
}
