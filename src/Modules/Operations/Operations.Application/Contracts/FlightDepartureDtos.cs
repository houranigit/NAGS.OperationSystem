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
    string? SignatureVersion = null)
{
    public static FlightDepartureSubmissionDto Map(FlightDepartureSubmission submission) => new(
        submission.Id,
        new(submission.SubmittedByEmployee.StaffMemberId, submission.SubmittedByEmployee.FullName, submission.SubmittedByEmployee.EmployeeId),
        submission.Details.Atd,
        new(submission.Details.HandledByEmployee.StaffMemberId, submission.Details.HandledByEmployee.FullName, submission.Details.HandledByEmployee.EmployeeId),
        submission.Details.Remarks, submission.IsAccepted, submission.CreatedAtUtc, submission.UpdatedAtUtc,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(submission.Details.CustomerSignature.StorageKey))));
}
