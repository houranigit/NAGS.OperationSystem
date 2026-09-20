using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Mobile;
using BuildingBlocks.Application.Persistence;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using MasterData.Application.Abstractions;
using MasterData.Domain.AtaChapters;
using Microsoft.EntityFrameworkCore;

namespace MasterData.Application.Features.AtaChapters;

public sealed record CreateAtaChapterCategoryCommand(string Name) : ICommand<Guid>;

public sealed class CreateAtaChapterCategoryCommandValidator : AbstractValidator<CreateAtaChapterCategoryCommand>
{
    public CreateAtaChapterCategoryCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
    }
}

public sealed class CreateAtaChapterCategoryCommandHandler(IMasterDataDbContext db, TimeProvider timeProvider, IMobileSyncBroadcaster mobileSync)
    : ICommandHandler<CreateAtaChapterCategoryCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateAtaChapterCategoryCommand request, CancellationToken cancellationToken)
    {
        var result = AtaChapterCategory.Create(request.Name, timeProvider.GetUtcNow());
        if (result.IsFailure)
            return result.Error;

        var item = result.Value;
        if (await db.AtaChapterCategories.AnyAsync(o => o.Name == item.Name, cancellationToken))
            return Error.Conflict("An ATA chapter category with this name already exists.", "MasterData.AtaChapterCategory.DuplicateName");

        db.AtaChapterCategories.Add(item);
        await db.SaveChangesAsync(cancellationToken);
        mobileSync.Enqueue(new MobileSyncChange(MobileSyncTables.AtaChapters, MobileSyncOps.Refresh, null, MobileSyncAudience.AllStations, timeProvider.GetUtcNow()));
        return item.Id;
    }
}

public sealed record UpdateAtaChapterCategoryCommand(Guid Id, string Name, byte[] RowVersion) : ICommand;

public sealed class UpdateAtaChapterCategoryCommandValidator : AbstractValidator<UpdateAtaChapterCategoryCommand>
{
    public UpdateAtaChapterCategoryCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.RowVersion).NotEmpty();
    }
}

public sealed class UpdateAtaChapterCategoryCommandHandler(IMasterDataDbContext db, TimeProvider timeProvider, IMobileSyncBroadcaster mobileSync)
    : ICommandHandler<UpdateAtaChapterCategoryCommand>
{
    public async Task<Result> Handle(UpdateAtaChapterCategoryCommand request, CancellationToken cancellationToken)
    {
        var item = await db.AtaChapterCategories.FirstOrDefaultAsync(o => o.Id == request.Id, cancellationToken);
        if (item is null)
            return Error.NotFound("ATA chapter category not found.", "MasterData.AtaChapterCategory.NotFound");

        var trimmedName = request.Name.Trim();
        if (await db.AtaChapterCategories.AnyAsync(o => o.Name == trimmedName && o.Id != request.Id, cancellationToken))
            return Error.Conflict("An ATA chapter category with this name already exists.", "MasterData.AtaChapterCategory.DuplicateName");

        var result = item.Update(request.Name, timeProvider.GetUtcNow());
        if (result.IsFailure)
            return result.Error;

        db.SetOriginalRowVersion(item, request.RowVersion);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            mobileSync.Enqueue(new MobileSyncChange(MobileSyncTables.AtaChapters, MobileSyncOps.Refresh, null, MobileSyncAudience.AllStations, timeProvider.GetUtcNow()));
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyErrors.Stale;
        }

        return Result.Success();
    }
}

public sealed record ActivateAtaChapterCategoryCommand(Guid Id, byte[] RowVersion) : ICommand;
public sealed record DeactivateAtaChapterCategoryCommand(Guid Id, byte[] RowVersion) : ICommand;

public sealed class ActivateAtaChapterCategoryCommandHandler(IMasterDataDbContext db, TimeProvider timeProvider, IMobileSyncBroadcaster mobileSync)
    : ICommandHandler<ActivateAtaChapterCategoryCommand>
{
    public async Task<Result> Handle(ActivateAtaChapterCategoryCommand request, CancellationToken cancellationToken)
    {
        var item = await db.AtaChapterCategories.FirstOrDefaultAsync(o => o.Id == request.Id, cancellationToken);
        if (item is null)
            return Error.NotFound("ATA chapter category not found.", "MasterData.AtaChapterCategory.NotFound");

        if (!item.RowVersion.SequenceEqual(request.RowVersion))
            return ConcurrencyErrors.Stale;

        item.Activate(timeProvider.GetUtcNow());
        db.SetOriginalRowVersion(item, request.RowVersion);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            mobileSync.Enqueue(new MobileSyncChange(MobileSyncTables.AtaChapters, MobileSyncOps.Refresh, null, MobileSyncAudience.AllStations, timeProvider.GetUtcNow()));
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyErrors.Stale;
        }

        return Result.Success();
    }
}

public sealed class DeactivateAtaChapterCategoryCommandHandler(IMasterDataDbContext db, TimeProvider timeProvider, IMobileSyncBroadcaster mobileSync)
    : ICommandHandler<DeactivateAtaChapterCategoryCommand>
{
    public async Task<Result> Handle(DeactivateAtaChapterCategoryCommand request, CancellationToken cancellationToken)
    {
        var item = await db.AtaChapterCategories.FirstOrDefaultAsync(o => o.Id == request.Id, cancellationToken);
        if (item is null)
            return Error.NotFound("ATA chapter category not found.", "MasterData.AtaChapterCategory.NotFound");

        if (!item.RowVersion.SequenceEqual(request.RowVersion))
            return ConcurrencyErrors.Stale;

        item.Deactivate(timeProvider.GetUtcNow());
        db.SetOriginalRowVersion(item, request.RowVersion);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            mobileSync.Enqueue(new MobileSyncChange(MobileSyncTables.AtaChapters, MobileSyncOps.Refresh, null, MobileSyncAudience.AllStations, timeProvider.GetUtcNow()));
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyErrors.Stale;
        }

        return Result.Success();
    }
}

public sealed class ActivateAtaChapterCategoryCommandValidator : AbstractValidator<ActivateAtaChapterCategoryCommand>
{
    public ActivateAtaChapterCategoryCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.RowVersion).NotEmpty();
    }
}

public sealed class DeactivateAtaChapterCategoryCommandValidator : AbstractValidator<DeactivateAtaChapterCategoryCommand>
{
    public DeactivateAtaChapterCategoryCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.RowVersion).NotEmpty();
    }
}
