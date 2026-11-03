namespace Operations.Domain.Authorization;

public static class OperationsPermissions
{
    public static class Flights
    {
        public const string View = "operations.flights.view";
        public const string ViewStation = "operations.flights.view-station";
        public const string Schedule = "operations.flights.schedule";
        public const string Update = "operations.flights.update";
        public const string Assign = "operations.flights.assign";
    }
}
