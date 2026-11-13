using BuildingBlocks.Domain.Results;
using MasterData.Contracts.Readers;
using Operations.Application.Common;
using Operations.Application.Contracts;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;

namespace Operations.Application.Features.Flights;

internal sealed record ResolvedFlightTaskInput(AtaChapterSnapshot? AtaChapter, TimeWindow Window,
    IReadOnlyList<FlightTaskPerformerInput> Performers, IReadOnlyList<FlightTaskToolInput> Tools,
    IReadOnlyList<FlightTaskMaterialInput> Materials, IReadOnlyList<FlightTaskGeneralSupportInput> GeneralSupports);

internal static class FlightTaskInputs
{
    internal static Result Validate(SaveFlightTaskRequest input, FlightTask? existing)
    {
        if (input.TaskType is not (0 or 1) || input.StartUtc == default || input.AtaChapterId == Guid.Empty)
            return Error.Validation("A valid task type and start time are required.", "Operations.FlightTask.DetailsRequired");
        if (input.Performers is not { Count: > 0 })
            return Error.Validation("Every task requires at least one performer.", "Operations.WorkOrder.TaskPerformerRequired");
        if (input.Performers.Any(p => p is null || p.Id == Guid.Empty || p.StaffMemberId == Guid.Empty || p.StartUtc == default) ||
            input.Performers.Select(p => p.Id).Distinct().Count() != input.Performers.Count ||
            input.Performers.Select(p => p.StaffMemberId).Distinct().Count() != input.Performers.Count)
            return Error.Validation("Provide one identified work period for every selected employee.", "Operations.FlightTask.PerformersInvalid");
        foreach (var list in new[] { input.Tools, input.Materials, input.GeneralSupports })
            if (list is null || list.Any(item => item is null || item.Id == Guid.Empty || item.ResourceId == Guid.Empty) ||
                list.Select(item => item.Id).Distinct().Count() != list.Count || list.Select(item => item.ResourceId).Distinct().Count() != list.Count)
                return Error.Validation("Resource selections and record identities must be present and unique within each list.", "Operations.FlightTask.ResourcesInvalid");
        if (input.Attachments is null) return Result.Success();
        if (input.Attachments.Count > FlightTask.MaxAttachments)
            return Error.Validation("A task can have at most 10 attachments.", "Operations.WorkOrder.AttachmentLimitExceeded");
        if (input.Attachments.Any(a => a is null || a.Id == Guid.Empty) || input.Attachments.Select(a => a.Id).Distinct().Count() != input.Attachments.Count)
            return Error.Validation("Attachment IDs must be present and unique.", "Operations.FlightTask.AttachmentIdInvalid");
        long aggregateBytes = 0;
        foreach (var attachment in input.Attachments)
        {
            var retained = existing?.Attachments.SingleOrDefault(a => a.Id == attachment.Id);
            var details = AttachmentDetails.Create(attachment.FileName, "validation", attachment.ContentType,
                attachment.Content?.LongLength ?? retained?.Details.SizeBytes ?? 0, (AttachmentKind)attachment.Kind);
            if (details.IsFailure) return details.Error;
            if (retained is not null && (details.Value.FileName != retained.Details.FileName || !details.Value.ContentType.Equals(retained.Details.ContentType, StringComparison.OrdinalIgnoreCase) || details.Value.Kind != retained.Details.Kind))
                return Error.Validation("Use a new attachment ID to replace a file.", "Operations.FlightTask.AttachmentImmutable");
            if (attachment.Content is null)
            {
                if (retained is null) return Error.Validation("A new attachment requires file content.", "Operations.FlightTask.AttachmentContentRequired");
            }
            else
            {
                var file = FlightServiceAttachmentPolicy.Validate((AttachmentKind)attachment.Kind, attachment.Content, attachment.FileName, attachment.ContentType);
                if (file.IsFailure) return file.Error;
                aggregateBytes += attachment.Content.LongLength;
                if (aggregateBytes > FlightServiceAttachmentPolicy.MaxAggregateBytes)
                    return Error.Validation("The combined inline attachments exceed the 25 MB request limit.", "Operations.FlightTask.InlineFilesTooLarge");
            }
        }
        return Result.Success();
    }

    internal static async Task<Result<ResolvedFlightTaskInput>> ResolveAsync(SaveFlightTaskRequest input, FlightTask? existing,
        Guid stationId, IMasterDataReader masterData, MasterDataResolver resolver, CancellationToken ct)
    {
        AtaChapterSnapshot? chapter = null;
        if (input.AtaChapterId is { } chapterId)
        {
            if (existing?.AtaChapter?.AtaChapterId == chapterId) chapter = existing.AtaChapter;
            else
            {
                var reference = await masterData.GetAtaChapterAsync(chapterId, ct);
                if (reference is null || !reference.IsActive || !reference.CategoryIsActive)
                    return Error.Validation("Choose an active ATA chapter.", "Operations.Ref.AtaChapterInactive");
                chapter = new AtaChapterSnapshot(reference.Id, reference.Code, reference.Title);
            }
        }
        var window = TimeWindow.Create(input.StartUtc, input.EndUtc);
        if (window.IsFailure) return window.Error;
        var staffIds = input.Performers.Where(p => existing?.Performers.Any(old => old.Id == p.Id && old.Employee.StaffMemberId == p.StaffMemberId) != true)
            .Select(p => p.StaffMemberId).ToArray();
        var staff = await resolver.StaffMembersForStationAsync(staffIds, stationId, ct);
        if (staff.IsFailure) return staff.Error;
        var performers = new List<FlightTaskPerformerInput>();
        foreach (var item in input.Performers)
        {
            var period = TimeWindow.Create(item.StartUtc, item.EndUtc);
            if (period.IsFailure) return period.Error;
            var retained = existing?.Performers.SingleOrDefault(old => old.Id == item.Id && old.Employee.StaffMemberId == item.StaffMemberId);
            performers.Add(new(retained?.Employee ?? staff.Value.Single(s => s.StaffMemberId == item.StaffMemberId), period.Value, item.Notes, item.Id));
        }
        var tools = new List<FlightTaskToolInput>();
        foreach (var item in input.Tools)
        {
            var snapshot = existing?.Tools.SingleOrDefault(old => old.Id == item.Id && old.Tool.ToolId == item.ResourceId)?.Tool;
            if (snapshot is null)
            {
                var reference = await masterData.GetToolAsync(item.ResourceId, ct);
                if (reference is null || !reference.IsActive) return Error.Validation("Choose an active tool.", "Operations.Ref.ToolInactive");
                snapshot = new(reference.Id, reference.Name, reference.CalculationType);
            }
            var usage = ResourceUsage.Create(snapshot.CalculationType, item.Quantity, item.StartUtc, item.EndUtc);
            if (usage.IsFailure) return usage.Error;
            tools.Add(new(snapshot, usage.Value, item.Notes, item.Id));
        }
        var materials = new List<FlightTaskMaterialInput>();
        foreach (var item in input.Materials)
        {
            var snapshot = existing?.Materials.SingleOrDefault(old => old.Id == item.Id && old.Material.MaterialId == item.ResourceId)?.Material;
            if (snapshot is null)
            {
                var reference = await masterData.GetMaterialAsync(item.ResourceId, ct);
                if (reference is null || !reference.IsActive) return Error.Validation("Choose an active material.", "Operations.Ref.MaterialInactive");
                snapshot = new(reference.Id, reference.Name, reference.CalculationType);
            }
            var usage = ResourceUsage.Create(snapshot.CalculationType, item.Quantity, item.StartUtc, item.EndUtc);
            if (usage.IsFailure) return usage.Error;
            materials.Add(new(snapshot, usage.Value, item.Notes, item.Id));
        }
        var supports = new List<FlightTaskGeneralSupportInput>();
        foreach (var item in input.GeneralSupports)
        {
            var snapshot = existing?.GeneralSupports.SingleOrDefault(old => old.Id == item.Id && old.GeneralSupport.GeneralSupportId == item.ResourceId)?.GeneralSupport;
            if (snapshot is null)
            {
                var reference = await masterData.GetGeneralSupportAsync(item.ResourceId, ct);
                if (reference is null || !reference.IsActive) return Error.Validation("Choose an active general support.", "Operations.Ref.GeneralSupportInactive");
                snapshot = new(reference.Id, reference.Name, reference.CalculationType);
            }
            var usage = ResourceUsage.Create(snapshot.CalculationType, item.Quantity, item.StartUtc, item.EndUtc);
            if (usage.IsFailure) return usage.Error;
            supports.Add(new(snapshot, usage.Value, item.Notes, item.Id));
        }
        return new ResolvedFlightTaskInput(chapter, window.Value, performers, tools, materials, supports);
    }

    internal static bool CoreMatches(FlightTask task, SaveFlightTaskRequest input) =>
        (int)task.TaskType == input.TaskType && task.AtaChapter?.AtaChapterId == input.AtaChapterId && task.Window.FromUtc == input.StartUtc &&
        Normalize(task.CustomerReferenceNumber)?.ToUpperInvariant() == Normalize(input.CustomerReferenceNumber)?.ToUpperInvariant() && task.Notes == Normalize(input.Notes) &&
        task.Performers.Count == input.Performers.Count && task.Performers.All(old => input.Performers.Any(item => item.Id == old.Id &&
            item.StaffMemberId == old.Employee.StaffMemberId && item.StartUtc == old.Window.FromUtc && item.EndUtc == old.Window.ToUtc && Normalize(item.Notes) == old.Notes)) &&
        task.Tools.Count == input.Tools.Count && task.Tools.All(old => input.Tools.Any(item => ResourceMatches(old.Id, old.Tool.ToolId, old.Usage, old.Notes, item))) &&
        task.Materials.Count == input.Materials.Count && task.Materials.All(old => input.Materials.Any(item => ResourceMatches(old.Id, old.Material.MaterialId, old.Usage, old.Notes, item))) &&
        task.GeneralSupports.Count == input.GeneralSupports.Count && task.GeneralSupports.All(old => input.GeneralSupports.Any(item => ResourceMatches(old.Id, old.GeneralSupport.GeneralSupportId, old.Usage, old.Notes, item)));

    private static bool ResourceMatches(Guid id, Guid resourceId, ResourceUsage usage, string? notes, SaveFlightTaskResourceRequest item) =>
        id == item.Id && resourceId == item.ResourceId && usage.Quantity == item.Quantity && usage.FromUtc == item.StartUtc && usage.ToUtc == item.EndUtc && notes == Normalize(item.Notes);
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
