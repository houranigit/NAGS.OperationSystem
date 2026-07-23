namespace OperationsSystem.Blazor.Client.Auth;

/// <summary>
/// Client-side mirror of the Operations module permission names (the backend stays authoritative).
/// Used only to gate UI; the API enforces these on every request.
/// </summary>
public static class OperationsPermissions
{
    public const string DashboardView = "operations.dashboard.view";
    public const string DashboardAnalyticsView = "operations.dashboard.view-analytics";

    public const string FlightsView = "operations.flights.view";

    /// <summary>Station-wide flight visibility for station staff without assignment (station dispatchers).</summary>
    public const string FlightsViewStation = "operations.flights.view-station";

    public const string FlightsSchedule = "operations.flights.schedule";
    public const string FlightsExport = "operations.flights.export";
    public const string FlightsUpdate = "operations.flights.update";
    public const string FlightsAssign = "operations.flights.assign";
    public const string FlightsInvite = "operations.flights.invite";
    public const string FlightsMerge = "operations.flights.merge";

    public const string WorkOrdersView = "operations.work-orders.view";
    public const string WorkOrdersViewOthers = "operations.work-orders.view-others";
    public const string WorkOrdersAuthor = "operations.work-orders.author";
    public const string WorkOrdersApprove = "operations.work-orders.approve";
    public const string WorkOrdersManageOthers = "operations.work-orders.manage-others";
    public const string WorkOrdersDeleteOthers = "operations.work-orders.delete-others";
    public const string WorkOrdersMerge = "operations.work-orders.merge";
}
