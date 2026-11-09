using Operations.Domain.Flights;

namespace Operations.Application.Contracts;

public sealed record FlightArrivalSubmissionDto(
    Guid Id,
    AssignedEmployeeDto SubmittedByEmployee,
    DateTimeOffset Ata,
    Guid AircraftTypeId,
    string AircraftTypeModel,
    string AircraftTailNumber,
    string? CustomerWorkReferenceNumber,
    AssignedEmployeeDto ReceivedByEmployee,
    string? Remarks,
    bool IsAccepted,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc)
{
    public static FlightArrivalSubmissionDto Map(FlightArrivalSubmission submission) => new(
        submission.Id,
        new(submission.SubmittedByEmployee.StaffMemberId, submission.SubmittedByEmployee.FullName, submission.SubmittedByEmployee.EmployeeId),
        submission.Details.Ata, submission.Details.AircraftType.AircraftTypeId, submission.Details.AircraftType.Model,
        submission.Details.AircraftTailNumber, submission.Details.CustomerWorkReferenceNumber,
        new(submission.Details.ReceivedByEmployee.StaffMemberId, submission.Details.ReceivedByEmployee.FullName, submission.Details.ReceivedByEmployee.EmployeeId),
        submission.Details.Remarks, submission.IsAccepted, submission.CreatedAtUtc, submission.UpdatedAtUtc);
}

public sealed record SaveFlightArrivalRequest(
    DateTimeOffset Ata,
    Guid AircraftTypeId,
    string AircraftTailNumber,
    string? CustomerWorkReferenceNumber,
    string? Remarks);

public sealed record FlightArrivalSavedDto(FlightArrivalSubmissionDto Submission, string RowVersion);
