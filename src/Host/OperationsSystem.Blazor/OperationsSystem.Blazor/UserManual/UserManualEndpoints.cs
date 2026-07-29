namespace OperationsSystem.Blazor.UserManual;

internal static class UserManualEndpoints
{
    private const string ResourceName = "OperationsSystem.Blazor.UserManual.html";
    private const string DownloadFileName = "NAGS-Operations-Field-Guide.html";
    private const string ContentType = "text/html; charset=utf-8";

    public static IEndpointRouteBuilder MapUserManual(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/manual", (HttpContext context) => CreateResult(context));
        endpoints.MapGet("/manual/index.html", (HttpContext context) => CreateResult(context));
        endpoints.MapGet(
            "/manual/download",
            (HttpContext context) => CreateResult(context, download: true));

        return endpoints;
    }

    internal static Stream OpenResource() =>
        typeof(UserManualEndpoints).Assembly.GetManifestResourceStream(ResourceName)
        ?? throw new InvalidOperationException(
            $"The embedded user manual resource '{ResourceName}' is missing.");

    private static IResult CreateResult(HttpContext context, bool download = false)
    {
        context.Response.Headers["Cache-Control"] = "public, max-age=3600";
        context.Response.Headers["Content-Security-Policy"] =
            "default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; " +
            "img-src data: blob:; font-src data:; base-uri 'none'; form-action 'none'; " +
            "frame-ancestors 'self'; object-src 'none'";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";

        return Results.Stream(
            OpenResource(),
            contentType: ContentType,
            fileDownloadName: download ? DownloadFileName : null);
    }
}
