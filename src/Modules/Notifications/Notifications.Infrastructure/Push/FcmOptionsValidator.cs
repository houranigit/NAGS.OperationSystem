using Microsoft.Extensions.Options;

namespace Notifications.Infrastructure.Push;

public sealed class FcmOptionsValidator : IValidateOptions<FcmOptions>
{
    public ValidateOptionsResult Validate(string? name, FcmOptions options)
    {
        if (options.Required && !options.Enabled)
            return ValidateOptionsResult.Fail("Notifications:Fcm:Enabled must be true in this environment.");
        if (!options.Enabled)
            return ValidateOptionsResult.Success;
        if (string.IsNullOrWhiteSpace(options.ProjectId))
            return ValidateOptionsResult.Fail("Notifications:Fcm:ProjectId is required when FCM is enabled.");
        if (!string.IsNullOrWhiteSpace(options.ServiceAccountJson) &&
            !string.IsNullOrWhiteSpace(options.ServiceAccountJsonPath))
        {
            return ValidateOptionsResult.Fail(
                "Configure either Notifications:Fcm:ServiceAccountJson or ServiceAccountJsonPath, not both.");
        }
        return ValidateOptionsResult.Success;
    }
}
