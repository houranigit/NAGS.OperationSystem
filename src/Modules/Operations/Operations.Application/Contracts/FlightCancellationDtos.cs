using Operations.Domain.Flights;

namespace Operations.Application.Contracts;

public sealed record FlightCancellationSubmissionDto(
    Guid Id,
    AssignedEmployeeDto SubmittedByEmployee,
    DateTimeOffset CanceledAtUtc,
    string? Remarks,
    bool IsAccepted,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc)
{
    public static FlightCancellationSubmissionDto Map(FlightCancellationSubmission submission) => new(
        submission.Id,
        new(submission.SubmittedByEmployee.StaffMemberId, submission.SubmittedByEmployee.FullName, submission.SubmittedByEmployee.EmployeeId),
        submission.Details.CanceledAtUtc, submission.Details.Remarks, submission.IsAccepted,
        submission.CreatedAtUtc, submission.UpdatedAtUtc);
}

public sealed record SaveFlightCancellationRequest(
    DateTimeOffset CanceledAtUtc,
    string? Remarks,
    bool AcknowledgeConflictingSubmission = false);

public sealed record FlightCancellationSavedDto(FlightCancellationSubmissionDto Submission, string RowVersion);

public sealed record FlightCancellationRevertedDto(IReadOnlyList<FlightCancellationSubmissionDto> CancellationSubmissions, string RowVersion);
