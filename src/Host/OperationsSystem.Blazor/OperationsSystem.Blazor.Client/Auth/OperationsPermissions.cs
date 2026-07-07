namespace OperationsSystem.Blazor.Client.Auth;

/// <summary>
/// Client-side mirror of the Operations module permission names (the backend stays authoritative).
/// Used only to gate UI; the API enforces these on every request.
/// </summary>
public static class OperationsPermissions
{
    public const string FlightsView = "operations.flights.view";

    /// <summary>Station-wide flight visibility for station staff without assignment (station dispatchers).</summary>
    public const string FlightsViewStation = "operations.flights.view-station";

    public const string FlightsSchedule = "operations.flights.schedule";
    public const string FlightsUpdate = "operations.flights.update";
    public const string FlightsAssign = "operations.flights.assign";
    public const string FlightsInvite = "operations.flights.invite";
    public const string FlightsCancel = "operations.flights.cancel";
    public const string FlightsReopen = "operations.flights.reopen";
    public const string FlightsMerge = "operations.flights.merge";

    public const string WorkOrdersView = "operations.work-orders.view";
    public const string WorkOrdersAuthor = "operations.work-orders.author";
    public const string WorkOrdersSubmit = "operations.work-orders.submit";
    public const string WorkOrdersApprove = "operations.work-orders.approve";
    public const string WorkOrdersReject = "operations.work-orders.reject";
    public const string WorkOrdersReturn = "operations.work-orders.return";
    public const string WorkOrdersMerge = "operations.work-orders.merge";

    public const string DashboardView = "operations.dashboard.view";
}
