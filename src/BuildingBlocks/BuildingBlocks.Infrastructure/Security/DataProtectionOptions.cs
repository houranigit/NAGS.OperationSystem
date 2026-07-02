using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Infrastructure.Security;

/// <summary>Production-facing Data Protection settings shared by MFA and durable email encryption.</summary>
public sealed class OperationsDataProtectionOptions
{
    public const string SectionName = "DataProtection";

    public string ApplicationName { get; set; } = "operations-system";

    /// <summary>Durable, shared key-ring directory. Required in Production by default.</summary>
    public string? KeyRingPath { get; set; }

    public bool RequirePersistedKeyRingInProduction { get; set; } = true;
}

public sealed class OperationsDataProtectionOptionsValidator(IHostEnvironment environment)
    : IValidateOptions<OperationsDataProtectionOptions>
{
    public ValidateOptionsResult Validate(string? name, OperationsDataProtectionOptions options)
    {
        if (environment.IsProduction()
            && options.RequirePersistedKeyRingInProduction
            && string.IsNullOrWhiteSpace(options.KeyRingPath))
        {
            return ValidateOptionsResult.Fail(
                "DataProtection:KeyRingPath is required in Production so MFA secrets and queued emails remain decryptable across restarts.");
        }

        if (string.IsNullOrWhiteSpace(options.ApplicationName))
            return ValidateOptionsResult.Fail("DataProtection:ApplicationName is required.");

        return ValidateOptionsResult.Success;
    }
}
