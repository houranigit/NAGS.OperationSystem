using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Persistence;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using MasterData.Application.Abstractions;
using MasterData.Domain.GeneralSupports;
using Microsoft.EntityFrameworkCore;

namespace MasterData.Application.Features.GeneralSupports;

public sealed record CreateGeneralSupportCommand(string Name, string? Description) : ICommand<Guid>;

public sealed class CreateGeneralSupportCommandValidator : AbstractValidator<CreateGeneralSupportCommand>
{
    public CreateGeneralSupportCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(500);
    }
}

public sealed class CreateGeneralSupportCommandHandler(IMasterDataDbContext db, TimeProvider timeProvider)
    : ICommandHandler<CreateGeneralSupportCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateGeneralSupportCommand request, CancellationToken cancellationToken)
    {
        var result = GeneralSupport.Create(request.Name, request.Description, timeProvider.GetUtcNow());
        if (result.IsFailure)
            return result.Error;

        var support = result.Value;
        if (await db.GeneralSupports.AnyAsync(g => g.Name == support.Name, cancellationToken))
            return Error.Conflict("A general support item with this name already exists.", "MasterData.GeneralSupport.DuplicateName");

        db.GeneralSupports.Add(support);
        await db.SaveChangesAsync(cancellationToken);
        return support.Id;
    }
}

public sealed record UpdateGeneralSupportCommand(Guid Id, string Name, string? Description, byte[] RowVersion) : ICommand;

public sealed class UpdateGeneralSupportCommandValidator : AbstractValidator<UpdateGeneralSupportCommand>
{
    public UpdateGeneralSupportCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.RowVersion).NotEmpty();
    }
}

public sealed class UpdateGeneralSupportCommandHandler(IMasterDataDbContext db, TimeProvider timeProvider)
    : ICommandHandler<UpdateGeneralSupportCommand>
{
    public async Task<Result> Handle(UpdateGeneralSupportCommand request, CancellationToken cancellationToken)
    {
        var support = await db.GeneralSupports.FirstOrDefaultAsync(g => g.Id == request.Id, cancellationToken);
        if (support is null)
            return Error.NotFound("General support item not found.", "MasterData.GeneralSupport.NotFound");

        var trimmedName = request.Name.Trim();
        if (await db.GeneralSupports.AnyAsync(g => g.Name == trimmedName && g.Id != request.Id, cancellationToken))
            return Error.Conflict("A general support item with this name already exists.", "MasterData.GeneralSupport.DuplicateName");

        var result = support.Update(request.Name, request.Description, timeProvider.GetUtcNow());
        if (result.IsFailure)
            return result.Error;

        db.SetOriginalRowVersion(support, request.RowVersion);

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

public sealed record ActivateGeneralSupportCommand(Guid Id, byte[] RowVersion) : ICommand;
public sealed record DeactivateGeneralSupportCommand(Guid Id, byte[] RowVersion) : ICommand;

public sealed class ActivateGeneralSupportCommandHandler(IMasterDataDbContext db, TimeProvider timeProvider)
    : ICommandHandler<ActivateGeneralSupportCommand>
{
    public async Task<Result> Handle(ActivateGeneralSupportCommand request, CancellationToken cancellationToken)
    {
        var support = await db.GeneralSupports.FirstOrDefaultAsync(g => g.Id == request.Id, cancellationToken);
        if (support is null)
            return Error.NotFound("General support item not found.", "MasterData.GeneralSupport.NotFound");

        support.Activate(timeProvider.GetUtcNow());
        db.SetOriginalRowVersion(support, request.RowVersion);

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

public sealed class DeactivateGeneralSupportCommandHandler(IMasterDataDbContext db, TimeProvider timeProvider)
    : ICommandHandler<DeactivateGeneralSupportCommand>
{
    public async Task<Result> Handle(DeactivateGeneralSupportCommand request, CancellationToken cancellationToken)
    {
        var support = await db.GeneralSupports.FirstOrDefaultAsync(g => g.Id == request.Id, cancellationToken);
        if (support is null)
            return Error.NotFound("General support item not found.", "MasterData.GeneralSupport.NotFound");

        support.Deactivate(timeProvider.GetUtcNow());
        db.SetOriginalRowVersion(support, request.RowVersion);

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
