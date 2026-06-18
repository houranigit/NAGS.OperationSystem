using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using Core.Domain.Aggregates.OperationType;

namespace Core.Application.Features.OperationType.Commands.CreateOperationType;

/// <summary>
/// Creates an operation type. Orchestrates domain <see cref="Core.Domain.Aggregates.OperationType.OperationType.Create"/> and repository persistence; <c>SaveChanges</c> is transactional behavior.
/// </summary>
public sealed class CreateOperationTypeCommandHandler(IOperationTypeRepository operationTypes)
    : ICommandHandler<CreateOperationTypeCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateOperationTypeCommand request, CancellationToken cancellationToken)
    {
        if (await operationTypes.ExistsByNameAsync(request.Name, cancellationToken))
            return Error.Conflict("An operation type with this name already exists.");

        var created = Core.Domain.Aggregates.OperationType.OperationType.Create(request.Name, request.Description);
        if (created.IsFailure) return created.Error;

        var operationType = created.Value;

        if (!request.IsActive)
        {
            var d = operationType.Deactivate();
            if (d.IsFailure) return d.Error;
        }

        operationTypes.Add(operationType);
        return operationType.Id.Value;
    }
}
