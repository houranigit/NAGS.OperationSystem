namespace Operations.Domain.Enumerations;

/// <summary>Lifecycle of a <see cref="Flights.Flight"/>. Completed and Canceled are terminal,
/// post-approval, billable outcomes; Merged is a soft-archived duplicate loser. A submitted work
/// order keeps the flight InProgress; there is no separate pending-review flight status.</summary>
public enum FlightStatus
{
    Scheduled = 0,
    InProgress = 1,
    Completed = 2,
    Canceled = 3,
    Merged = 4
}

/// <summary>Lifecycle of a <see cref="WorkOrders.WorkOrder"/> (the operational completion document).</summary>
public enum WorkOrderStatus
{
    Draft = 0,
    Submitted = 1,
    Approved = 2
}

/// <summary>Outcome kind of a work order: a normal completion or a customer cancellation.</summary>
public enum WorkOrderType
{
    Completion = 0,
    Cancellation = 1
}

/// <summary>Classification of a performed service line relative to what was planned.</summary>
public enum ServiceLineOrigin
{
    Planned = 0,
    Extra = 1
}

/// <summary>Reporting classification of a work-order task.</summary>
public enum TaskType
{
    Major = 0,
    Minor = 1
}

/// <summary>Kind of a work-order task attachment (drives allowed content types and size caps).</summary>
public enum TaskAttachmentKind
{
    Image = 0,
    Voice = 1,
    Document = 2
}

/// <summary>Kind of event recorded on a flight's portal-visible timeline.</summary>
public enum FlightTimelineEventType
{
    FlightScheduled = 0,
    AdHocFlightCreated = 1,
    WorkOrderCreated = 2,
    WorkOrderSubmitted = 3,
    WorkOrderApproved = 4,
    WorkOrderReturned = 5,
    WorkOrderRejected = 6,
    FlightCompleted = 7,
    FlightCanceled = 8,
    EmployeeAssigned = 9,
    ApprovedSnapshotCleared = 10
}

/// <summary>Kind of event recorded on a work order's timeline/history.</summary>
public enum WorkOrderTimelineEventType
{
    Submitted = 0,
    Updated = 1,
    Approved = 2,
    Returned = 3,
    Superseded = 4
}
