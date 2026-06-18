using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using Core.Domain.Aggregates.Country;
using Core.Domain.ValueObjects;

namespace Core.Application.Features.Country.Commands.CreateCountry;

public sealed class CreateCountryCommandHandler(ICountryRepository countries)
    : ICommandHandler<CreateCountryCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateCountryCommand request, CancellationToken cancellationToken)
    {
        var codeResult = CountryCode.Create(request.Code);
        if (codeResult.IsFailure) return codeResult.Error;

        if (await countries.ExistsByCodeAsync(codeResult.Value.Value, cancellationToken))
            return Error.Conflict("A country with this code already exists.");

        var created = Core.Domain.Aggregates.Country.Country.Create(codeResult.Value, request.Name);
        if (created.IsFailure) return created.Error;

        var country = created.Value;

        if (!request.IsActive)
        {
            var d = country.Deactivate();
            if (d.IsFailure) return d.Error;
        }

        countries.Add(country);
        return country.Id.Value;
    }
}
