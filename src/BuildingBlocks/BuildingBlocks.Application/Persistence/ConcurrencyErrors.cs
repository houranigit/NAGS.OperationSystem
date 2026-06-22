using BuildingBlocks.Domain.Results;

namespace BuildingBlocks.Application.Persistence;

/// <summary>Shared errors for optimistic-concurrency conflicts (mapped to HTTP 409).</summary>
public static class ConcurrencyErrors
{
    public static readonly Error Stale = Error.Conflict(
        "The record was changed by someone else. Reload it and try again.",
        "General.ConcurrencyConflict");

    /// <summary>Returned when a mutating request omits the required <c>If-Match</c> precondition.</summary>
    public static readonly Error PreconditionRequired = Error.Validation(
        "This change requires the current version (If-Match). Reload the record and retry.",
        "General.PreconditionRequired");
}
