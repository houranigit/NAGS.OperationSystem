using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Notifications.Infrastructure.Push;

/// <summary>Fails startup immediately when an enabled Firebase credential/app cannot be initialized.</summary>
public sealed class FcmStartupValidator(
    IOptions<FcmOptions> options,
    FirebaseAppFactory firebaseApps) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (options.Value.Enabled && firebaseApps.GetApp() is null)
            throw new InvalidOperationException("FCM is enabled but the Firebase application could not be initialized.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
