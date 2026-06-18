namespace Operations.Contracts.Dashboard;

/// <summary>
/// Aggregated operational metrics for the home dashboard. All counts cover the
/// trailing window (default 30 days) requested by the caller, except
/// <see cref="FlightsToday"/> which is always today's flights regardless of window
/// and the work-order counts which reflect work orders created in the window.
/// </summary>
public sealed record OperationsDashboardDto(
    int LookBackDays,
    int TotalFlights,
    int FlightsToday,
    int FlightsScheduled,
    int FlightsInProgress,
    int FlightsCompleted,
    int FlightsCanceled,
    decimal CompletionRatePct,
    int WorkOrdersUnderReview,
    int WorkOrdersApproved,
    int WorkOrdersRejected,
    int WorkOrdersDeleting,
    IReadOnlyList<FlightsByStationRow> TopStations,
    IReadOnlyList<FlightsByCustomerRow> TopCustomers,
    IReadOnlyList<FlightsByDayRow> FlightsByDay
);

public sealed record FlightsByStationRow(Guid StationId, string IataCode, string Name, int Count);

public sealed record FlightsByCustomerRow(Guid CustomerId, string IataCode, string Name, int Count);

public sealed record FlightsByDayRow(DateOnly Date, int Count);
