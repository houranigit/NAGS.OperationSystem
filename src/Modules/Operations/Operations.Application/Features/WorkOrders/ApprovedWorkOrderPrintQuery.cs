using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Domain.Results;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Application.Authorization;
using Operations.Application.Contracts;
using Operations.Domain.Enumerations;

namespace Operations.Application.Features.WorkOrders;

public sealed record GetApprovedWorkOrderPrintQuery(Guid FlightId) : IQuery<ApprovedWorkOrderPrintDto>;

public sealed class GetApprovedWorkOrderPrintQueryHandler(
    IOperationsDbContext db,
    IOperationsScope scope,
    IUserContext user,
    IFileStorage storage) : IQueryHandler<GetApprovedWorkOrderPrintQuery, ApprovedWorkOrderPrintDto>
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
            .Include(f => f.PlannedServices)
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

        var isPerLanding = flight.IsPerLanding;
        var isOnCall = isPerLanding && await db.WorkOrders.AsNoTracking()
            .QualifyingForOnCall()
            .AnyAsync(w => w.FlightId == flight.Id, cancellationToken);
        var signatureContent = await LoadOptionalSignatureAsync(workOrder.CustomerSignatureReference, cancellationToken);
        var detail = NormalizeForPrint(WorkOrderDtoMapper.Detail(workOrder));

        return new ApprovedWorkOrderPrintDto(
            detail,
            workOrder.AircraftType?.Manufacturer,
            flight.ContractNumber,
            flight.PlannedServices
                .Select(service => service.Service.Name)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(name => name, StringComparer.Ordinal)
                .ToList(),
            isPerLanding,
            isOnCall,
            signatureContent,
            signatureContent is null
                ? null
                : workOrder.CustomerSignatureContentType ?? "image/png");
    }

    private static WorkOrderDetailDto NormalizeForPrint(WorkOrderDetailDto detail) =>
        detail with
        {
            ServiceLines = detail.ServiceLines
                .OrderBy(line => line.FromUtc)
                .ThenBy(line => line.ToUtc)
                .ThenBy(line => line.ServiceName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(line => line.Id)
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
