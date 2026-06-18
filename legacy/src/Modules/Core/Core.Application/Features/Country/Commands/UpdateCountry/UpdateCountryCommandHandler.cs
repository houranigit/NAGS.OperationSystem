using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using Core.Domain.Aggregates.Country;

namespace Core.Application.Features.Country.Commands.UpdateCountry;

public sealed class UpdateCountryCommandHandler(ICountryRepository countries)
    : ICommandHandler<UpdateCountryCommand>
{
    public async Task<Result> Handle(UpdateCountryCommand request, CancellationToken cancellationToken)
    {
        var id = CountryId.From(request.Id);
        var entity = await countries.GetByIdAsync(id, cancellationToken);
        if (entity is null) return Error.NotFound("Country was not found.");

        var nameResult = entity.UpdateName(request.Name);
        if (nameResult.IsFailure) return nameResult;

        if (request.IsActive != entity.IsActive)
        {
            var toggle = request.IsActive ? entity.Activate() : entity.Deactivate();
            if (toggle.IsFailure) return toggle;
        }

        countries.Update(entity);
        return Result.Success();
    }
}
