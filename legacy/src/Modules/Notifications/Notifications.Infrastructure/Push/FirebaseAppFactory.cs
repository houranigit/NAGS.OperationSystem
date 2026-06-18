using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Notifications.Infrastructure.Push;

/// <summary>
/// Creates the singleton <see cref="FirebaseApp"/> we hand to <see cref="FcmNotificationPusher"/>.
/// Returns <c>null</c> when FCM is disabled in configuration (e.g. local dev), letting the
/// composite pusher silently fall back to SignalR-only delivery.
/// </summary>
public sealed class FirebaseAppFactory
{
    private readonly Lazy<FirebaseApp?> _app;

    public FirebaseAppFactory(IOptions<FcmOptions> options, ILogger<FirebaseAppFactory> logger)
    {
        var opts = options.Value;
        _app = new Lazy<FirebaseApp?>(() =>
        {
            if (!opts.Enabled)
            {
                logger.LogInformation("FCM is disabled (Fcm:Enabled=false). Push fan-out will be SignalR-only.");
                return null;
            }

            try
            {
                var existing = FirebaseApp.DefaultInstance;
                if (existing is not null) return existing;
            }
            catch
            {
                // FirebaseAdmin throws when no app is registered yet; fall through to create one.
            }

            GoogleCredential credential;
            if (!string.IsNullOrWhiteSpace(opts.ServiceAccountJson))
            {
                credential = GoogleCredential.FromJson(opts.ServiceAccountJson);
            }
            else if (!string.IsNullOrWhiteSpace(opts.ServiceAccountJsonPath))
            {
                credential = GoogleCredential.FromFile(opts.ServiceAccountJsonPath);
            }
            else
            {
                logger.LogWarning(
                    "FCM is enabled but no service-account JSON was supplied — push will fall back to SignalR-only.");
                return null;
            }

            return FirebaseApp.Create(new AppOptions { Credential = credential });
        });
    }

    public FirebaseApp? GetApp() => _app.Value;
}
