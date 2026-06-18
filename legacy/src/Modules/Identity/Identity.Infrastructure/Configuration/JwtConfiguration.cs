using Identity.Infrastructure.Services;
using Microsoft.Extensions.Configuration;

namespace Identity.Infrastructure.Configuration;

/// <summary>
/// Validates Jwt settings before token generation or bearer configuration.
/// </summary>
public static class JwtConfiguration
{
    public static void EnsureValid(IConfiguration configuration)
    {
        var jwt = configuration.GetSection("Jwt").Get<JwtSettings>();
        if (jwt is null)
            throw new InvalidOperationException(
                "Jwt configuration section is missing. Add a \"Jwt\" section with SecretKey, Issuer, and Audience.");

        if (string.IsNullOrWhiteSpace(jwt.SecretKey))
            throw new InvalidOperationException(
                "Jwt:SecretKey is missing or empty. " +
                "Copy the Jwt block from OperationsSystem.Api appsettings.json into OperationsSystem.Web, " +
                "or run: dotnet user-secrets set \"Jwt:SecretKey\" \"<at-least-32-chars>\" --project src/Host/OperationsSystem.Web. " +
                "If you use environment variables, remove or fix Jwt__SecretKey when it overrides appsettings with an empty value.");

        if (jwt.SecretKey.Length < 32)
            throw new InvalidOperationException(
                "Jwt:SecretKey must be at least 32 characters for HS256.");
    }
}
