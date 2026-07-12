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

/// <summary>Service-derived grouping used by flight list filters.</summary>
public enum FlightServiceCategory
{
    PerLanding = 0,
    OnCall = 1,
    Other = 2
}

/// <summary>Kind of event recorded on a flight's portal-visible timeline.</summary>
public enum FlightTimelineEventType
{
    FlightScheduled = 0,
    EmployeeAssigned = 1,
    WorkOrderSubmitted = 2,
    FlightCompleted = 3,
    FlightCanceled = 4,
    FlightReopened = 5
}

public enum WorkOrderStatus
{
    Submitted = 0,
    Returned = 1,
    Approved = 2,
    Merged = 3
}

public enum WorkOrderType
{
    Completion = 0,
    Cancellation = 1
}

public enum TaskType
{
    Major = 0,
    Minor = 1
}

public enum TaskAttachmentKind
{
    Image = 0,
    Voice = 1,
    Document = 2
}

public enum WorkOrderTimelineEventType
{
    Submitted = 0,
    Updated = 1,
    ConvertedToCompletion = 2,
    ConvertedToCancellation = 3,
    Approved = 4,
    NumberAssigned = 5,
    Returned = 6,
    NumberReleased = 7,
    Merged = 8
}
