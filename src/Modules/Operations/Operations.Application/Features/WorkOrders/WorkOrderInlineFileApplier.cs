using System.Buffers.Text;
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
        var aggregateValidation = WorkOrderInlineFilePolicy.Validate(payload);
        if (aggregateValidation.IsFailure)
            return aggregateValidation.Error;

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

        var serviceLineCommands = (payload.ServiceLines ?? []).Where(line => !line.IsReturnToRamp).ToList();
        var taskCommands = (payload.Tasks ?? []).Where(task => !task.IsReturnToRamp).ToList();
        var returnToRampCommands = payload.ReturnToRamps ?? BuildLegacyReturnToRampCommands(payload);
        if (!serviceLineCommands.Any(line => line.Attachments is { Count: > 0 }) &&
            !taskCommands.Any(task => task.Attachments is { Count: > 0 }) &&
            !returnToRampCommands.Any(item =>
                (item.ServiceLines ?? []).Any(line => line.Attachments is { Count: > 0 }) ||
                (item.Tasks ?? []).Any(task => task.Attachments is { Count: > 0 })))
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

        if (returnToRampCommands.Any(item =>
                (item.ServiceLines ?? []).Any(line => line.Attachments is { Count: > 0 }) ||
                (item.Tasks ?? []).Any(task => task.Attachments is { Count: > 0 })))
        {
            var knownIds = returnToRampCommands.Where(item => item.Id.HasValue).Select(item => item.Id!.Value).ToHashSet();
            var newRecords = new Queue<WorkOrderReturnToRamp>(workOrder.ReturnToRamps
                .Where(item => !knownIds.Contains(item.Id))
                .OrderBy(item => item.CreatedAtUtc)
                .ThenBy(item => item.Id));

            foreach (var command in returnToRampCommands)
            {
                WorkOrderReturnToRamp? record;
                if (command.Id is { } existingId)
                    record = workOrder.ReturnToRamps.FirstOrDefault(item => item.Id == existingId);
                else
                    newRecords.TryDequeue(out record);
                if (record is null)
                    return await FailAsync(Error.Conflict("Could not match return-to-ramp attachments to an occurrence.", "Operations.ReturnToRamp.AttachmentMatchFailed"));

                var returnServiceCommands = command.ServiceLines ?? [];
                var serviceIds = ResolveReturnToRampServiceLineIds(record, returnServiceCommands);
                if (serviceIds.IsFailure)
                    return await FailAsync(serviceIds.Error);
                for (var i = 0; i < returnServiceCommands.Count; i++)
                {
                    foreach (var attachment in returnServiceCommands[i].Attachments ?? [])
                    {
                        var content = DecodeBase64(attachment.Base64Content, "attachment");
                        if (content.IsFailure)
                            return await FailAsync(content.Error);
                        var validation = WorkOrderAttachmentPolicy.Validate(attachment.Kind, content.Value, attachment.FileName, attachment.ContentType);
                        if (validation.IsFailure)
                            return await FailAsync(validation.Error);

                        await using var stream = new MemoryStream(content.Value);
                        var stored = await storage.SaveAsync("work-order-attachments", attachment.FileName, attachment.ContentType, stream, cancellationToken);
                        storedReferences.Add(stored.StorageKey);
                        var add = workOrder.AddReturnToRampServiceLineAttachment(
                            serviceIds.Value[i], attachment.Kind, stored.StorageKey, attachment.FileName, stored.ContentType, stored.SizeBytes, now);
                        if (add.IsFailure)
                            return await FailAsync(add.Error);
                    }
                }

                var returnTaskCommands = command.Tasks ?? [];
                var taskIds = ResolveReturnToRampTaskIds(record, returnTaskCommands);
                if (taskIds.IsFailure)
                    return await FailAsync(taskIds.Error);
                for (var i = 0; i < returnTaskCommands.Count; i++)
                {
                    foreach (var attachment in returnTaskCommands[i].Attachments ?? [])
                    {
                        var content = DecodeBase64(attachment.Base64Content, "attachment");
                        if (content.IsFailure)
                            return await FailAsync(content.Error);
                        var validation = WorkOrderAttachmentPolicy.Validate(attachment.Kind, content.Value, attachment.FileName, attachment.ContentType);
                        if (validation.IsFailure)
                            return await FailAsync(validation.Error);

                        await using var stream = new MemoryStream(content.Value);
                        var stored = await storage.SaveAsync("work-order-attachments", attachment.FileName, attachment.ContentType, stream, cancellationToken);
                        storedReferences.Add(stored.StorageKey);
                        var add = workOrder.AddReturnToRampTaskAttachment(
                            taskIds.Value[i], attachment.Kind, stored.StorageKey, attachment.FileName, stored.ContentType, stored.SizeBytes, now);
                        if (add.IsFailure)
                            return await FailAsync(add.Error);
                    }
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
            .Where(line => line.ReturnToRampId is null)
            .Where(line => !knownCommandServiceLineIds.Contains(line.Id))
            .Select(line => line.Id));
        var serviceLineIds = new List<Guid>(serviceLineCommands.Count);

        foreach (var serviceLine in serviceLineCommands)
        {
            if (serviceLine.Id is { } existingServiceLineId)
            {
                if (workOrder.ServiceLines.All(existing => existing.Id != existingServiceLineId || existing.ReturnToRampId is not null))
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
            .Where(task => task.ReturnToRampId is null)
            .Where(task => !knownCommandTaskIds.Contains(task.Id))
            .Select(task => task.Id));
        var taskIds = new List<Guid>(taskCommands.Count);

        foreach (var task in taskCommands)
        {
            if (task.Id is { } existingTaskId)
            {
                if (workOrder.Tasks.All(existing => existing.Id != existingTaskId || existing.ReturnToRampId is not null))
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

    private static Result<IReadOnlyList<Guid>> ResolveReturnToRampServiceLineIds(
        WorkOrderReturnToRamp record,
        IReadOnlyList<WorkOrderServiceLineCommand> commands)
    {
        var knownIds = commands.Where(item => item.Id.HasValue).Select(item => item.Id!.Value).ToHashSet();
        var newIds = new Queue<Guid>(record.ServiceLines.Where(item => !knownIds.Contains(item.Id)).Select(item => item.Id));
        var result = new List<Guid>(commands.Count);
        foreach (var command in commands)
        {
            if (command.Id is { } id)
            {
                if (record.ServiceLines.All(item => item.Id != id))
                    return Error.Conflict("A service line does not belong to this return-to-ramp record.", "Operations.ReturnToRamp.ServiceLineIdForeign");
                result.Add(id);
            }
            else if (newIds.TryDequeue(out var newId))
            {
                result.Add(newId);
            }
            else
            {
                return Error.Conflict("Could not match a return-to-ramp service attachment.", "Operations.ReturnToRamp.ServiceAttachmentMatchFailed");
            }
        }
        return result;
    }

    private static Result<IReadOnlyList<Guid>> ResolveReturnToRampTaskIds(
        WorkOrderReturnToRamp record,
        IReadOnlyList<WorkOrderTaskCommand> commands)
    {
        var knownIds = commands.Where(item => item.Id.HasValue).Select(item => item.Id!.Value).ToHashSet();
        var newIds = new Queue<Guid>(record.Tasks.Where(item => !knownIds.Contains(item.Id)).Select(item => item.Id));
        var result = new List<Guid>(commands.Count);
        foreach (var command in commands)
        {
            if (command.Id is { } id)
            {
                if (record.Tasks.All(item => item.Id != id))
                    return Error.Conflict("A task does not belong to this return-to-ramp record.", "Operations.ReturnToRamp.TaskIdForeign");
                result.Add(id);
            }
            else if (newIds.TryDequeue(out var newId))
            {
                result.Add(newId);
            }
            else
            {
                return Error.Conflict("Could not match a return-to-ramp task attachment.", "Operations.ReturnToRamp.TaskAttachmentMatchFailed");
            }
        }
        return result;
    }

    private static IReadOnlyList<WorkOrderReturnToRampCommand> BuildLegacyReturnToRampCommands(
        WorkOrderEditableCommandPayload payload)
    {
        var services = (payload.ServiceLines ?? []).Where(item => item.IsReturnToRamp).ToList();
        var tasks = (payload.Tasks ?? []).Where(item => item.IsReturnToRamp).ToList();
        if (services.Count + tasks.Count == 0)
            return [];

        var from = services.Select(item => item.FromUtc).Concat(tasks.Select(item => item.FromUtc)).Min();
        var to = services.Select(item => item.ToUtc).Concat(tasks.Select(item => item.ToUtc)).Max();
        return [new WorkOrderReturnToRampCommand(null, from, to, null, services, tasks)];
    }
}

public static class WorkOrderInlineFilePolicy
{
    /// <summary>
    /// Bounds the combined decoded bytes carried inline by one work-order mutation. This retains
    /// support for one maximum-size voice recording while preventing a JSON request from carrying
    /// an unbounded number of individually valid files.
    /// </summary>
    public const int MaxAggregateBytes = WorkOrderAttachmentPolicy.MaxVoiceBytes;

    /// <summary>
    /// Allows the Base64 representation of the raw aggregate plus a bounded JSON envelope for the
    /// work-order fields, resource rows, file metadata, and serializer overhead.
    /// </summary>
    public const int JsonEnvelopeAllowanceBytes = 4 * 1024 * 1024;
    public const long MaxJsonRequestBytes = (((long)MaxAggregateBytes + 2) / 3 * 4) + JsonEnvelopeAllowanceBytes;

    public static Result Validate(WorkOrderEditableCommandPayload payload)
    {
        long aggregateBytes = 0;
        foreach (var file in EnumerateProcessedFiles(payload))
        {
            if (string.IsNullOrWhiteSpace(file.Base64Content))
                return Error.Validation(
                    $"The {file.Label} file is empty.",
                    $"Operations.WorkOrder.{ToCodeLabel(file.Label)}Empty");

            if (!Base64.IsValid(file.Base64Content.AsSpan(), out var decodedLength))
                return Error.Validation(
                    $"The {file.Label} file content is invalid.",
                    $"Operations.WorkOrder.{ToCodeLabel(file.Label)}InvalidContent");

            aggregateBytes += decodedLength;
            if (aggregateBytes > MaxAggregateBytes)
            {
                return Error.Validation(
                    "The combined inline attachments and signature exceed the 25 MB request limit.",
                    "Operations.WorkOrder.InlineFilesTooLarge");
            }
        }

        return Result.Success();
    }

    private static IEnumerable<InlineFile> EnumerateProcessedFiles(WorkOrderEditableCommandPayload payload)
    {
        if (payload.CustomerSignature is { } signature)
            yield return new InlineFile(signature.Base64Content, "signature");

        // When the canonical occurrence collection is present, legacy top-level RTR rows are not
        // processed by the applier. With no collection, they are folded into one legacy occurrence.
        var includeLegacyReturnToRampRows = payload.ReturnToRamps is null;
        foreach (var line in payload.ServiceLines ?? [])
        {
            if (line.IsReturnToRamp && !includeLegacyReturnToRampRows)
                continue;
            foreach (var attachment in line.Attachments ?? [])
                yield return new InlineFile(attachment.Base64Content, "attachment");
        }

        foreach (var task in payload.Tasks ?? [])
        {
            if (task.IsReturnToRamp && !includeLegacyReturnToRampRows)
                continue;
            foreach (var attachment in task.Attachments ?? [])
                yield return new InlineFile(attachment.Base64Content, "attachment");
        }

        foreach (var occurrence in payload.ReturnToRamps ?? [])
        {
            foreach (var line in occurrence.ServiceLines ?? [])
            foreach (var attachment in line.Attachments ?? [])
                yield return new InlineFile(attachment.Base64Content, "attachment");

            foreach (var task in occurrence.Tasks ?? [])
            foreach (var attachment in task.Attachments ?? [])
                yield return new InlineFile(attachment.Base64Content, "attachment");
        }
    }

    private static string ToCodeLabel(string value) =>
        char.ToUpperInvariant(value[0]) + value[1..];

    private sealed record InlineFile(string? Base64Content, string Label);
}
