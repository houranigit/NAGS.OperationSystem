using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Persistence;
using BuildingBlocks.Domain.Results;
using MasterData.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MasterData.Application.Features.Countries;

public sealed record ActivateCountryCommand(Guid Id, byte[] RowVersion) : ICommand;

public sealed record DeactivateCountryCommand(Guid Id, byte[] RowVersion) : ICommand;

public sealed class ActivateCountryCommandHandler(IMasterDataDbContext db, TimeProvider timeProvider)
    : ICommandHandler<ActivateCountryCommand>
{
    public async Task<Result> Handle(ActivateCountryCommand request, CancellationToken cancellationToken)
    {
        var country = await db.Countries.FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken);
        if (country is null)
            return Error.NotFound("Country not found.", "MasterData.Country.NotFound");

        country.Activate(timeProvider.GetUtcNow());
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

public sealed class DeactivateCountryCommandHandler(IMasterDataDbContext db, TimeProvider timeProvider)
    : ICommandHandler<DeactivateCountryCommand>
{
    public async Task<Result> Handle(DeactivateCountryCommand request, CancellationToken cancellationToken)
    {
        var country = await db.Countries.FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken);
        if (country is null)
            return Error.NotFound("Country not found.", "MasterData.Country.NotFound");

        country.Deactivate(timeProvider.GetUtcNow());
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
