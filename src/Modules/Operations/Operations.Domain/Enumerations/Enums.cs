namespace Operations.Domain.Enumerations;

/// <summary>Lifecycle of a <see cref="Flights.Flight"/>. Completed and Canceled are terminal,
/// post-approval, billable outcomes; Merged is a soft-archived duplicate loser.</summary>
public enum FlightStatus
{
    Scheduled = 0,
    InProgress = 1,
    PendingReview = 2,
    Completed = 3,
    Canceled = 4,
    Merged = 5
}

/// <summary>Lifecycle of a <see cref="WorkOrders.WorkOrder"/> (the operational completion document).</summary>
public enum WorkOrderStatus
{
    Draft = 0,
    Submitted = 1,
    Approved = 2,
    Returned = 3,
    Rejected = 4,
    Superseded = 5
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
