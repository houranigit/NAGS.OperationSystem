using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Notifications.Infrastructure.Push;

public sealed class FirebaseAppFactory
{
    private const string AppName = "operations-notifications";
    private readonly Lazy<FirebaseApp?> app;

    public FirebaseAppFactory(
        IOptions<FcmOptions> options,
        IHostEnvironment environment,
        ILogger<FirebaseAppFactory> logger)
    {
        app = new Lazy<FirebaseApp?>(
            () => Create(options.Value, environment.ContentRootPath, logger),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public FirebaseApp? GetApp() => app.Value;

    private static FirebaseApp? Create(FcmOptions options, string contentRootPath, ILogger logger)
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
        {
            var credentialPath = Path.IsPathRooted(options.ServiceAccountJsonPath)
                ? options.ServiceAccountJsonPath
                : Path.GetFullPath(options.ServiceAccountJsonPath, contentRootPath);
            credential = CredentialFactory
                .FromFile<ServiceAccountCredential>(credentialPath)
                .ToGoogleCredential();
        }
        else
            credential = GoogleCredential.GetApplicationDefault();

        return FirebaseApp.Create(new AppOptions
        {
            Credential = credential,
            ProjectId = options.ProjectId
        }, AppName);
    }
}
