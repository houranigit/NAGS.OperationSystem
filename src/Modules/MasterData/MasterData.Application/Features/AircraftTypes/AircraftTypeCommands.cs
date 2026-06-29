using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Persistence;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using MasterData.Application.Abstractions;
using MasterData.Domain.AircraftTypes;
using Microsoft.EntityFrameworkCore;

namespace MasterData.Application.Features.AircraftTypes;

public sealed record CreateAircraftTypeCommand(AircraftManufacturer Manufacturer, string Model, string? Notes) : ICommand<Guid>;

public sealed class CreateAircraftTypeCommandValidator : AbstractValidator<CreateAircraftTypeCommand>
{
    public CreateAircraftTypeCommandValidator()
    {
        RuleFor(x => x.Manufacturer).IsInEnum();
        RuleFor(x => x.Model).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}

public sealed class CreateAircraftTypeCommandHandler(IMasterDataDbContext db, TimeProvider timeProvider)
    : ICommandHandler<CreateAircraftTypeCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateAircraftTypeCommand request, CancellationToken cancellationToken)
    {
        var result = AircraftType.Create(request.Manufacturer, request.Model, request.Notes, timeProvider.GetUtcNow());
        if (result.IsFailure)
            return result.Error;

        var aircraftType = result.Value;
        if (await db.AircraftTypes.AnyAsync(a => a.Manufacturer == aircraftType.Manufacturer && a.Model == aircraftType.Model, cancellationToken))
            return Error.Conflict("An aircraft type with this manufacturer and model already exists.", "MasterData.AircraftType.Duplicate");

        db.AircraftTypes.Add(aircraftType);
        await db.SaveChangesAsync(cancellationToken);
        return aircraftType.Id;
    }
}

public sealed record UpdateAircraftTypeCommand(Guid Id, AircraftManufacturer Manufacturer, string Model, string? Notes, byte[] RowVersion) : ICommand;

public sealed class UpdateAircraftTypeCommandValidator : AbstractValidator<UpdateAircraftTypeCommand>
{
    public UpdateAircraftTypeCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Manufacturer).IsInEnum();
        RuleFor(x => x.Model).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Notes).MaximumLength(500);
        RuleFor(x => x.RowVersion).NotEmpty();
    }
}

public sealed class UpdateAircraftTypeCommandHandler(IMasterDataDbContext db, TimeProvider timeProvider)
    : ICommandHandler<UpdateAircraftTypeCommand>
{
    public async Task<Result> Handle(UpdateAircraftTypeCommand request, CancellationToken cancellationToken)
    {
        var aircraftType = await db.AircraftTypes.FirstOrDefaultAsync(a => a.Id == request.Id, cancellationToken);
        if (aircraftType is null)
            return Error.NotFound("Aircraft type not found.", "MasterData.AircraftType.NotFound");

        var result = aircraftType.Update(request.Manufacturer, request.Model, request.Notes, timeProvider.GetUtcNow());
        if (result.IsFailure)
            return result.Error;

        if (await db.AircraftTypes.AnyAsync(a =>
                a.Manufacturer == aircraftType.Manufacturer && a.Model == aircraftType.Model && a.Id != request.Id,
                cancellationToken))
            return Error.Conflict("An aircraft type with this manufacturer and model already exists.", "MasterData.AircraftType.Duplicate");

        db.SetOriginalRowVersion(aircraftType, request.RowVersion);

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

public sealed record ActivateAircraftTypeCommand(Guid Id, byte[] RowVersion) : ICommand;
public sealed record DeactivateAircraftTypeCommand(Guid Id, byte[] RowVersion) : ICommand;

public sealed class ActivateAircraftTypeCommandHandler(IMasterDataDbContext db, TimeProvider timeProvider)
    : ICommandHandler<ActivateAircraftTypeCommand>
{
    public async Task<Result> Handle(ActivateAircraftTypeCommand request, CancellationToken cancellationToken)
    {
        var aircraftType = await db.AircraftTypes.FirstOrDefaultAsync(a => a.Id == request.Id, cancellationToken);
        if (aircraftType is null)
            return Error.NotFound("Aircraft type not found.", "MasterData.AircraftType.NotFound");

        aircraftType.Activate(timeProvider.GetUtcNow());
        db.SetOriginalRowVersion(aircraftType, request.RowVersion);

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

public sealed class DeactivateAircraftTypeCommandHandler(IMasterDataDbContext db, TimeProvider timeProvider)
    : ICommandHandler<DeactivateAircraftTypeCommand>
{
    public async Task<Result> Handle(DeactivateAircraftTypeCommand request, CancellationToken cancellationToken)
    {
        var aircraftType = await db.AircraftTypes.FirstOrDefaultAsync(a => a.Id == request.Id, cancellationToken);
        if (aircraftType is null)
            return Error.NotFound("Aircraft type not found.", "MasterData.AircraftType.NotFound");

        aircraftType.Deactivate(timeProvider.GetUtcNow());
        db.SetOriginalRowVersion(aircraftType, request.RowVersion);

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
