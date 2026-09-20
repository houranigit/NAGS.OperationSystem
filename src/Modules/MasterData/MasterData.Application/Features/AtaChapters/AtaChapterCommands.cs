using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Mobile;
using BuildingBlocks.Application.Persistence;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using MasterData.Application.Abstractions;
using MasterData.Domain.AtaChapters;
using Microsoft.EntityFrameworkCore;

namespace MasterData.Application.Features.AtaChapters;

public sealed record CreateAtaChapterCommand(Guid CategoryId, string Code, string Title) : ICommand<Guid>;

public sealed class CreateAtaChapterCommandValidator : AbstractValidator<CreateAtaChapterCommand>
{
    public CreateAtaChapterCommandValidator()
    {
        RuleFor(x => x.CategoryId).NotEmpty();
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
    }
}

public sealed class CreateAtaChapterCommandHandler(IMasterDataDbContext db, TimeProvider timeProvider, IMobileSyncBroadcaster mobileSync)
    : ICommandHandler<CreateAtaChapterCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateAtaChapterCommand request, CancellationToken cancellationToken)
    {
        var result = AtaChapter.Create(request.CategoryId, request.Code, request.Title, timeProvider.GetUtcNow());
        if (result.IsFailure)
            return result.Error;

        var item = result.Value;
        if (await db.AtaChapters.AnyAsync(o => o.Code == item.Code, cancellationToken))
            return Error.Conflict("An ATA chapter with this code already exists.", "MasterData.AtaChapter.DuplicateCode");

        if (!await db.AtaChapterCategories.AnyAsync(c => c.Id == request.CategoryId, cancellationToken))
            return Error.Validation("ATA chapter category not found.", "MasterData.AtaChapter.CategoryNotFound");

        db.AtaChapters.Add(item);
        await db.SaveChangesAsync(cancellationToken);
        mobileSync.Enqueue(new MobileSyncChange(MobileSyncTables.AtaChapters, MobileSyncOps.Refresh, null, MobileSyncAudience.AllStations, timeProvider.GetUtcNow()));
        return item.Id;
    }
}

public sealed record UpdateAtaChapterCommand(Guid Id, Guid CategoryId, string Code, string Title, byte[] RowVersion) : ICommand;

public sealed class UpdateAtaChapterCommandValidator : AbstractValidator<UpdateAtaChapterCommand>
{
    public UpdateAtaChapterCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.CategoryId).NotEmpty();
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.RowVersion).NotEmpty();
    }
}

public sealed class UpdateAtaChapterCommandHandler(IMasterDataDbContext db, TimeProvider timeProvider, IMobileSyncBroadcaster mobileSync)
    : ICommandHandler<UpdateAtaChapterCommand>
{
    public async Task<Result> Handle(UpdateAtaChapterCommand request, CancellationToken cancellationToken)
    {
        var item = await db.AtaChapters.FirstOrDefaultAsync(o => o.Id == request.Id, cancellationToken);
        if (item is null)
            return Error.NotFound("ATA chapter not found.", "MasterData.AtaChapter.NotFound");

        var trimmedCode = request.Code.Trim();
        if (await db.AtaChapters.AnyAsync(o => o.Code == trimmedCode && o.Id != request.Id, cancellationToken))
            return Error.Conflict("An ATA chapter with this code already exists.", "MasterData.AtaChapter.DuplicateCode");

        if (!await db.AtaChapterCategories.AnyAsync(c => c.Id == request.CategoryId, cancellationToken))
            return Error.Validation("ATA chapter category not found.", "MasterData.AtaChapter.CategoryNotFound");

        var result = item.Update(request.CategoryId, request.Code, request.Title, timeProvider.GetUtcNow());
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

public sealed record ActivateAtaChapterCommand(Guid Id, byte[] RowVersion) : ICommand;
public sealed record DeactivateAtaChapterCommand(Guid Id, byte[] RowVersion) : ICommand;

public sealed class ActivateAtaChapterCommandHandler(IMasterDataDbContext db, TimeProvider timeProvider, IMobileSyncBroadcaster mobileSync)
    : ICommandHandler<ActivateAtaChapterCommand>
{
    public async Task<Result> Handle(ActivateAtaChapterCommand request, CancellationToken cancellationToken)
    {
        var item = await db.AtaChapters.FirstOrDefaultAsync(o => o.Id == request.Id, cancellationToken);
        if (item is null)
            return Error.NotFound("ATA chapter not found.", "MasterData.AtaChapter.NotFound");

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

public sealed class DeactivateAtaChapterCommandHandler(IMasterDataDbContext db, TimeProvider timeProvider, IMobileSyncBroadcaster mobileSync)
    : ICommandHandler<DeactivateAtaChapterCommand>
{
    public async Task<Result> Handle(DeactivateAtaChapterCommand request, CancellationToken cancellationToken)
    {
        var item = await db.AtaChapters.FirstOrDefaultAsync(o => o.Id == request.Id, cancellationToken);
        if (item is null)
            return Error.NotFound("ATA chapter not found.", "MasterData.AtaChapter.NotFound");

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

public sealed class ActivateAtaChapterCommandValidator : AbstractValidator<ActivateAtaChapterCommand>
{
    public ActivateAtaChapterCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.RowVersion).NotEmpty();
    }
}

public sealed class DeactivateAtaChapterCommandValidator : AbstractValidator<DeactivateAtaChapterCommand>
{
    public DeactivateAtaChapterCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.RowVersion).NotEmpty();
    }
}
