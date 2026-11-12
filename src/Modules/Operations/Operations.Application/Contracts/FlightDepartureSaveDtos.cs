namespace Operations.Application.Contracts;

/// <summary>Signature bytes are a base64 PNG in JSON; storage metadata is always supplied by the server.</summary>
public sealed record SaveFlightDepartureRequest(
    DateTimeOffset Atd,
    byte[]? SignatureContent,
    DateTimeOffset? SignatureSignedAtUtc,
    string? Remarks,
    bool AcknowledgeConflictingSubmission = false,
    Guid? TargetSubmissionId = null);

public sealed record FlightDepartureSavedDto(FlightDepartureSubmissionDto Submission, string RowVersion);
