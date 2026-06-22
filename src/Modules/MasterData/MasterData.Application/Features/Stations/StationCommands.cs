using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Persistence;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using MasterData.Application.Abstractions;
using MasterData.Application.Authorization;
using MasterData.Domain.Stations;
using Microsoft.EntityFrameworkCore;

namespace MasterData.Application.Features.Stations;

// --- Create ---------------------------------------------------------------

public sealed record CreateStationCommand(string IataCode, string? IcaoCode, string Name, string City, Guid CountryId) : ICommand<Guid>;

public sealed class CreateStationCommandValidator : AbstractValidator<CreateStationCommand>
{
    public CreateStationCommandValidator()
    {
        RuleFor(x => x.IataCode).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.City).NotEmpty().MaximumLength(100);
        RuleFor(x => x.CountryId).NotEmpty();
    }
}

public sealed class CreateStationCommandHandler(IMasterDataDbContext db, TimeProvider timeProvider)
    : ICommandHandler<CreateStationCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateStationCommand request, CancellationToken cancellationToken)
    {
        var countryCheck = await StationGuards.EnsureActiveCountryAsync(db, request.CountryId, cancellationToken);
        if (countryCheck.IsFailure)
            return countryCheck.Error;

        var result = Station.Create(request.IataCode, request.IcaoCode, request.Name, request.City, request.CountryId, timeProvider.GetUtcNow());
        if (result.IsFailure)
            return result.Error;

        var station = result.Value;

        var conflict = await StationGuards.EnsureCodesAvailableAsync(db, station.IataCode, station.IcaoCode, null, cancellationToken);
        if (conflict.IsFailure)
            return conflict.Error;

        db.Stations.Add(station);
        await db.SaveChangesAsync(cancellationToken);
        return station.Id;
    }
}

// --- Update ---------------------------------------------------------------

public sealed record UpdateStationCommand(Guid Id, string IataCode, string? IcaoCode, string Name, string City, Guid CountryId, byte[] RowVersion) : ICommand;

public sealed class UpdateStationCommandValidator : AbstractValidator<UpdateStationCommand>
{
    public UpdateStationCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.IataCode).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.City).NotEmpty().MaximumLength(100);
        RuleFor(x => x.CountryId).NotEmpty();
        RuleFor(x => x.RowVersion).NotEmpty();
    }
}

public sealed class UpdateStationCommandHandler(IMasterDataDbContext db, IMasterDataScope scope, TimeProvider timeProvider)
    : ICommandHandler<UpdateStationCommand>
{
    public async Task<Result> Handle(UpdateStationCommand request, CancellationToken cancellationToken)
    {
        var scopeCheck = await scope.CheckStationAsync(request.Id, cancellationToken);
        if (scopeCheck.IsFailure)
            return scopeCheck.Error;

        var station = await db.Stations.FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken);
        if (station is null)
            return Error.NotFound("Station not found.", "MasterData.Station.NotFound");

        var countryCheck = await StationGuards.EnsureActiveCountryAsync(db, request.CountryId, cancellationToken);
        if (countryCheck.IsFailure)
            return countryCheck.Error;

        var result = station.Update(request.IataCode, request.IcaoCode, request.Name, request.City, request.CountryId, timeProvider.GetUtcNow());
        if (result.IsFailure)
            return result.Error;

        var conflict = await StationGuards.EnsureCodesAvailableAsync(db, station.IataCode, station.IcaoCode, station.Id, cancellationToken);
        if (conflict.IsFailure)
            return conflict.Error;

        db.SetOriginalRowVersion(station, request.RowVersion);

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

// --- Activate / Deactivate ------------------------------------------------

public sealed record ActivateStationCommand(Guid Id, byte[] RowVersion) : ICommand;

public sealed record DeactivateStationCommand(Guid Id, byte[] RowVersion) : ICommand;

public sealed class ActivateStationCommandHandler(IMasterDataDbContext db, TimeProvider timeProvider)
    : ICommandHandler<ActivateStationCommand>
{
    public async Task<Result> Handle(ActivateStationCommand request, CancellationToken cancellationToken)
    {
        var station = await db.Stations.FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken);
        if (station is null)
            return Error.NotFound("Station not found.", "MasterData.Station.NotFound");

        station.Activate(timeProvider.GetUtcNow());
        db.SetOriginalRowVersion(station, request.RowVersion);

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

public sealed class DeactivateStationCommandHandler(IMasterDataDbContext db, TimeProvider timeProvider)
    : ICommandHandler<DeactivateStationCommand>
{
    public async Task<Result> Handle(DeactivateStationCommand request, CancellationToken cancellationToken)
    {
        var station = await db.Stations.FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken);
        if (station is null)
            return Error.NotFound("Station not found.", "MasterData.Station.NotFound");

        if (station.IsActive)
        {
            // Deactivating a station blocks access for all of its linked staff members.
            var linkedStaff = await db.StaffMembers
                .Where(s => s.StationId == station.Id && s.IsActive && s.LinkedUserId != null)
                .Select(s => new { s.Id, s.LinkedUserId })
                .ToListAsync(cancellationToken);

            foreach (var staff in linkedStaff)
                PortalAccess.PortalLifecycle.EnqueueDeactivation(db, staff.Id, staff.LinkedUserId!.Value);
        }

        station.Deactivate(timeProvider.GetUtcNow());
        db.SetOriginalRowVersion(station, request.RowVersion);

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

internal static class StationGuards
{
    public static async Task<Result> EnsureActiveCountryAsync(IMasterDataDbContext db, Guid countryId, CancellationToken cancellationToken)
    {
        var country = await db.Countries.FirstOrDefaultAsync(c => c.Id == countryId, cancellationToken);
        if (country is null)
            return Error.NotFound("The selected country was not found.", "MasterData.Station.CountryNotFound");

        if (!country.IsActive)
            return Error.Validation("The selected country is inactive.", "MasterData.Station.CountryInactive");

        return Result.Success();
    }

    public static async Task<Result> EnsureCodesAvailableAsync(
        IMasterDataDbContext db, string iataCode, string? icaoCode, Guid? excludeId, CancellationToken cancellationToken)
    {
        var iataTaken = await db.Stations.AnyAsync(s => s.IataCode == iataCode && (excludeId == null || s.Id != excludeId), cancellationToken);
        if (iataTaken)
            return Error.Conflict("A station with this IATA code already exists.", "MasterData.Station.DuplicateIata");

        if (icaoCode is not null)
        {
            var icaoTaken = await db.Stations.AnyAsync(s => s.IcaoCode == icaoCode && (excludeId == null || s.Id != excludeId), cancellationToken);
            if (icaoTaken)
                return Error.Conflict("A station with this ICAO code already exists.", "MasterData.Station.DuplicateIcao");
        }

        return Result.Success();
    }
}
