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
            .Include(f => f.PlannedServices)
            .Include(f => f.AssignedEmployees)
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

        return await new WorkOrderPrintSourceBuilder(storage, masterData)
            .BuildAsync(workOrder, flight, cancellationToken);
    }
}
