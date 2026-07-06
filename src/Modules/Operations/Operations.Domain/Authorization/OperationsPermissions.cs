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
        public const string Cancel = "operations.flights.cancel";
        public const string Reopen = "operations.flights.reopen";
        public const string Merge = "operations.flights.merge";
    }

    public static class WorkOrders
    {
        public const string View = "operations.work-orders.view";
        public const string Author = "operations.work-orders.author";
        public const string Submit = "operations.work-orders.submit";
        public const string Approve = "operations.work-orders.approve";
        public const string Reject = "operations.work-orders.reject";
        public const string Return = "operations.work-orders.return";
        public const string Merge = "operations.work-orders.merge";
    }

    public static class Dashboard
    {
        public const string View = "operations.dashboard.view";
    }
}
