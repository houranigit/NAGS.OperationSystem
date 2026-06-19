using BuildingBlocks.Domain.Results;
using Microsoft.AspNetCore.Http;
using HttpResults = Microsoft.AspNetCore.Http.Results;

namespace BuildingBlocks.Api.Results;

/// <summary>
/// The single place that maps a domain/application <see cref="Error"/> to a ProblemDetails
/// HTTP response. Endpoints must route failures through here instead of hand-rolling errors.
/// </summary>
public static class ApiResults
{
    public static IResult Problem(Error error)
    {
        if (error.Type == ErrorType.Validation && error.Failures is { Count: > 0 })
        {
            return HttpResults.ValidationProblem(
                error.Failures,
                detail: error.Description,
                extensions: CodeExtension(error));
        }

        var statusCode = StatusFor(error.Type);
        return HttpResults.Problem(
            title: TitleFor(error.Type),
            detail: error.Description,
            statusCode: statusCode,
            extensions: CodeExtension(error));
    }

    private static int StatusFor(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status500InternalServerError
    };

    private static string TitleFor(ErrorType type) => type switch
    {
        ErrorType.Validation => "Validation failed",
        ErrorType.Unauthorized => "Unauthorized",
        ErrorType.Forbidden => "Forbidden",
        ErrorType.NotFound => "Resource not found",
        ErrorType.Conflict => "Conflict",
        _ => "An unexpected error occurred"
    };

    private static Dictionary<string, object?> CodeExtension(Error error) =>
        new() { ["code"] = error.Code };
}
