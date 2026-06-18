using BuildingBlocks.Domain.Aggregates;
using BuildingBlocks.Domain.Results;
using Core.Domain.Events;

namespace Core.Domain.Aggregates.OperationType;

public sealed class OperationType : AggregateRoot<OperationTypeId>
{
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private OperationType() { }

    public static Result<OperationType> Create(string name, string? description = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Error.Validation("Operation type name is required.");

        if (name.Length > 100)
            return Error.Validation("Operation type name must not exceed 100 characters.");

        if (description?.Length > 500)
            return Error.Validation("Description must not exceed 500 characters.");

        var operationType = new OperationType
        {
            Id = OperationTypeId.New(),
            Name = name.Trim(),
            Description = description?.Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        operationType.RaiseDomainEvent(new OperationTypeCreatedEvent(operationType.Id));
        return operationType;
    }

    public Result Activate()
    {
        if (IsActive)
            return Error.Conflict("Operation type is already active.");

        IsActive = true;
        RaiseDomainEvent(new OperationTypeActivatedEvent(Id));
        return Result.Success();
    }

    public Result Deactivate()
    {
        if (!IsActive)
            return Error.Conflict("Operation type is already inactive.");

        IsActive = false;
        RaiseDomainEvent(new OperationTypeDeactivatedEvent(Id));
        return Result.Success();
    }

    public Result UpdateDetails(string name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Error.Validation("Operation type name is required.");

        if (name.Length > 100)
            return Error.Validation("Operation type name must not exceed 100 characters.");

        if (description?.Length > 500)
            return Error.Validation("Description must not exceed 500 characters.");

        Name = name.Trim();
        Description = description?.Trim();
        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }

    public static OperationType CreateSeed(Guid id, string name, string? description = null)
    {
        return new OperationType
        {
            Id = OperationTypeId.From(id),
            Name = name.Trim(),
            Description = description?.Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
    }
}
