using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Persistence;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using MasterData.Application.Abstractions;
using MasterData.Domain.ManpowerTypes;
using Microsoft.EntityFrameworkCore;

namespace MasterData.Application.Features.ManpowerTypes;

// --- Create ---------------------------------------------------------------

public sealed record CreateManpowerTypeCommand(string Name, string? Description) : ICommand<Guid>;

public sealed class CreateManpowerTypeCommandValidator : AbstractValidator<CreateManpowerTypeCommand>
{
    public CreateManpowerTypeCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
    }
}

public sealed class CreateManpowerTypeCommandHandler(IMasterDataDbContext db, TimeProvider timeProvider)
    : ICommandHandler<CreateManpowerTypeCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateManpowerTypeCommand request, CancellationToken cancellationToken)
    {
        var result = ManpowerType.Create(request.Name, request.Description, timeProvider.GetUtcNow());
        if (result.IsFailure)
            return result.Error;

        var manpowerType = result.Value;
        var nameExists = await db.ManpowerTypes.AnyAsync(m => m.Name == manpowerType.Name, cancellationToken);
        if (nameExists)
            return Error.Conflict("A manpower type with this name already exists.", "MasterData.ManpowerType.DuplicateName");

        db.ManpowerTypes.Add(manpowerType);
        await db.SaveChangesAsync(cancellationToken);
        return manpowerType.Id;
    }
}

// --- Update ---------------------------------------------------------------

public sealed record UpdateManpowerTypeCommand(Guid Id, string Name, string? Description, byte[] RowVersion) : ICommand;

public sealed class UpdateManpowerTypeCommandValidator : AbstractValidator<UpdateManpowerTypeCommand>
{
    public UpdateManpowerTypeCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.RowVersion).NotEmpty();
    }
}

public sealed class UpdateManpowerTypeCommandHandler(IMasterDataDbContext db, TimeProvider timeProvider)
    : ICommandHandler<UpdateManpowerTypeCommand>
{
    public async Task<Result> Handle(UpdateManpowerTypeCommand request, CancellationToken cancellationToken)
    {
        var manpowerType = await db.ManpowerTypes.FirstOrDefaultAsync(m => m.Id == request.Id, cancellationToken);
        if (manpowerType is null)
            return Error.NotFound("Manpower type not found.", "MasterData.ManpowerType.NotFound");

        var trimmedName = request.Name.Trim();
        var nameTaken = await db.ManpowerTypes.AnyAsync(m => m.Name == trimmedName && m.Id != request.Id, cancellationToken);
        if (nameTaken)
            return Error.Conflict("A manpower type with this name already exists.", "MasterData.ManpowerType.DuplicateName");

        var result = manpowerType.Update(request.Name, request.Description, timeProvider.GetUtcNow());
        if (result.IsFailure)
            return result.Error;

        db.SetOriginalRowVersion(manpowerType, request.RowVersion);

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

public sealed record ActivateManpowerTypeCommand(Guid Id, byte[] RowVersion) : ICommand;

public sealed record DeactivateManpowerTypeCommand(Guid Id, byte[] RowVersion) : ICommand;

public sealed class ActivateManpowerTypeCommandHandler(IMasterDataDbContext db, TimeProvider timeProvider)
    : ICommandHandler<ActivateManpowerTypeCommand>
{
    public async Task<Result> Handle(ActivateManpowerTypeCommand request, CancellationToken cancellationToken)
    {
        var manpowerType = await db.ManpowerTypes.FirstOrDefaultAsync(m => m.Id == request.Id, cancellationToken);
        if (manpowerType is null)
            return Error.NotFound("Manpower type not found.", "MasterData.ManpowerType.NotFound");

        manpowerType.Activate(timeProvider.GetUtcNow());
        db.SetOriginalRowVersion(manpowerType, request.RowVersion);

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

public sealed class DeactivateManpowerTypeCommandHandler(IMasterDataDbContext db, TimeProvider timeProvider)
    : ICommandHandler<DeactivateManpowerTypeCommand>
{
    public async Task<Result> Handle(DeactivateManpowerTypeCommand request, CancellationToken cancellationToken)
    {
        var manpowerType = await db.ManpowerTypes.FirstOrDefaultAsync(m => m.Id == request.Id, cancellationToken);
        if (manpowerType is null)
            return Error.NotFound("Manpower type not found.", "MasterData.ManpowerType.NotFound");

        manpowerType.Deactivate(timeProvider.GetUtcNow());
        db.SetOriginalRowVersion(manpowerType, request.RowVersion);

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
