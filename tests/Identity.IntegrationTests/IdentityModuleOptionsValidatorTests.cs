using Identity.Application;
using Identity.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Shouldly;

namespace Identity.IntegrationTests;

public sealed class IdentityModuleOptionsValidatorTests
{
    [Fact]
    public void Passwordless_bootstrap_requires_email_delivery_outside_development()
    {
        var validator = CreateValidator(emailEnabled: false, environmentName: Environments.Production);

        var result = validator.Validate(null, ValidOptions(adminPassword: string.Empty));

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Passwordless bootstrap admin activation requires EmailSettings:EnableEmailNotifications=true");
    }

    [Fact]
    public void Passwordless_bootstrap_is_allowed_in_development_for_logged_local_activation()
    {
        var validator = CreateValidator(emailEnabled: false, environmentName: Environments.Development);

        var result = validator.Validate(null, ValidOptions(adminPassword: string.Empty));

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Explicit_bootstrap_password_is_allowed_without_email_delivery()
    {
        var validator = CreateValidator(emailEnabled: false, environmentName: Environments.Production);

        var result = validator.Validate(null, ValidOptions(adminPassword: "Admin#12345"));

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Auth_link_bases_must_be_absolute_http_urls()
    {
        var validator = CreateValidator(emailEnabled: true, environmentName: Environments.Production);
        var options = ValidOptions(adminPassword: string.Empty);
        options.EmailChangeConfirmUrlBase = "/confirm-email-change";

        var result = validator.Validate(null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Identity:EmailChangeConfirmUrlBase must be an absolute HTTP(S) URL.");
    }

    [Fact]
    public void Auth_time_windows_and_lockout_settings_must_be_positive()
    {
        var validator = CreateValidator(emailEnabled: true, environmentName: Environments.Production);
        var options = ValidOptions(adminPassword: string.Empty);
        options.MaxFailedSignInAttempts = 0;
        options.LockoutMinutes = 0;
        options.InvitationExpiryHours = 0;
        options.PasswordResetExpiryHours = 0;
        options.EmailChangeExpiryHours = 0;

        var result = validator.Validate(null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Identity:MaxFailedSignInAttempts must be positive.");
        result.FailureMessage.ShouldContain("Identity:LockoutMinutes must be positive.");
        result.FailureMessage.ShouldContain("Identity:InvitationExpiryHours must be positive.");
        result.FailureMessage.ShouldContain("Identity:PasswordResetExpiryHours must be positive.");
        result.FailureMessage.ShouldContain("Identity:EmailChangeExpiryHours must be positive.");
    }

    private static IdentityModuleOptionsValidator CreateValidator(bool emailEnabled, string environmentName)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EmailSettings:EnableEmailNotifications"] = emailEnabled.ToString()
            })
            .Build();

        return new IdentityModuleOptionsValidator(configuration, new TestHostEnvironment(environmentName));
    }

    private static IdentityModuleOptions ValidOptions(string adminPassword) =>
        new()
        {
            Jwt = new JwtOptions
            {
                Issuer = "operations-system",
                Audience = "operations-system",
                SigningKey = "integration-tests-signing-key-must-be-long-enough-1234567890"
            },
            Admin = new AdminBootstrapOptions
            {
                Email = "admin@nags.sa",
                DisplayName = "System Administrator",
                Password = adminPassword
            }
        };

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Identity.IntegrationTests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
