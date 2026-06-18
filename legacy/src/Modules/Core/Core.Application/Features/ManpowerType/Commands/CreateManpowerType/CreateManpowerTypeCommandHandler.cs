using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using Core.Domain.Aggregates.ManpowerType;

namespace Core.Application.Features.ManpowerType.Commands.CreateManpowerType;

/// <summary>
/// Creates a manpower type reference row. Handler orchestrates validation and persistence — no <c>SaveChanges</c> here.
/// </summary>
public sealed class CreateManpowerTypeCommandHandler(IManpowerTypeRepository manpowerTypes)
    : ICommandHandler<CreateManpowerTypeCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateManpowerTypeCommand request, CancellationToken cancellationToken)
    {
        if (await manpowerTypes.ExistsByNameAsync(request.Name, cancellationToken))
            return Error.Conflict("A manpower type with this name already exists.");

        var created = Core.Domain.Aggregates.ManpowerType.ManpowerType.Create(request.Name, request.Description);
        if (created.IsFailure) return created.Error;

        var manpowerType = created.Value;

        if (!request.IsActive)
        {
            var d = manpowerType.Deactivate();
            if (d.IsFailure) return d.Error;
        }

        manpowerTypes.Add(manpowerType);
        return manpowerType.Id.Value;
    }
}
