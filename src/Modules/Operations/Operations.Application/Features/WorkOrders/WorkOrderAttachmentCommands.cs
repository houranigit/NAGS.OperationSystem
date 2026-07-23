using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Persistence;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Application.Authorization;
using Operations.Domain.Enumerations;
using Operations.Domain.WorkOrders;

namespace Operations.Application.Features.WorkOrders;

public sealed record UploadWorkOrderTaskAttachmentCommand(
    Guid WorkOrderId,
    Guid TaskId,
    TaskAttachmentKind Kind,
    byte[] Content,
    string FileName,
    string ContentType,
    byte[] RowVersion) : ICommand<Guid>;

public sealed class UploadWorkOrderTaskAttachmentCommandValidator : AbstractValidator<UploadWorkOrderTaskAttachmentCommand>
{
    public UploadWorkOrderTaskAttachmentCommandValidator()
    {
        RuleFor(x => x.WorkOrderId).NotEmpty();
        RuleFor(x => x.TaskId).NotEmpty();
        RuleFor(x => x.Content).NotEmpty();
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(255);
        RuleFor(x => x.ContentType).NotEmpty().MaximumLength(100);
        RuleFor(x => x.RowVersion).NotEmpty();
    }
}

public sealed class UploadWorkOrderTaskAttachmentCommandHandler(
    IOperationsDbContext db,
    IOperationsScope scope,
    IFileStorage storage,
    IWorkOrderTimelineWriter timeline,
    IUserContext user,
    TimeProvider timeProvider) : ICommandHandler<UploadWorkOrderTaskAttachmentCommand, Guid>
{
    public async Task<Result<Guid>> Handle(UploadWorkOrderTaskAttachmentCommand request, CancellationToken cancellationToken)
    {
        var validation = WorkOrderAttachmentPolicy.Validate(request.Kind, request.Content, request.FileName, request.ContentType);
        if (validation.IsFailure)
            return validation.Error;

        var workOrder = await WorkOrderLoader.ForMutation(db.WorkOrders)
            .FirstOrDefaultAsync(w => w.Id == request.WorkOrderId, cancellationToken);
        if (workOrder is null)
            return Error.NotFound("Work order not found.", "Operations.WorkOrder.NotFound");

        var scopeResult = await scope.ResolveAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;
        var access = scopeResult.Value.EnsureWorkOrderAccess(workOrder);
        if (access.IsFailure)
            return access.Error;
        var author = WorkOrderAuthorization.EnsureManageAccess(workOrder, user);
        if (author.IsFailure)
            return author.Error;

        await using var content = new MemoryStream(request.Content);
        var stored = await storage.SaveAsync("work-order-attachments", request.FileName, request.ContentType, content, cancellationToken);

        db.SetOriginalRowVersion(workOrder, request.RowVersion);
        var added = workOrder.AddTaskAttachment(
            request.TaskId,
            request.Kind,
            stored.StorageKey,
            request.FileName,
            stored.ContentType,
            stored.SizeBytes,
            timeProvider.GetUtcNow());
        if (added.IsFailure)
        {
            await storage.DeleteAsync(stored.StorageKey, cancellationToken);
            return added.Error;
        }

        await timeline.AppendAsync(workOrder.Id, WorkOrderTimelineEventType.Updated, timeProvider.GetUtcNow(),
            details: $"Attachment added: {added.Value.OriginalFileName}", cancellationToken: cancellationToken);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await storage.DeleteAsync(stored.StorageKey, cancellationToken);
            return ConcurrencyErrors.Stale;
        }
        catch (DbUpdateException)
        {
            await storage.DeleteAsync(stored.StorageKey, cancellationToken);
            return Error.Conflict("Attachment upload conflicted with another update. Reload and try again.", "Operations.WorkOrder.AttachmentConflict");
        }

        return added.Value.Id;
    }
}

public sealed record DeleteWorkOrderTaskAttachmentCommand(
    Guid WorkOrderId,
    Guid TaskId,
    Guid AttachmentId,
    byte[] RowVersion) : ICommand;

public sealed class DeleteWorkOrderTaskAttachmentCommandValidator : AbstractValidator<DeleteWorkOrderTaskAttachmentCommand>
{
    public DeleteWorkOrderTaskAttachmentCommandValidator()
    {
        RuleFor(x => x.WorkOrderId).NotEmpty();
        RuleFor(x => x.TaskId).NotEmpty();
        RuleFor(x => x.AttachmentId).NotEmpty();
        RuleFor(x => x.RowVersion).NotEmpty();
    }
}

public sealed class DeleteWorkOrderTaskAttachmentCommandHandler(
    IOperationsDbContext db,
    IOperationsScope scope,
    IFileStorage storage,
    IWorkOrderTimelineWriter timeline,
    IUserContext user,
    TimeProvider timeProvider) : ICommandHandler<DeleteWorkOrderTaskAttachmentCommand>
{
    public async Task<Result> Handle(DeleteWorkOrderTaskAttachmentCommand request, CancellationToken cancellationToken)
    {
        var workOrder = await WorkOrderLoader.ForMutation(db.WorkOrders)
            .FirstOrDefaultAsync(w => w.Id == request.WorkOrderId, cancellationToken);
        if (workOrder is null)
            return Error.NotFound("Work order not found.", "Operations.WorkOrder.NotFound");

        var scopeResult = await scope.ResolveAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;
        var access = scopeResult.Value.EnsureWorkOrderAccess(workOrder);
        if (access.IsFailure)
            return access.Error;
        var author = WorkOrderAuthorization.EnsureManageAccess(workOrder, user);
        if (author.IsFailure)
            return author.Error;

        db.SetOriginalRowVersion(workOrder, request.RowVersion);
        var storageReference = workOrder.RemoveTaskAttachment(request.TaskId, request.AttachmentId, timeProvider.GetUtcNow());
        if (storageReference.IsFailure)
            return storageReference.Error;

        await timeline.AppendAsync(workOrder.Id, WorkOrderTimelineEventType.Updated, timeProvider.GetUtcNow(),
            details: $"Attachment removed: {request.AttachmentId}", cancellationToken: cancellationToken);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyErrors.Stale;
        }

        await storage.DeleteAsync(storageReference.Value, cancellationToken);
        return Result.Success();
    }
}

public sealed record UploadWorkOrderServiceLineAttachmentCommand(
    Guid WorkOrderId,
    Guid ServiceLineId,
    TaskAttachmentKind Kind,
    byte[] Content,
    string FileName,
    string ContentType,
    byte[] RowVersion) : ICommand<Guid>;

public sealed class UploadWorkOrderServiceLineAttachmentCommandValidator : AbstractValidator<UploadWorkOrderServiceLineAttachmentCommand>
{
    public UploadWorkOrderServiceLineAttachmentCommandValidator()
    {
        RuleFor(x => x.WorkOrderId).NotEmpty();
        RuleFor(x => x.ServiceLineId).NotEmpty();
        RuleFor(x => x.Content).NotEmpty();
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(255);
        RuleFor(x => x.ContentType).NotEmpty().MaximumLength(100);
        RuleFor(x => x.RowVersion).NotEmpty();
    }
}

public sealed class UploadWorkOrderServiceLineAttachmentCommandHandler(
    IOperationsDbContext db,
    IOperationsScope scope,
    IFileStorage storage,
    IWorkOrderTimelineWriter timeline,
    IUserContext user,
    TimeProvider timeProvider) : ICommandHandler<UploadWorkOrderServiceLineAttachmentCommand, Guid>
{
    public async Task<Result<Guid>> Handle(UploadWorkOrderServiceLineAttachmentCommand request, CancellationToken cancellationToken)
    {
        var validation = WorkOrderAttachmentPolicy.Validate(request.Kind, request.Content, request.FileName, request.ContentType);
        if (validation.IsFailure)
            return validation.Error;

        var workOrder = await WorkOrderLoader.ForMutation(db.WorkOrders)
            .FirstOrDefaultAsync(w => w.Id == request.WorkOrderId, cancellationToken);
        if (workOrder is null)
            return Error.NotFound("Work order not found.", "Operations.WorkOrder.NotFound");

        var scopeResult = await scope.ResolveAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;
        var access = scopeResult.Value.EnsureWorkOrderAccess(workOrder);
        if (access.IsFailure)
            return access.Error;
        var author = WorkOrderAuthorization.EnsureManageAccess(workOrder, user);
        if (author.IsFailure)
            return author.Error;

        await using var content = new MemoryStream(request.Content);
        var stored = await storage.SaveAsync("work-order-attachments", request.FileName, request.ContentType, content, cancellationToken);

        db.SetOriginalRowVersion(workOrder, request.RowVersion);
        var added = workOrder.AddServiceLineAttachment(
            request.ServiceLineId,
            request.Kind,
            stored.StorageKey,
            request.FileName,
            stored.ContentType,
            stored.SizeBytes,
            timeProvider.GetUtcNow());
        if (added.IsFailure)
        {
            await storage.DeleteAsync(stored.StorageKey, cancellationToken);
            return added.Error;
        }

        await timeline.AppendAsync(workOrder.Id, WorkOrderTimelineEventType.Updated, timeProvider.GetUtcNow(),
            details: $"Attachment added: {added.Value.OriginalFileName}", cancellationToken: cancellationToken);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await storage.DeleteAsync(stored.StorageKey, cancellationToken);
            return ConcurrencyErrors.Stale;
        }
        catch (DbUpdateException)
        {
            await storage.DeleteAsync(stored.StorageKey, cancellationToken);
            return Error.Conflict("Attachment upload conflicted with another update. Reload and try again.", "Operations.WorkOrder.AttachmentConflict");
        }

        return added.Value.Id;
    }
}

public sealed record DeleteWorkOrderServiceLineAttachmentCommand(
    Guid WorkOrderId,
    Guid ServiceLineId,
    Guid AttachmentId,
    byte[] RowVersion) : ICommand;

public sealed class DeleteWorkOrderServiceLineAttachmentCommandValidator : AbstractValidator<DeleteWorkOrderServiceLineAttachmentCommand>
{
    public DeleteWorkOrderServiceLineAttachmentCommandValidator()
    {
        RuleFor(x => x.WorkOrderId).NotEmpty();
        RuleFor(x => x.ServiceLineId).NotEmpty();
        RuleFor(x => x.AttachmentId).NotEmpty();
        RuleFor(x => x.RowVersion).NotEmpty();
    }
}

public sealed class DeleteWorkOrderServiceLineAttachmentCommandHandler(
    IOperationsDbContext db,
    IOperationsScope scope,
    IFileStorage storage,
    IWorkOrderTimelineWriter timeline,
    IUserContext user,
    TimeProvider timeProvider) : ICommandHandler<DeleteWorkOrderServiceLineAttachmentCommand>
{
    public async Task<Result> Handle(DeleteWorkOrderServiceLineAttachmentCommand request, CancellationToken cancellationToken)
    {
        var workOrder = await WorkOrderLoader.ForMutation(db.WorkOrders)
            .FirstOrDefaultAsync(w => w.Id == request.WorkOrderId, cancellationToken);
        if (workOrder is null)
            return Error.NotFound("Work order not found.", "Operations.WorkOrder.NotFound");

        var scopeResult = await scope.ResolveAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;
        var access = scopeResult.Value.EnsureWorkOrderAccess(workOrder);
        if (access.IsFailure)
            return access.Error;
        var author = WorkOrderAuthorization.EnsureManageAccess(workOrder, user);
        if (author.IsFailure)
            return author.Error;

        db.SetOriginalRowVersion(workOrder, request.RowVersion);
        var storageReference = workOrder.RemoveServiceLineAttachment(
            request.ServiceLineId,
            request.AttachmentId,
            timeProvider.GetUtcNow());
        if (storageReference.IsFailure)
            return storageReference.Error;

        await timeline.AppendAsync(workOrder.Id, WorkOrderTimelineEventType.Updated, timeProvider.GetUtcNow(),
            details: $"Attachment removed: {request.AttachmentId}", cancellationToken: cancellationToken);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyErrors.Stale;
        }

        await storage.DeleteAsync(storageReference.Value, cancellationToken);
        return Result.Success();
    }
}

public static class WorkOrderAttachmentPolicy
{
    public const int MaxImageBytes = 10 * 1024 * 1024;
    public const int MaxVoiceBytes = 25 * 1024 * 1024;
    public const int MaxDocumentBytes = 20 * 1024 * 1024;
    public const int MaxUploadBytes = MaxVoiceBytes;

    private static readonly HashSet<string> ImageContentTypes =
        new(StringComparer.OrdinalIgnoreCase) { "image/png", "image/jpeg", "image/webp" };

    private static readonly HashSet<string> VoiceContentTypes =
        new(StringComparer.OrdinalIgnoreCase) { "audio/mp4", "audio/m4a", "audio/mpeg", "audio/mp3", "audio/ogg", "audio/webm" };

    private static readonly HashSet<string> DocumentContentTypes =
        new(StringComparer.OrdinalIgnoreCase) { "application/pdf" };

    public static Result Validate(TaskAttachmentKind kind, byte[] content, string fileName, string contentType)
    {
        if (content.Length == 0)
            return Error.Validation("The attachment file is empty.", "Operations.WorkOrder.AttachmentEmpty");
        if (content.Length > MaxBytes(kind))
            return Error.Validation($"The {kind.ToString().ToLowerInvariant()} attachment exceeds the size limit.", "Operations.WorkOrder.AttachmentTooLarge");

        var normalizedContentType = string.IsNullOrWhiteSpace(contentType) ? ContentTypeFromFileName(fileName) : contentType.Trim();
        if (!AllowedContentTypes(kind).Contains(normalizedContentType))
            return Error.Validation("The attachment file type is not allowed.", "Operations.WorkOrder.AttachmentInvalidType");
        if (!HasExpectedSignature(kind, normalizedContentType, content))
            return Error.Validation("The attachment content does not match the selected type.", "Operations.WorkOrder.AttachmentInvalidSignature");

        return Result.Success();
    }

    private static int MaxBytes(TaskAttachmentKind kind) => kind switch
    {
        TaskAttachmentKind.Image => MaxImageBytes,
        TaskAttachmentKind.Voice => MaxVoiceBytes,
        TaskAttachmentKind.Document => MaxDocumentBytes,
        _ => MaxDocumentBytes
    };

    private static HashSet<string> AllowedContentTypes(TaskAttachmentKind kind) => kind switch
    {
        TaskAttachmentKind.Image => ImageContentTypes,
        TaskAttachmentKind.Voice => VoiceContentTypes,
        TaskAttachmentKind.Document => DocumentContentTypes,
        _ => DocumentContentTypes
    };

    private static string ContentTypeFromFileName(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        ".m4a" => "audio/mp4",
        ".mp3" => "audio/mpeg",
        ".ogg" => "audio/ogg",
        ".pdf" => "application/pdf",
        _ => "application/octet-stream"
    };

    private static bool HasExpectedSignature(TaskAttachmentKind kind, string contentType, byte[] content) => kind switch
    {
        TaskAttachmentKind.Image => HasImageSignature(contentType, content),
        TaskAttachmentKind.Document => HasPdfSignature(content),
        TaskAttachmentKind.Voice => HasVoiceSignature(contentType, content),
        _ => false
    };

    private static bool HasImageSignature(string contentType, byte[] content)
    {
        if (content.Length < 12)
            return false;
        if (contentType.Equals("image/png", StringComparison.OrdinalIgnoreCase))
            return content[0] == 0x89 && content[1] == 0x50 && content[2] == 0x4E && content[3] == 0x47;
        if (contentType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase))
            return content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF;
        if (contentType.Equals("image/webp", StringComparison.OrdinalIgnoreCase))
            return content[0] == 0x52 && content[1] == 0x49 && content[2] == 0x46 && content[3] == 0x46
                && content[8] == 0x57 && content[9] == 0x45 && content[10] == 0x42 && content[11] == 0x50;

        return false;
    }

    private static bool HasPdfSignature(byte[] content) =>
        content.Length >= 5
        && content[0] == 0x25
        && content[1] == 0x50
        && content[2] == 0x44
        && content[3] == 0x46
        && content[4] == 0x2D;

    private static bool HasVoiceSignature(string contentType, byte[] content)
    {
        if (content.Length < 4)
            return false;
        if (contentType.Equals("audio/ogg", StringComparison.OrdinalIgnoreCase))
            return content[0] == 0x4F && content[1] == 0x67 && content[2] == 0x67 && content[3] == 0x53;
        if (contentType.Equals("audio/mpeg", StringComparison.OrdinalIgnoreCase) || contentType.Equals("audio/mp3", StringComparison.OrdinalIgnoreCase))
            return (content[0] == 0x49 && content[1] == 0x44 && content[2] == 0x33) || (content[0] == 0xFF && (content[1] & 0xE0) == 0xE0);
        if (contentType.Equals("audio/mp4", StringComparison.OrdinalIgnoreCase) || contentType.Equals("audio/m4a", StringComparison.OrdinalIgnoreCase))
            return content.Length >= 12 && content[4] == 0x66 && content[5] == 0x74 && content[6] == 0x79 && content[7] == 0x70;

        return contentType.Equals("audio/webm", StringComparison.OrdinalIgnoreCase);
    }
}

internal static class WorkOrderAttachmentStorage
{
    public static IReadOnlyList<string> References(WorkOrder workOrder) =>
        workOrder.ServiceLines
            .SelectMany(line => line.Attachments)
            .Select(attachment => attachment.StorageReference)
            .Concat(workOrder.Tasks
                .SelectMany(task => task.Attachments)
                .Select(attachment => attachment.StorageReference))
            .Concat([workOrder.CustomerSignatureReference])
            .Where(reference => !string.IsNullOrWhiteSpace(reference))
            .Select(reference => reference!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static async Task DeleteAsync(IFileStorage storage, IEnumerable<string> references, CancellationToken cancellationToken)
    {
        foreach (var reference in references.Distinct(StringComparer.OrdinalIgnoreCase))
            await storage.DeleteAsync(reference, cancellationToken);
    }
}
