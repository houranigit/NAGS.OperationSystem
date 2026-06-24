using Identity.Application;
using Microsoft.Extensions.Options;

namespace Identity.Infrastructure;

/// <summary>
/// Fail-fast validation of Identity configuration at startup. A weak or missing JWT signing key,
/// or missing issuer/audience, stops the application from booting in any environment.
/// </summary>
public sealed class IdentityModuleOptionsValidator : IValidateOptions<IdentityModuleOptions>
{
    // HMAC-SHA256 needs a key of at least 256 bits (32 bytes) of entropy.
    private const int MinSigningKeyLength = 32;

    public ValidateOptionsResult Validate(string? name, IdentityModuleOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Jwt.Issuer))
            failures.Add("Identity:Jwt:Issuer is required.");

        if (string.IsNullOrWhiteSpace(options.Jwt.Audience))
            failures.Add("Identity:Jwt:Audience is required.");

        if (string.IsNullOrWhiteSpace(options.Jwt.SigningKey) || options.Jwt.SigningKey.Length < MinSigningKeyLength)
            failures.Add($"Identity:Jwt:SigningKey must be configured and at least {MinSigningKeyLength} characters.");

        if (options.AccessTokenMinutes <= 0)
            failures.Add("Identity:AccessTokenMinutes must be positive.");

        if (options.RefreshTokenDays <= 0)
            failures.Add("Identity:RefreshTokenDays must be positive.");

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
