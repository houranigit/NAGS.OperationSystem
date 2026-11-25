using System.Text.Json.Serialization;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;

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
    string? CreatedByDisplayName = null,
    [property: JsonConverter(typeof(JsonStringEnumConverter<CancellationType>))]
    CancellationType CancellationType = CancellationType.Unplanned)
{
    public static FlightCancellationSubmissionDto Map(FlightCancellationSubmission submission,
        IReadOnlyDictionary<Guid, string>? names = null, bool legacyStaffAttribution = false)
    {
        var creatorName = submission.SubmittedByEmployee is null ? names?.GetValueOrDefault(submission.CreatedByUserId) : null;
        return new(submission.Id, AssignedEmployeeDto.MapSubmitter(submission.SubmittedByEmployee, creatorName, legacyStaffAttribution),
            submission.Details.CanceledAtUtc, submission.Details.Remarks, submission.IsAccepted,
            submission.CreatedAtUtc, submission.UpdatedAtUtc, submission.CreatedByUserId, creatorName, submission.Details.Type);
    }
}

public sealed record SaveFlightCancellationRequest(
    DateTimeOffset CanceledAtUtc,
    string? Remarks,
    bool AcknowledgeConflictingSubmission = false,
    CancellationType? CancellationType = null);

public sealed record FlightCancellationSavedDto(FlightCancellationSubmissionDto Submission, string RowVersion);

public sealed record FlightCancellationRevertedDto(IReadOnlyList<FlightCancellationSubmissionDto> CancellationSubmissions, string RowVersion);
