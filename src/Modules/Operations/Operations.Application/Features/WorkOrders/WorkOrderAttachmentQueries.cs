using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Domain.Results;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Application.Authorization;

namespace Operations.Application.Features.WorkOrders;

public sealed record WorkOrderAttachmentContent(byte[] Content, string ContentType, string FileName);

public sealed record GetWorkOrderTaskAttachmentContentQuery(
    Guid WorkOrderId,
    Guid TaskId,
    Guid AttachmentId) : IQuery<WorkOrderAttachmentContent>;

public sealed class GetWorkOrderTaskAttachmentContentQueryHandler(
    IOperationsDbContext db,
    IOperationsScope scope,
    IFileStorage storage) : IQueryHandler<GetWorkOrderTaskAttachmentContentQuery, WorkOrderAttachmentContent>
{
    public async Task<Result<WorkOrderAttachmentContent>> Handle(GetWorkOrderTaskAttachmentContentQuery request, CancellationToken cancellationToken)
    {
        var workOrder = await WorkOrderLoader.ForMutation(db.WorkOrders.AsNoTracking())
            .FirstOrDefaultAsync(w => w.Id == request.WorkOrderId, cancellationToken);
        if (workOrder is null)
            return Error.NotFound("Work order not found.", "Operations.WorkOrder.NotFound");

        var scopeResult = await scope.ResolveAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;
        var access = scopeResult.Value.EnsureWorkOrderAccess(workOrder);
        if (access.IsFailure)
            return access.Error;

        var task = workOrder.Tasks.FirstOrDefault(t => t.Id == request.TaskId);
        if (task is null)
            return Error.NotFound("Task not found.", "Operations.WorkOrder.TaskNotFound");

        var attachment = task.Attachments.FirstOrDefault(a => a.Id == request.AttachmentId);
        if (attachment is null)
            return Error.NotFound("Attachment not found.", "Operations.WorkOrder.AttachmentNotFound");

        await using var stream = await storage.OpenAsync(attachment.StorageReference, cancellationToken);
        if (stream is null)
            return Error.NotFound("Attachment file not found.", "Operations.WorkOrder.AttachmentFileNotFound");

        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);
        return new WorkOrderAttachmentContent(memory.ToArray(), attachment.ContentType, attachment.OriginalFileName);
    }
}

public sealed record GetWorkOrderServiceLineAttachmentContentQuery(
    Guid WorkOrderId,
    Guid ServiceLineId,
    Guid AttachmentId) : IQuery<WorkOrderAttachmentContent>;

public sealed class GetWorkOrderServiceLineAttachmentContentQueryHandler(
    IOperationsDbContext db,
    IOperationsScope scope,
    IFileStorage storage) : IQueryHandler<GetWorkOrderServiceLineAttachmentContentQuery, WorkOrderAttachmentContent>
{
    public async Task<Result<WorkOrderAttachmentContent>> Handle(
        GetWorkOrderServiceLineAttachmentContentQuery request,
        CancellationToken cancellationToken)
    {
        var workOrder = await WorkOrderLoader.ForMutation(db.WorkOrders.AsNoTracking())
            .FirstOrDefaultAsync(w => w.Id == request.WorkOrderId, cancellationToken);
        if (workOrder is null)
            return Error.NotFound("Work order not found.", "Operations.WorkOrder.NotFound");

        var scopeResult = await scope.ResolveAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;
        var access = scopeResult.Value.EnsureWorkOrderAccess(workOrder);
        if (access.IsFailure)
            return access.Error;

        var serviceLine = workOrder.ServiceLines.FirstOrDefault(line => line.Id == request.ServiceLineId);
        if (serviceLine is null)
            return Error.NotFound("Service line not found.", "Operations.WorkOrder.ServiceLineNotFound");

        var attachment = serviceLine.Attachments.FirstOrDefault(a => a.Id == request.AttachmentId);
        if (attachment is null)
            return Error.NotFound("Attachment not found.", "Operations.WorkOrder.AttachmentNotFound");

        await using var stream = await storage.OpenAsync(attachment.StorageReference, cancellationToken);
        if (stream is null)
            return Error.NotFound("Attachment file not found.", "Operations.WorkOrder.AttachmentFileNotFound");

        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);
        return new WorkOrderAttachmentContent(memory.ToArray(), attachment.ContentType, attachment.OriginalFileName);
    }
}

public sealed record GetWorkOrderSignatureContentQuery(Guid Id) : IQuery<WorkOrderAttachmentContent>;

public sealed class GetWorkOrderSignatureContentQueryHandler(
    IOperationsDbContext db,
    IOperationsScope scope,
    IFileStorage storage) : IQueryHandler<GetWorkOrderSignatureContentQuery, WorkOrderAttachmentContent>
{
    public async Task<Result<WorkOrderAttachmentContent>> Handle(GetWorkOrderSignatureContentQuery request, CancellationToken cancellationToken)
    {
        var workOrder = await WorkOrderLoader.ForMutation(db.WorkOrders.AsNoTracking())
            .FirstOrDefaultAsync(w => w.Id == request.Id, cancellationToken);
        if (workOrder is null)
            return Error.NotFound("Work order not found.", "Operations.WorkOrder.NotFound");

        var scopeResult = await scope.ResolveAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;
        var access = scopeResult.Value.EnsureWorkOrderAccess(workOrder);
        if (access.IsFailure)
            return access.Error;

        if (string.IsNullOrWhiteSpace(workOrder.CustomerSignatureReference))
            return Error.NotFound("Customer signature not found.", "Operations.WorkOrder.SignatureNotFound");

        await using var stream = await storage.OpenAsync(workOrder.CustomerSignatureReference, cancellationToken);
        if (stream is null)
            return Error.NotFound("Customer signature file not found.", "Operations.WorkOrder.SignatureFileNotFound");

        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);
        return new WorkOrderAttachmentContent(
            memory.ToArray(),
            workOrder.CustomerSignatureContentType ?? "image/png",
            workOrder.CustomerSignatureFileName ?? "customer-signature.png");
    }
}
