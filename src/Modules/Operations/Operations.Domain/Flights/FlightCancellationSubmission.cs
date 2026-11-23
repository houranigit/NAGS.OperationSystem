using BuildingBlocks.Domain.Entities;
using Operations.Domain.ValueObjects;

namespace Operations.Domain.Flights;

/// <summary>A cancellation report attributed either to a staff member or to its creator account.</summary>
public sealed class FlightCancellationSubmission : Entity<Guid>
{
    private FlightCancellationSubmission() { }

    internal FlightCancellationSubmission(
        Guid id,
        Guid flightId,
        StaffMemberSnapshot? submittedByEmployee,
        CancellationDetails details,
        bool isAccepted,
        Guid createdByUserId,
        DateTimeOffset now)
    {
        Id = id;
        FlightId = flightId;
        SubmittedByEmployee = submittedByEmployee is null ? null
            : new StaffMemberSnapshot(submittedByEmployee.StaffMemberId, submittedByEmployee.FullName, submittedByEmployee.EmployeeId);
        Details = details.Copy();
        IsAccepted = isAccepted;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = now;
    }

    public Guid FlightId { get; private set; }
    public StaffMemberSnapshot? SubmittedByEmployee { get; private set; }
    public CancellationDetails Details { get; private set; } = null!;
    public bool IsAccepted { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public Guid? UpdatedByUserId { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    public bool IsOwnedBy(Guid? staffMemberId, Guid? userId) => staffMemberId is { } staffId
        ? SubmittedByEmployee?.StaffMemberId == staffId
        : SubmittedByEmployee is null && CreatedByUserId == userId;

    internal void UpdateDetails(CancellationDetails details, Guid updatedByUserId, DateTimeOffset now)
    {
        Details = details.Copy();
        UpdatedByUserId = updatedByUserId;
        UpdatedAtUtc = now;
    }

    internal void ClearAcceptance() => IsAccepted = false;
}
