namespace Operations.Domain.Authorization;

/// <summary>
/// Operations permission catalog. Codes follow the lowercase <c>operations.resource.action</c>
/// convention. UserType compatibility is declared in the application-layer permission catalog.
/// </summary>
public static class OperationsPermissions
{
    public static class Flights
    {
        public const string View = "operations.flights.view";

        public const string Export = "operations.flights.export";

        /// <summary>
        /// Station-wide flight visibility for station staff (e.g. station dispatchers): every flight
        /// at their own station regardless of the assigned-employee roster. Without it, station staff
        /// see only Per-Landing flights plus flights they are assigned to. Administrators already see
        /// all stations through their data scope.
        /// </summary>
        public const string ViewStation = "operations.flights.view-station";

        public const string Schedule = "operations.flights.schedule";
        public const string Update = "operations.flights.update";
        public const string Assign = "operations.flights.assign";
        public const string Invite = "operations.flights.invite";
        public const string Merge = "operations.flights.merge";
    }

    public static class Dashboard
    {
        public const string View = "operations.dashboard.view";
        public const string ViewAnalytics = "operations.dashboard.view-analytics";
        public const string Export = "operations.dashboard.export";
    }

    public static class WorkOrders
    {
        public const string View = "operations.work-orders.view";
        public const string ViewOthers = "operations.work-orders.view-others";
        public const string Author = "operations.work-orders.author";
        public const string ManageOthers = "operations.work-orders.manage-others";
        public const string Approve = "operations.work-orders.approve";
        public const string DeleteOthers = "operations.work-orders.delete-others";
        public const string Merge = "operations.work-orders.merge";
    }
}
