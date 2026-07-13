using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.Auth;

namespace OperationsSystem.Blazor.Client.Features.Notifications;

/// <summary>
/// Owns the authenticated, reconnecting SignalR channel for user-facing notifications. Initial
/// connection failures are retried as well as established connection drops; neither blocks inbox
/// REST access.
/// </summary>
public sealed class NotificationsRealtimeClient(
    NavigationManager navigation,
    AuthTokenStore tokenStore,
    ClientTokenRefresher tokenRefresher,
    ILogger<NotificationsRealtimeClient> logger) : IAsyncDisposable
{
    public const string HubPath = "/hubs/notifications";
    public const string ClientMethod = "notification";

    private static readonly TimeSpan[] ReconnectDelays =
    [
        TimeSpan.Zero,
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30)
    ];

    private readonly SemaphoreSlim lifecycleGate = new(1, 1);
    private HubConnection? connection;
    private CancellationTokenSource? sessionCts;
    private Task? connectionTask;
    private TaskCompletionSource? disconnectedSignal;

    public event Action<NotificationDto>? NotificationReceived;
    public event Action? Connected;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            if (connectionTask is { IsCompleted: false })
                return;

            sessionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connection = BuildConnection();
            connectionTask = RunConnectionLoopAsync(connection, sessionCts.Token);
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    public async Task StopAsync()
    {
        await lifecycleGate.WaitAsync();
        try
        {
            var currentConnection = connection;
            var currentCts = sessionCts;
            var currentTask = connectionTask;

            connection = null;
            sessionCts = null;
            connectionTask = null;

            if (currentCts is null)
                return;

            currentCts.Cancel();
            if (currentConnection is not null)
            {
                try
                {
                    await currentConnection.StopAsync();
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Notifications hub stop completed with an error.");
                }
            }

            if (currentTask is not null)
            {
                try
                {
                    await currentTask;
                }
                catch (OperationCanceledException) when (currentCts.IsCancellationRequested)
                {
                    // Expected during sign-out or component disposal.
                }
            }

            if (currentConnection is not null)
                await currentConnection.DisposeAsync();

            currentCts.Dispose();
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    private HubConnection BuildConnection()
    {
        var hubUri = navigation.ToAbsoluteUri(HubPath);
        var hub = new HubConnectionBuilder()
            .WithUrl(hubUri, options => options.AccessTokenProvider = GetAccessTokenAsync)
            .WithAutomaticReconnect(ReconnectDelays)
            .Build();

        hub.On<NotificationDto>(ClientMethod, notification => NotificationReceived?.Invoke(notification));
        hub.Reconnected += _ =>
        {
            Connected?.Invoke();
            return Task.CompletedTask;
        };
        hub.Closed += OnClosedAsync;
        return hub;
    }

    private async Task RunConnectionLoopAsync(HubConnection hub, CancellationToken cancellationToken)
    {
        var retryDelay = TimeSpan.FromSeconds(2);

        while (!cancellationToken.IsCancellationRequested)
        {
            var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Interlocked.Exchange(ref disconnectedSignal, signal);

            try
            {
                await hub.StartAsync(cancellationToken);
                Connected?.Invoke();
                retryDelay = TimeSpan.FromSeconds(2);
                await signal.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Notifications hub is unavailable; live delivery will retry while the inbox remains available.");
            }

            if (cancellationToken.IsCancellationRequested)
                break;

            await Task.Delay(retryDelay, cancellationToken);
            retryDelay = TimeSpan.FromSeconds(Math.Min(retryDelay.TotalSeconds * 2, 30));
        }
    }

    private Task OnClosedAsync(Exception? exception)
    {
        if (exception is not null)
            logger.LogDebug(exception, "Notifications hub connection closed after automatic reconnect attempts.");

        Volatile.Read(ref disconnectedSignal)?.TrySetResult();
        return Task.CompletedTask;
    }

    private async Task<string?> GetAccessTokenAsync()
    {
        var cancellationToken = sessionCts?.Token ?? CancellationToken.None;
        if (cancellationToken.IsCancellationRequested)
            return null;

        if (!string.IsNullOrWhiteSpace(tokenStore.AccessToken) &&
            tokenStore.ExpiresAtUtc is { } expiry &&
            expiry <= DateTimeOffset.UtcNow.AddMinutes(1))
        {
            await tokenRefresher.TryRefreshAsync(cancellationToken);
        }

        return tokenStore.AccessToken;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        lifecycleGate.Dispose();
    }
}
