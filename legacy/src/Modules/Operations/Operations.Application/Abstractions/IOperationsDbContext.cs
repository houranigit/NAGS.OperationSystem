using BuildingBlocks.Application.Abstractions;
using FlightRoot = Operations.Domain.Aggregates.Flight.Flight;
using WorkOrderRoot = Operations.Domain.Aggregates.WorkOrder.WorkOrder;

namespace Operations.Application.Abstractions;

/// <summary>Abstraction implemented by Operations infrastructure persistence.</summary>
public interface IOperationsDbContext : IUnitOfWork
{
    IQueryable<FlightRoot> Flights { get; }
    IQueryable<WorkOrderRoot> WorkOrders { get; }
}
