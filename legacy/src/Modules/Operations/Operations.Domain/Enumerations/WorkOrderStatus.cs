namespace Operations.Domain.Enumerations;

public enum WorkOrderStatus
{
    UnderReview = 0,
    Approved = 1,
    Rejected = 2,

    /// <summary>
    /// Sibling work orders entered when another work order on the same flight is approved.
    /// Hard-deleted by the deletion job after a configurable delay; restored to
    /// <see cref="UnderReview"/> if the winning approval is revoked first.
    /// </summary>
    Deleting = 3
}
