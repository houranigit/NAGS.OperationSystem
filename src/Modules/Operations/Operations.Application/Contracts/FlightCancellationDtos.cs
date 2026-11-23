using Operations.Domain.Flights;

namespace Operations.Application.Contracts;

public sealed record FlightCancellationSubmissionDto(
    Guid Id,
    AssignedEmployeeDto? SubmittedByEmployee,
    DateTimeOffset CanceledAtUtc,
    string? Remarks,
    bool IsAccepted,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    Guid? CreatedByUserId = null,
    string? CreatedByDisplayName = null)
{
    public static FlightCancellationSubmissionDto Map(FlightCancellationSubmission submission,
        IReadOnlyDictionary<Guid, string>? names = null, bool legacyStaffAttribution = false)
    {
        var creatorName = submission.SubmittedByEmployee is null ? names?.GetValueOrDefault(submission.CreatedByUserId) : null;
        return new(submission.Id, AssignedEmployeeDto.MapSubmitter(submission.SubmittedByEmployee, creatorName, legacyStaffAttribution),
            submission.Details.CanceledAtUtc, submission.Details.Remarks, submission.IsAccepted,
            submission.CreatedAtUtc, submission.UpdatedAtUtc, submission.CreatedByUserId, creatorName);
    }
}

public sealed record SaveFlightCancellationRequest(
    DateTimeOffset CanceledAtUtc,
    string? Remarks,
    bool AcknowledgeConflictingSubmission = false);

public sealed record FlightCancellationSavedDto(FlightCancellationSubmissionDto Submission, string RowVersion);

public sealed record FlightCancellationRevertedDto(IReadOnlyList<FlightCancellationSubmissionDto> CancellationSubmissions, string RowVersion);
