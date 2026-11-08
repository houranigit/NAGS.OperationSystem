using BuildingBlocks.Domain.Entities;
using Operations.Domain.ValueObjects;

namespace Operations.Domain.Flights;

/// <summary>One employee's departure submission, retained independently of other employees' inputs.</summary>
public sealed class FlightDepartureSubmission : Entity<Guid>
{
    private FlightDepartureSubmission() { }

    internal FlightDepartureSubmission(
        Guid id,
        Guid flightId,
        StaffMemberSnapshot submittedByEmployee,
        DepartureDetails details,
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
    public DepartureDetails Details { get; private set; } = null!;
    public bool IsAccepted { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public Guid? UpdatedByUserId { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    internal void UpdateDetails(DepartureDetails details, Guid updatedByUserId, DateTimeOffset now)
    {
        Details = details.Copy();
        UpdatedByUserId = updatedByUserId;
        UpdatedAtUtc = now;
    }

    internal void ClearAcceptance() => IsAccepted = false;
}
