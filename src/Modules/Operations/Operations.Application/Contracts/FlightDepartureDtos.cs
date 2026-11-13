using Operations.Domain.Flights;
using System.Security.Cryptography;
using System.Text;

namespace Operations.Application.Contracts;

public sealed record FlightDepartureSubmissionDto(
    Guid Id,
    AssignedEmployeeDto SubmittedByEmployee,
    DateTimeOffset Atd,
    AssignedEmployeeDto HandledByEmployee,
    string? Remarks,
    bool IsAccepted,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    string? SignatureVersion = null,
    bool HasSignature = true,
    string? CreatedByDisplayName = null,
    string? UpdatedByDisplayName = null)
{
    public static FlightDepartureSubmissionDto Map(FlightDepartureSubmission submission, IReadOnlyDictionary<Guid, string>? names = null) => new(
        submission.Id,
        new(submission.SubmittedByEmployee.StaffMemberId, submission.SubmittedByEmployee.FullName, submission.SubmittedByEmployee.EmployeeId),
        submission.Details.Atd,
        new(submission.Details.HandledByEmployee.StaffMemberId, submission.Details.HandledByEmployee.FullName, submission.Details.HandledByEmployee.EmployeeId),
        submission.Details.Remarks, submission.IsAccepted, submission.CreatedAtUtc, submission.UpdatedAtUtc,
        submission.Details.CustomerSignature is { } signature
            ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signature.StorageKey))) : null,
        submission.Details.CustomerSignature is not null, names?.GetValueOrDefault(submission.CreatedByUserId),
        submission.UpdatedByUserId is { } updater ? names?.GetValueOrDefault(updater) : null);
}
