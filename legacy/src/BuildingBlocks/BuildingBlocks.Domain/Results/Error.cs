namespace BuildingBlocks.Domain.Results;

public sealed record Error(string Code, string Description, ErrorType Type)
{
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.None);
    public static readonly Error NullValue = new("Error.NullValue", "A null value was provided.", ErrorType.Failure);

    public static Error NotFound(string description) =>
        new("Error.NotFound", description, ErrorType.NotFound);

    public static Error Validation(string description) =>
        new("Error.Validation", description, ErrorType.Validation);

    public static Error Conflict(string description) =>
        new("Error.Conflict", description, ErrorType.Conflict);

    public static Error Failure(string description) =>
        new("Error.Failure", description, ErrorType.Failure);

    public static Error Unauthorized(string description = "Unauthorized.") =>
        new("Error.Unauthorized", description, ErrorType.Unauthorized);
}

public enum ErrorType
{
    None,
    NotFound,
    Validation,
    Conflict,
    Failure,
    Unauthorized
}
