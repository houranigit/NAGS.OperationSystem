using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using MasterData.Application.Abstractions;
using MasterData.Domain.Countries;
using Microsoft.EntityFrameworkCore;

namespace MasterData.Application.Features.Countries;

public sealed record CreateCountryCommand(string Name, string IsoCode) : ICommand<Guid>;

public sealed class CreateCountryCommandValidator : AbstractValidator<CreateCountryCommand>
{
    public CreateCountryCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.IsoCode).NotEmpty();
    }
}

public sealed class CreateCountryCommandHandler(IMasterDataDbContext db, TimeProvider timeProvider)
    : ICommandHandler<CreateCountryCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateCountryCommand request, CancellationToken cancellationToken)
    {
        var result = Country.Create(request.Name, request.IsoCode, timeProvider.GetUtcNow());
        if (result.IsFailure)
            return result.Error;

        var country = result.Value;
        var codeExists = await db.Countries.AnyAsync(c => c.IsoCode == country.IsoCode, cancellationToken);
        if (codeExists)
            return Error.Conflict("A country with this code already exists.", "MasterData.Country.DuplicateCode");

        db.Countries.Add(country);
        await db.SaveChangesAsync(cancellationToken);
        return country.Id;
    }
}
