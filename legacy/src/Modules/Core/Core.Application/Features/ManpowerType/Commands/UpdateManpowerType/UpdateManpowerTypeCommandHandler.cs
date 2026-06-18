using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using Core.Domain.Aggregates.ManpowerType;

namespace Core.Application.Features.ManpowerType.Commands.UpdateManpowerType;

/// <summary>
/// Updates manpower type details and active flag. Handler orchestrates domain mutators and repository; <c>SaveChanges</c> is transactional behavior only.
/// </summary>
public sealed class UpdateManpowerTypeCommandHandler(IManpowerTypeRepository manpowerTypes)
    : ICommandHandler<UpdateManpowerTypeCommand>
{
    public async Task<Result> Handle(UpdateManpowerTypeCommand request, CancellationToken cancellationToken)
    {
        var id = ManpowerTypeId.From(request.Id);
        var entity = await manpowerTypes.GetByIdAsync(id, cancellationToken);
        if (entity is null) return Error.NotFound("Manpower type was not found.");

        var detailsResult = entity.UpdateDetails(request.Name, request.Description);
        if (detailsResult.IsFailure) return detailsResult;

        if (request.IsActive != entity.IsActive)
        {
            var toggle = request.IsActive ? entity.Activate() : entity.Deactivate();
            if (toggle.IsFailure) return toggle;
        }

        manpowerTypes.Update(entity);
        return Result.Success();
    }
}
