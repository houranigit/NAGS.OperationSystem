using Operations.Domain.Flights;

namespace Operations.Application.Contracts;

public sealed record FlightDepartureSubmissionDto(
    Guid Id,
    AssignedEmployeeDto SubmittedByEmployee,
    DateTimeOffset Atd,
    AssignedEmployeeDto HandledByEmployee,
    string? Remarks,
    bool IsAccepted,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc)
{
    public static FlightDepartureSubmissionDto Map(FlightDepartureSubmission submission) => new(
        submission.Id,
        new(submission.SubmittedByEmployee.StaffMemberId, submission.SubmittedByEmployee.FullName, submission.SubmittedByEmployee.EmployeeId),
        submission.Details.Atd,
        new(submission.Details.HandledByEmployee.StaffMemberId, submission.Details.HandledByEmployee.FullName, submission.Details.HandledByEmployee.EmployeeId),
        submission.Details.Remarks, submission.IsAccepted, submission.CreatedAtUtc, submission.UpdatedAtUtc);
}
