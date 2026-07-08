namespace Operations.Domain.Enumerations;

/// <summary>Lifecycle of a <see cref="Flights.Flight"/>.</summary>
public enum FlightStatus
{
    Scheduled = 0,
    InProgress = 1,
    Completed = 2,
    Canceled = 3,
    Merged = 4
}

/// <summary>Kind of event recorded on a flight's portal-visible timeline.</summary>
public enum FlightTimelineEventType
{
    FlightScheduled = 0,
    EmployeeAssigned = 1
}
