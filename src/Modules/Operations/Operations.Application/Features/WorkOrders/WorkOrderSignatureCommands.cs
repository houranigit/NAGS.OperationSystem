using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Persistence;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Application.Authorization;
using Operations.Domain.WorkOrders;

namespace Operations.Application.Features.WorkOrders;

public sealed record UploadWorkOrderSignatureCommand(
    Guid Id,
    byte[] Content,
    string FileName,
    string ContentType,
    byte[] RowVersion) : ICommand;

public sealed class UploadWorkOrderSignatureCommandValidator : AbstractValidator<UploadWorkOrderSignatureCommand>
{
    public UploadWorkOrderSignatureCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Content).NotEmpty();
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(255);
        RuleFor(x => x.ContentType).NotEmpty().MaximumLength(100);
        RuleFor(x => x.RowVersion).NotEmpty();
    }
}

public sealed class UploadWorkOrderSignatureCommandHandler(
    IOperationsDbContext db,
    IOperationsScope scope,
    IFileStorage storage,
    IWorkOrderTimelineWriter timeline,
    IUserContext user,
    TimeProvider timeProvider) : ICommandHandler<UploadWorkOrderSignatureCommand>
{
    public async Task<Result> Handle(UploadWorkOrderSignatureCommand request, CancellationToken cancellationToken)
    {
        var validation = WorkOrderSignaturePolicy.Validate(request.Content, request.FileName, request.ContentType);
        if (validation.IsFailure)
            return validation.Error;

        var workOrder = await WorkOrderLoader.ForMutation(db.WorkOrders)
            .FirstOrDefaultAsync(w => w.Id == request.Id, cancellationToken);
        if (workOrder is null)
            return Error.NotFound("Work order not found.", "Operations.WorkOrder.NotFound");

        var scopeResult = await scope.ResolveAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;
        var access = scopeResult.Value.EnsureWorkOrderAccess(workOrder);
        if (access.IsFailure)
            return access.Error;
        var author = WorkOrderAuthorization.EnsureAuthorAccess(workOrder, user);
        if (author.IsFailure)
            return author.Error;

        var previousReference = workOrder.CustomerSignatureReference;
        await using var content = new MemoryStream(request.Content);
        var stored = await storage.SaveAsync("work-order-signatures", request.FileName, request.ContentType, content, cancellationToken);

        db.SetOriginalRowVersion(workOrder, request.RowVersion);
        var set = workOrder.SetCustomerSignature(stored.StorageKey, request.FileName, stored.ContentType, stored.SizeBytes, timeProvider.GetUtcNow());
        if (set.IsFailure)
        {
            await storage.DeleteAsync(stored.StorageKey, cancellationToken);
            return set.Error;
        }

        await timeline.AppendAsync(workOrder.Id, Domain.Enumerations.WorkOrderTimelineEventType.Updated, timeProvider.GetUtcNow(),
            details: "Customer signature captured.", cancellationToken: cancellationToken);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await storage.DeleteAsync(stored.StorageKey, cancellationToken);
            return ConcurrencyErrors.Stale;
        }

        if (!string.IsNullOrWhiteSpace(previousReference))
            await storage.DeleteAsync(previousReference, cancellationToken);

        return Result.Success();
    }
}

public sealed record DeleteWorkOrderSignatureCommand(Guid Id, byte[] RowVersion) : ICommand;

public sealed class DeleteWorkOrderSignatureCommandValidator : AbstractValidator<DeleteWorkOrderSignatureCommand>
{
    public DeleteWorkOrderSignatureCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.RowVersion).NotEmpty();
    }
}

public sealed class DeleteWorkOrderSignatureCommandHandler(
    IOperationsDbContext db,
    IOperationsScope scope,
    IFileStorage storage,
    IWorkOrderTimelineWriter timeline,
    IUserContext user,
    TimeProvider timeProvider) : ICommandHandler<DeleteWorkOrderSignatureCommand>
{
    public async Task<Result> Handle(DeleteWorkOrderSignatureCommand request, CancellationToken cancellationToken)
    {
        var workOrder = await WorkOrderLoader.ForMutation(db.WorkOrders)
            .FirstOrDefaultAsync(w => w.Id == request.Id, cancellationToken);
        if (workOrder is null)
            return Error.NotFound("Work order not found.", "Operations.WorkOrder.NotFound");

        var scopeResult = await scope.ResolveAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;
        var access = scopeResult.Value.EnsureWorkOrderAccess(workOrder);
        if (access.IsFailure)
            return access.Error;
        var author = WorkOrderAuthorization.EnsureAuthorAccess(workOrder, user);
        if (author.IsFailure)
            return author.Error;

        db.SetOriginalRowVersion(workOrder, request.RowVersion);
        var storageReference = workOrder.RemoveCustomerSignature(timeProvider.GetUtcNow());
        if (storageReference.IsFailure)
            return storageReference.Error;

        await timeline.AppendAsync(workOrder.Id, Domain.Enumerations.WorkOrderTimelineEventType.Updated, timeProvider.GetUtcNow(),
            details: "Customer signature removed.", cancellationToken: cancellationToken);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyErrors.Stale;
        }

        if (!string.IsNullOrWhiteSpace(storageReference.Value))
            await storage.DeleteAsync(storageReference.Value, cancellationToken);

        return Result.Success();
    }
}

public static class WorkOrderSignaturePolicy
{
    public const int MaxSignatureBytes = 2 * 1024 * 1024;

    public static Result Validate(byte[] content, string fileName, string contentType)
    {
        if (content.Length == 0)
            return Error.Validation("The signature file is empty.", "Operations.WorkOrder.SignatureEmpty");
        if (content.Length > MaxSignatureBytes)
            return Error.Validation("The signature image must be at most 2 MB.", "Operations.WorkOrder.SignatureTooLarge");
        if (!contentType.Equals("image/png", StringComparison.OrdinalIgnoreCase) && !Path.GetExtension(fileName).Equals(".png", StringComparison.OrdinalIgnoreCase))
            return Error.Validation("The signature must be a PNG image.", "Operations.WorkOrder.SignatureInvalidType");
        if (content.Length < 4 || content[0] != 0x89 || content[1] != 0x50 || content[2] != 0x4E || content[3] != 0x47)
            return Error.Validation("The signature content does not match PNG format.", "Operations.WorkOrder.SignatureInvalidSignature");

        return Result.Success();
    }
}
