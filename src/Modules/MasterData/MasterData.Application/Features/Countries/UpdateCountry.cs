using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Persistence;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using MasterData.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MasterData.Application.Features.Countries;

public sealed record UpdateCountryCommand(Guid Id, string Name, string IsoCode, byte[] RowVersion) : ICommand;

public sealed class UpdateCountryCommandValidator : AbstractValidator<UpdateCountryCommand>
{
    public UpdateCountryCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.IsoCode).NotEmpty();
        RuleFor(x => x.RowVersion).NotEmpty();
    }
}

public sealed class UpdateCountryCommandHandler(IMasterDataDbContext db, TimeProvider timeProvider)
    : ICommandHandler<UpdateCountryCommand>
{
    public async Task<Result> Handle(UpdateCountryCommand request, CancellationToken cancellationToken)
    {
        var country = await db.Countries.FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken);
        if (country is null)
            return Error.NotFound("Country not found.", "MasterData.Country.NotFound");

        var normalized = request.IsoCode.Trim().ToUpperInvariant();
        var codeTaken = await db.Countries.AnyAsync(c => c.IsoCode == normalized && c.Id != request.Id, cancellationToken);
        if (codeTaken)
            return Error.Conflict("A country with this code already exists.", "MasterData.Country.DuplicateCode");

        var result = country.Update(request.Name, request.IsoCode, timeProvider.GetUtcNow());
        if (result.IsFailure)
            return result.Error;

        db.SetOriginalRowVersion(country, request.RowVersion);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyErrors.Stale;
        }

        return Result.Success();
    }
}
