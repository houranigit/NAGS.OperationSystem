using BuildingBlocks.Domain.Results;
using Operations.Application.Common;
using Operations.Domain.Enumerations;
using Operations.Domain.WorkOrders;

namespace Operations.Application.Features.WorkOrders;

// Transport-level request shapes for work-order authoring, mapped into validated domain inputs.

public sealed record ServiceLineRequest(
    Guid ServiceId,
    ServiceLineOrigin Origin,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    string? Description,
    bool ReturnToRamp,
    IReadOnlyList<Guid> EmployeeIds);

public sealed record ResourceUsageRequest(Guid Id, decimal Quantity);

public sealed record TaskAttachmentRequest(
    TaskAttachmentKind Kind,
    string ContentType,
    string FileName,
    long SizeBytes,
    string StorageReference,
    DateTimeOffset CapturedAtUtc);

public sealed record TaskRequest(
    TaskType TaskType,
    string? Description,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    bool ReturnToRamp,
    IReadOnlyList<Guid> EmployeeIds,
    IReadOnlyList<ResourceUsageRequest> Tools,
    IReadOnlyList<ResourceUsageRequest> Materials,
    IReadOnlyList<ResourceUsageRequest> GeneralSupports,
    IReadOnlyList<TaskAttachmentRequest> Attachments);

/// <summary>Resolves work-order authoring requests into validated domain inputs (snapshots + value objects).</summary>
public sealed class WorkOrderInputBuilder(MasterDataResolver resolver)
{
    public async Task<Result<IReadOnlyList<ServiceLineInput>>> BuildServiceLinesAsync(IReadOnlyList<ServiceLineRequest> requests, CancellationToken ct)
    {
        var result = new List<ServiceLineInput>();
        foreach (var request in requests)
        {
            var service = await resolver.ServiceAsync(request.ServiceId, ct);
            if (service.IsFailure)
                return service.Error;

            var employees = await resolver.StaffMembersAsync(request.EmployeeIds, ct);
            if (employees.IsFailure)
                return employees.Error;

            result.Add(new ServiceLineInput(service.Value, request.Origin, request.FromUtc, request.ToUtc,
                request.Description, request.ReturnToRamp, employees.Value));
        }

        return result;
    }

    public async Task<Result<IReadOnlyList<TaskInput>>> BuildTasksAsync(IReadOnlyList<TaskRequest> requests, CancellationToken ct)
    {
        var result = new List<TaskInput>();
        foreach (var request in requests)
        {
            var employees = await resolver.StaffMembersAsync(request.EmployeeIds, ct);
            if (employees.IsFailure)
                return employees.Error;

            var tools = new List<ToolUsageInput>();
            foreach (var tool in request.Tools)
            {
                var snapshot = await resolver.ToolAsync(tool.Id, ct);
                if (snapshot.IsFailure)
                    return snapshot.Error;
                tools.Add(new ToolUsageInput(snapshot.Value, tool.Quantity));
            }

            var materials = new List<MaterialUsageInput>();
            foreach (var material in request.Materials)
            {
                var snapshot = await resolver.MaterialAsync(material.Id, ct);
                if (snapshot.IsFailure)
                    return snapshot.Error;
                materials.Add(new MaterialUsageInput(snapshot.Value, material.Quantity));
            }

            var generalSupports = new List<GeneralSupportUsageInput>();
            foreach (var gs in request.GeneralSupports)
            {
                var snapshot = await resolver.GeneralSupportAsync(gs.Id, ct);
                if (snapshot.IsFailure)
                    return snapshot.Error;
                generalSupports.Add(new GeneralSupportUsageInput(snapshot.Value, gs.Quantity));
            }

            var attachments = request.Attachments
                .Select(a => new AttachmentInput(a.Kind, a.ContentType, a.FileName, a.SizeBytes, a.StorageReference, a.CapturedAtUtc))
                .ToList();

            result.Add(new TaskInput(request.TaskType, request.Description, request.FromUtc, request.ToUtc, request.ReturnToRamp,
                employees.Value, tools, materials, generalSupports, attachments));
        }

        return result;
    }
}
