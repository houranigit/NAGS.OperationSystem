using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Notifications.Infrastructure.Push;

public sealed class FirebaseAppFactory
{
    private const string AppName = "operations-notifications";
    private readonly Lazy<FirebaseApp?> app;

    public FirebaseAppFactory(IOptions<FcmOptions> options, ILogger<FirebaseAppFactory> logger)
    {
        app = new Lazy<FirebaseApp?>(() => Create(options.Value, logger), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public FirebaseApp? GetApp() => app.Value;

    private static FirebaseApp? Create(FcmOptions options, ILogger logger)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("FCM delivery is disabled; persisted inbox and SignalR delivery remain active.");
            return null;
        }

        GoogleCredential credential;
        if (!string.IsNullOrWhiteSpace(options.ServiceAccountJson))
            credential = CredentialFactory
                .FromJson<ServiceAccountCredential>(options.ServiceAccountJson)
                .ToGoogleCredential();
        else if (!string.IsNullOrWhiteSpace(options.ServiceAccountJsonPath))
            credential = CredentialFactory
                .FromFile<ServiceAccountCredential>(options.ServiceAccountJsonPath)
                .ToGoogleCredential();
        else
            credential = GoogleCredential.GetApplicationDefault();

        return FirebaseApp.Create(new AppOptions
        {
            Credential = credential,
            ProjectId = options.ProjectId
        }, AppName);
    }
}
