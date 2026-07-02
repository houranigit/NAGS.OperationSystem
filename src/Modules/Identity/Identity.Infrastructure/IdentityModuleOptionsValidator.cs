using Identity.Application;
using Identity.Application.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Identity.Infrastructure;

/// <summary>
/// Fail-fast validation of Identity configuration at startup. A weak or missing JWT signing key,
/// or missing issuer/audience, stops the application from booting in any environment.
/// </summary>
public sealed class IdentityModuleOptionsValidator(
    IConfiguration configuration,
    IHostEnvironment environment) : IValidateOptions<IdentityModuleOptions>
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

        if (options.MaxFailedSignInAttempts <= 0)
            failures.Add("Identity:MaxFailedSignInAttempts must be positive.");

        if (options.LockoutMinutes <= 0)
            failures.Add("Identity:LockoutMinutes must be positive.");

        if (options.InvitationExpiryHours <= 0)
            failures.Add("Identity:InvitationExpiryHours must be positive.");

        if (options.PasswordResetExpiryHours <= 0)
            failures.Add("Identity:PasswordResetExpiryHours must be positive.");

        if (options.EmailChangeExpiryHours <= 0)
            failures.Add("Identity:EmailChangeExpiryHours must be positive.");

        RequireAbsoluteHttpUrl(options.ActivationUrlBase, "Identity:ActivationUrlBase", failures);
        RequireAbsoluteHttpUrl(options.PasswordResetUrlBase, "Identity:PasswordResetUrlBase", failures);
        RequireAbsoluteHttpUrl(options.EmailChangeConfirmUrlBase, "Identity:EmailChangeConfirmUrlBase", failures);

        if (!string.IsNullOrWhiteSpace(options.Admin.Password))
            failures.AddRange(PasswordPolicy.Validate(options.Admin.Password, "Identity:Admin:Password"));

        var emailEnabled = configuration.GetValue<bool?>("EmailSettings:EnableEmailNotifications") ?? false;
        if (string.IsNullOrWhiteSpace(options.Admin.Password) && !emailEnabled && !environment.IsDevelopment())
        {
            failures.Add(
                "Passwordless bootstrap admin activation requires EmailSettings:EnableEmailNotifications=true outside Development. " +
                "Configure SMTP delivery or provide Identity:Admin:Password for controlled development/test environments.");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }

    private static void RequireAbsoluteHttpUrl(string? value, string settingName, List<string> failures)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            failures.Add($"{settingName} must be an absolute HTTP(S) URL.");
        }
    }
}
