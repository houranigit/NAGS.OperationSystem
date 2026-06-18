using BuildingBlocks.Application.Abstractions.Queries;
using Operations.Contracts.WorkOrder;

namespace Operations.Application.Features.WorkOrder.Queries.GetWorkOrderById;

/// <summary>Loads the full detail (header + lines) of a work order. <see langword="null"/> when missing.</summary>
public sealed record GetWorkOrderByIdQuery(Guid Id) : IQuery<WorkOrderDetailDto?>;
