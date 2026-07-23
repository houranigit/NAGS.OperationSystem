using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Domain.Results;
using Operations.Domain.WorkOrders;

namespace Operations.Application.Features.WorkOrders;

internal static class WorkOrderInlineFileApplier
{
    public static async Task<Result<IReadOnlyList<string>>> ApplyAsync(
        WorkOrder workOrder,
        WorkOrderEditableCommandPayload payload,
        IFileStorage storage,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var storedReferences = new List<string>();

        async Task<Result<IReadOnlyList<string>>> FailAsync(Error error)
        {
            await WorkOrderAttachmentStorage.DeleteAsync(storage, storedReferences, cancellationToken);
            return error;
        }

        if (payload.CustomerSignature is { } signature)
        {
            var signatureContent = DecodeBase64(signature.Base64Content, "signature");
            if (signatureContent.IsFailure)
                return await FailAsync(signatureContent.Error);

            var validation = WorkOrderSignaturePolicy.Validate(signatureContent.Value, signature.FileName, signature.ContentType);
            if (validation.IsFailure)
                return await FailAsync(validation.Error);

            await using var signatureStream = new MemoryStream(signatureContent.Value);
            var stored = await storage.SaveAsync("work-order-signatures", signature.FileName, signature.ContentType, signatureStream, cancellationToken);
            storedReferences.Add(stored.StorageKey);

            var set = workOrder.SetCustomerSignature(stored.StorageKey, signature.FileName, stored.ContentType, stored.SizeBytes, now);
            if (set.IsFailure)
                return await FailAsync(set.Error);
        }

        var serviceLineCommands = payload.ServiceLines ?? [];
        var taskCommands = payload.Tasks ?? [];
        if (!serviceLineCommands.Any(line => line.Attachments is { Count: > 0 }) &&
            !taskCommands.Any(task => task.Attachments is { Count: > 0 }))
            return storedReferences;

        if (serviceLineCommands.Any(line => line.Attachments is { Count: > 0 }))
        {
            var serviceLineIds = ResolveServiceLineIds(workOrder, serviceLineCommands);
            if (serviceLineIds.IsFailure)
                return await FailAsync(serviceLineIds.Error);

            for (var i = 0; i < serviceLineCommands.Count; i++)
            {
                var serviceLine = serviceLineCommands[i];
                foreach (var attachment in serviceLine.Attachments ?? [])
                {
                    var attachmentContent = DecodeBase64(attachment.Base64Content, "attachment");
                    if (attachmentContent.IsFailure)
                        return await FailAsync(attachmentContent.Error);

                    var validation = WorkOrderAttachmentPolicy.Validate(
                        attachment.Kind,
                        attachmentContent.Value,
                        attachment.FileName,
                        attachment.ContentType);
                    if (validation.IsFailure)
                        return await FailAsync(validation.Error);

                    await using var attachmentStream = new MemoryStream(attachmentContent.Value);
                    var stored = await storage.SaveAsync("work-order-attachments", attachment.FileName, attachment.ContentType, attachmentStream, cancellationToken);
                    storedReferences.Add(stored.StorageKey);

                    var add = workOrder.AddServiceLineAttachment(
                        serviceLineIds.Value[i],
                        attachment.Kind,
                        stored.StorageKey,
                        attachment.FileName,
                        stored.ContentType,
                        stored.SizeBytes,
                        now);
                    if (add.IsFailure)
                        return await FailAsync(add.Error);
                }
            }
        }

        if (taskCommands.Any(task => task.Attachments is { Count: > 0 }))
        {
            var taskIds = ResolveTaskIds(workOrder, taskCommands);
            if (taskIds.IsFailure)
                return await FailAsync(taskIds.Error);

            for (var i = 0; i < taskCommands.Count; i++)
            {
                var task = taskCommands[i];
                foreach (var attachment in task.Attachments ?? [])
                {
                    var attachmentContent = DecodeBase64(attachment.Base64Content, "attachment");
                    if (attachmentContent.IsFailure)
                        return await FailAsync(attachmentContent.Error);

                    var validation = WorkOrderAttachmentPolicy.Validate(
                        attachment.Kind,
                        attachmentContent.Value,
                        attachment.FileName,
                        attachment.ContentType);
                    if (validation.IsFailure)
                        return await FailAsync(validation.Error);

                    await using var attachmentStream = new MemoryStream(attachmentContent.Value);
                    var stored = await storage.SaveAsync("work-order-attachments", attachment.FileName, attachment.ContentType, attachmentStream, cancellationToken);
                    storedReferences.Add(stored.StorageKey);

                    var add = workOrder.AddTaskAttachment(
                        taskIds.Value[i],
                        attachment.Kind,
                        stored.StorageKey,
                        attachment.FileName,
                        stored.ContentType,
                        stored.SizeBytes,
                        now);
                    if (add.IsFailure)
                        return await FailAsync(add.Error);
                }
            }
        }

        return storedReferences;
    }

    private static Result<byte[]> DecodeBase64(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Error.Validation($"The {label} file is empty.", $"Operations.WorkOrder.{ToCodeLabel(label)}Empty");

        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            return Error.Validation($"The {label} file content is invalid.", $"Operations.WorkOrder.{ToCodeLabel(label)}InvalidContent");
        }
    }

    private static string ToCodeLabel(string value) =>
        string.IsNullOrWhiteSpace(value) ? value : char.ToUpperInvariant(value[0]) + value[1..];

    private static Result<IReadOnlyList<Guid>> ResolveServiceLineIds(
        WorkOrder workOrder,
        IReadOnlyList<WorkOrderServiceLineCommand> serviceLineCommands)
    {
        var knownCommandServiceLineIds = serviceLineCommands
            .Where(line => line.Id.HasValue)
            .Select(line => line.Id!.Value)
            .ToHashSet();
        var newWorkOrderServiceLineIds = new Queue<Guid>(workOrder.ServiceLines
            .Where(line => !knownCommandServiceLineIds.Contains(line.Id))
            .Select(line => line.Id));
        var serviceLineIds = new List<Guid>(serviceLineCommands.Count);

        foreach (var serviceLine in serviceLineCommands)
        {
            if (serviceLine.Id is { } existingServiceLineId)
            {
                if (workOrder.ServiceLines.All(existing => existing.Id != existingServiceLineId))
                    return Error.Conflict("One or more service line ids do not belong to this work order.", "Operations.WorkOrder.ServiceLineIdForeign");

                serviceLineIds.Add(existingServiceLineId);
                continue;
            }

            if (!newWorkOrderServiceLineIds.TryDequeue(out var newServiceLineId))
                return Error.Conflict("Could not match a new service attachment to its service line.", "Operations.WorkOrder.ServiceLineAttachmentMatchFailed");

            serviceLineIds.Add(newServiceLineId);
        }

        return serviceLineIds;
    }

    private static Result<IReadOnlyList<Guid>> ResolveTaskIds(
        WorkOrder workOrder,
        IReadOnlyList<WorkOrderTaskCommand> taskCommands)
    {
        var knownCommandTaskIds = taskCommands
            .Where(task => task.Id.HasValue)
            .Select(task => task.Id!.Value)
            .ToHashSet();
        var newWorkOrderTaskIds = new Queue<Guid>(workOrder.Tasks
            .Where(task => !knownCommandTaskIds.Contains(task.Id))
            .Select(task => task.Id));
        var taskIds = new List<Guid>(taskCommands.Count);

        foreach (var task in taskCommands)
        {
            if (task.Id is { } existingTaskId)
            {
                if (workOrder.Tasks.All(existing => existing.Id != existingTaskId))
                    return Error.Conflict("One or more task ids do not belong to this work order.", "Operations.WorkOrder.TaskIdForeign");

                taskIds.Add(existingTaskId);
                continue;
            }

            if (!newWorkOrderTaskIds.TryDequeue(out var newTaskId))
                return Error.Conflict("Could not match a new task attachment to its task.", "Operations.WorkOrder.TaskAttachmentMatchFailed");

            taskIds.Add(newTaskId);
        }

        return taskIds;
    }
}
