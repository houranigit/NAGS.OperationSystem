using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using Core.Domain.Aggregates.OperationType;

namespace Core.Application.Features.OperationType.Commands.UpdateOperationType;

/// <summary>
/// Updates operation type details and active flag via aggregate mutators; same orchestration shape as other Core update handlers.
/// </summary>
public sealed class UpdateOperationTypeCommandHandler(IOperationTypeRepository operationTypes)
    : ICommandHandler<UpdateOperationTypeCommand>
{
    public async Task<Result> Handle(UpdateOperationTypeCommand request, CancellationToken cancellationToken)
    {
        var id = OperationTypeId.From(request.Id);
        var entity = await operationTypes.GetByIdAsync(id, cancellationToken);
        if (entity is null) return Error.NotFound("Operation type was not found.");

        var detailsResult = entity.UpdateDetails(request.Name, request.Description);
        if (detailsResult.IsFailure) return detailsResult;

        if (request.IsActive != entity.IsActive)
        {
            var toggle = request.IsActive ? entity.Activate() : entity.Deactivate();
            if (toggle.IsFailure) return toggle;
        }

        operationTypes.Update(entity);
        return Result.Success();
    }
}
