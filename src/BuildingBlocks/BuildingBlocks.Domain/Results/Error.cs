namespace BuildingBlocks.Domain.Results;

/// <summary>
/// An expected failure carrying an application error <see cref="Code"/>, a human-readable
/// <see cref="Description"/>, a <see cref="ErrorType"/> category, and optional field-level
/// <see cref="Failures"/> for validation errors. This is the single failure representation
/// returned by domain and application code; a central API mapper turns it into ProblemDetails.
/// </summary>
public sealed record Error
{
    public string Code { get; }
    public string Description { get; }
    public ErrorType Type { get; }
    public IReadOnlyDictionary<string, string[]>? Failures { get; }

    private Error(string code, string description, ErrorType type, IReadOnlyDictionary<string, string[]>? failures = null)
    {
        Code = code;
        Description = description;
        Type = type;
        Failures = failures;
    }

    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.None);

    public static Error Validation(string description, string code = "General.Validation") =>
        new(code, description, ErrorType.Validation);

    public static Error Validation(IReadOnlyDictionary<string, string[]> failures, string description = "One or more validation errors occurred.", string code = "General.Validation") =>
        new(code, description, ErrorType.Validation, failures);

    public static Error NotFound(string description, string code = "General.NotFound") =>
        new(code, description, ErrorType.NotFound);

    public static Error Conflict(string description, string code = "General.Conflict") =>
        new(code, description, ErrorType.Conflict);

    public static Error Unauthorized(string description = "Unauthorized.", string code = "General.Unauthorized") =>
        new(code, description, ErrorType.Unauthorized);

    public static Error Forbidden(string description = "Forbidden.", string code = "General.Forbidden") =>
        new(code, description, ErrorType.Forbidden);

    public static Error Failure(string description, string code = "General.Failure") =>
        new(code, description, ErrorType.Failure);
}
