using BuildingBlocks.Domain.Entities;
using Operations.Domain.ValueObjects;

namespace Operations.Domain.Flights;

/// <summary>One employee's arrival submission, retained independently of other employees' inputs.</summary>
public sealed class FlightArrivalSubmission : Entity<Guid>
{
    private FlightArrivalSubmission() { }

    internal FlightArrivalSubmission(
        Guid id,
        Guid flightId,
        StaffMemberSnapshot submittedByEmployee,
        ArrivalDetails details,
        bool isAccepted,
        Guid createdByUserId,
        DateTimeOffset now)
    {
        Id = id;
        FlightId = flightId;
        SubmittedByEmployee = new StaffMemberSnapshot(submittedByEmployee.StaffMemberId, submittedByEmployee.FullName, submittedByEmployee.EmployeeId);
        Details = details.Copy();
        IsAccepted = isAccepted;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = now;
    }

    public Guid FlightId { get; private set; }
    public StaffMemberSnapshot SubmittedByEmployee { get; private set; } = null!;
    public ArrivalDetails Details { get; private set; } = null!;
    public bool IsAccepted { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public Guid? UpdatedByUserId { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    internal void UpdateDetails(ArrivalDetails details, Guid updatedByUserId, DateTimeOffset now)
    {
        Details = details.Copy();
        UpdatedByUserId = updatedByUserId;
        UpdatedAtUtc = now;
    }

    internal void ClearAcceptance() => IsAccepted = false;
}
