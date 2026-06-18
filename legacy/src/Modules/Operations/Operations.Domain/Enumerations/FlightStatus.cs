namespace Operations.Domain.Enumerations;

/// <summary>Lifecycle of a flight row for portal and mobile flows.</summary>
public enum FlightStatus
{
    Scheduled = 0,
    InProgress = 1,
    Completed = 2,
    Canceled = 3
}
