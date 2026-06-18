using OpsFlight = Operations.Domain.Aggregates.Flight;

namespace Operations.Domain.Aggregates.WorkOrder;

public interface IWorkOrderRepository
{
    Task<WorkOrder?> GetByIdAsync(WorkOrderId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads many work orders with the same includes as <see cref="GetByIdAsync"/> for mobile projections.
    /// </summary>
    Task<IReadOnlyList<WorkOrder>> GetByIdsAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkOrder>> GetByFlightIdAsync(OpsFlight.FlightId flightId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns work orders whose status is <c>Deleting</c> and whose <c>MarkedForDeletionAt</c>
    /// is at or before <paramref name="threshold"/> — i.e. those past the configured grace
    /// period and ready for the deletion job to remove.
    /// </summary>
    Task<IReadOnlyList<WorkOrder>> GetDueForDeletionAsync(
        DateTimeOffset threshold,
        int batchSize,
        CancellationToken cancellationToken = default);

    void Add(WorkOrder workOrder);
    void Update(WorkOrder workOrder);

    /// <summary>Hard-deletes a work order (use only when status is not <c>Approved</c>).</summary>
    void Remove(WorkOrder workOrder);
}
