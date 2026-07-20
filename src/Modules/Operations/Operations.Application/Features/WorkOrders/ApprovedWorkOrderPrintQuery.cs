using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Domain.Results;
using MasterData.Contracts.Readers;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Application.Authorization;
using Operations.Application.Contracts;
using Operations.Domain.Enumerations;
using Operations.Domain.WorkOrders;

namespace Operations.Application.Features.WorkOrders;

public sealed record GetApprovedWorkOrderPrintQuery(Guid FlightId) : IQuery<ApprovedWorkOrderPrintDto>;

public sealed class GetApprovedWorkOrderPrintQueryHandler(
    IOperationsDbContext db,
    IOperationsScope scope,
    IUserContext user,
    IFileStorage storage,
    IMasterDataReader masterData) : IQueryHandler<GetApprovedWorkOrderPrintQuery, ApprovedWorkOrderPrintDto>
{
    public async Task<Result<ApprovedWorkOrderPrintDto>> Handle(
        GetApprovedWorkOrderPrintQuery request,
        CancellationToken cancellationToken)
    {
        var scopeResult = await scope.ResolveAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;

        var visibleWorkOrders = WorkOrderQueryVisibility.ApplyVisibility(
            db.WorkOrders.AsNoTracking(),
            scopeResult.Value,
            user);
        var workOrder = await WorkOrderLoader.ForMutation(visibleWorkOrders)
            .SingleOrDefaultAsync(w =>
                w.FlightId == request.FlightId &&
                w.Type == WorkOrderType.Completion &&
                w.Status == WorkOrderStatus.Approved,
                cancellationToken);
        if (workOrder is null)
        {
            return Error.NotFound(
                "No accessible approved completion work order was found for this flight.",
                "Operations.WorkOrder.ApprovedCompletionNotFound");
        }

        var flight = await db.Flights.AsNoTracking()
            .SingleOrDefaultAsync(f =>
                f.Id == request.FlightId &&
                f.Status == FlightStatus.Completed,
                cancellationToken);
        if (flight is null)
        {
            return Error.NotFound(
                "No accessible approved completion work order was found for this flight.",
                "Operations.WorkOrder.ApprovedCompletionNotFound");
        }

        var signatureContent = await LoadOptionalSignatureAsync(workOrder.CustomerSignatureReference, cancellationToken);
        var staff = await LoadStaffAsync(workOrder, cancellationToken);
        var detail = NormalizeForPrint(WorkOrderDtoMapper.Detail(workOrder));

        return new ApprovedWorkOrderPrintDto(
            detail,
            workOrder.AircraftType?.Manufacturer,
            flight.ContractNumber,
            staff,
            signatureContent,
            signatureContent is null
                ? null
                : workOrder.CustomerSignatureContentType ?? "image/png");
    }

    private async Task<IReadOnlyList<WorkOrderPrintStaffDto>> LoadStaffAsync(
        WorkOrder workOrder,
        CancellationToken cancellationToken)
    {
        var snapshots = workOrder.ServiceLines
            .SelectMany(line => line.PerformedBy.Select(performer => performer.StaffMember))
            .Concat(workOrder.Tasks.SelectMany(task => task.Employees.Select(employee => employee.Employee)))
            .GroupBy(employee => employee.StaffMemberId)
            .Select(group => group.First())
            .OrderBy(employee => employee.FullName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(employee => employee.EmployeeId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(employee => employee.StaffMemberId)
            .ToList();
        if (snapshots.Count == 0)
            return [];

        var currentStaff = await masterData.GetStaffMembersAsync(
            snapshots.Select(employee => employee.StaffMemberId).ToList(),
            cancellationToken);
        var manpowerTypeIdByStaff = currentStaff
            .Where(employee => employee.StationId == workOrder.Station.StationId)
            .GroupBy(employee => employee.Id)
            .ToDictionary(group => group.Key, group => group.First().ManpowerTypeId);

        var manpowerTypeNames = new Dictionary<Guid, string>();
        foreach (var manpowerTypeId in manpowerTypeIdByStaff.Values.Distinct().OrderBy(id => id))
        {
            var manpowerType = await masterData.GetManpowerTypeAsync(manpowerTypeId, cancellationToken);
            if (manpowerType is not null && !string.IsNullOrWhiteSpace(manpowerType.Name))
                manpowerTypeNames[manpowerTypeId] = manpowerType.Name.Trim();
        }

        return snapshots.Select(employee =>
        {
            string? manpowerTypeName = null;
            if (manpowerTypeIdByStaff.TryGetValue(employee.StaffMemberId, out var manpowerTypeId) &&
                manpowerTypeNames.TryGetValue(manpowerTypeId, out var resolvedName))
            {
                manpowerTypeName = resolvedName;
            }

            return new WorkOrderPrintStaffDto(
                employee.StaffMemberId,
                manpowerTypeName);
        }).ToList();
    }

    private static WorkOrderDetailDto NormalizeForPrint(WorkOrderDetailDto detail) =>
        detail with
        {
            ServiceLines = detail.ServiceLines
                .OrderBy(line => line.FromUtc)
                .ThenBy(line => line.ToUtc)
                .ThenBy(line => line.ServiceName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(line => line.Id)
                .Select(line => line with
                {
                    PerformedBy = line.PerformedBy
                        .OrderBy(performer => performer.FullName, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(performer => performer.StaffMemberId)
                        .ToList()
                })
                .ToList(),
            Tasks = detail.Tasks
                .OrderBy(task => task.FromUtc)
                .ThenBy(task => task.ToUtc)
                .ThenBy(task => task.TaskType, StringComparer.Ordinal)
                .ThenBy(task => task.Id)
                .Select(task => task with
                {
                    Employees = task.Employees
                        .OrderBy(employee => employee.FullName, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(employee => employee.StaffMemberId)
                        .ToList(),
                    Tools = task.Tools
                        .OrderBy(tool => tool.Name, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(tool => tool.ToolId)
                        .ToList(),
                    Materials = task.Materials
                        .OrderBy(material => material.Name, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(material => material.MaterialId)
                        .ToList(),
                    GeneralSupports = task.GeneralSupports
                        .OrderBy(support => support.Name, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(support => support.GeneralSupportId)
                        .ToList(),
                    Attachments = task.Attachments
                        .OrderBy(attachment => attachment.OriginalFileName, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(attachment => attachment.Id)
                        .ToList()
                })
                .ToList()
        };

    private async Task<byte[]?> LoadOptionalSignatureAsync(
        string? storageReference,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(storageReference))
            return null;

        Stream? stream;
        try
        {
            stream = await storage.OpenAsync(storageReference, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }

        if (stream is null)
            return null;

        await using (stream)
        {
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory, cancellationToken);
            return memory.ToArray();
        }
    }
}
