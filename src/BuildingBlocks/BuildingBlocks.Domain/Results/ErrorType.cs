namespace BuildingBlocks.Domain.Results;

/// <summary>
/// Categorizes a domain/application failure so the API edge can map it to a consistent
/// HTTP status code without endpoints hand-rolling their own error handling.
/// </summary>
public enum ErrorType
{
    None = 0,
    Validation = 1,
    NotFound = 2,
    Conflict = 3,
    Unauthorized = 4,
    Forbidden = 5,
    Failure = 6
}
