using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using OperationsSystem.Blazor.Client.Auth;

namespace OperationsSystem.Blazor.Client.Features.Operations;

/// <summary>
/// Owns the authenticated, reconnecting SignalR channel for operations-dashboard invalidations.
/// Signals carry no dashboard data; consumers re-query the authoritative REST projections.
/// </summary>
public sealed class OperationsDashboardRealtimeClient(
    NavigationManager navigation,
    AuthTokenStore tokenStore,
    ClientTokenRefresher tokenRefresher,
    ILogger<OperationsDashboardRealtimeClient> logger) : IAsyncDisposable
{
    public const string HubPath = "/hubs/operations-dashboard";
    public const string ClientMethod = "dashboardChanged";

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
    private int isConnected;

    public event Action? DashboardChanged;
    public event Action<bool>? ConnectionStateChanged;

    public bool IsConnected => Volatile.Read(ref isConnected) == 1;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            if (connectionTask is { IsCompleted: false })
                return;

            cancellationToken.ThrowIfCancellationRequested();
            sessionCts = new CancellationTokenSource();
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
            SetConnectionState(false);

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
                    logger.LogDebug(ex, "Operations dashboard hub stop completed with an error.");
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

        hub.On(ClientMethod, () => DashboardChanged?.Invoke());
        hub.Reconnecting += _ =>
        {
            SetConnectionState(false);
            return Task.CompletedTask;
        };
        hub.Reconnected += _ =>
        {
            SetConnectionState(true);
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
                SetConnectionState(true);
                retryDelay = TimeSpan.FromSeconds(2);
                await signal.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Operations dashboard hub is unavailable; live invalidations will retry while REST data remains available.");
            }

            if (cancellationToken.IsCancellationRequested)
                break;

            await Task.Delay(retryDelay, cancellationToken);
            retryDelay = TimeSpan.FromSeconds(Math.Min(retryDelay.TotalSeconds * 2, 30));
        }
    }

    private Task OnClosedAsync(Exception? exception)
    {
        SetConnectionState(false);

        if (exception is not null)
            logger.LogDebug(exception, "Operations dashboard hub connection closed after automatic reconnect attempts.");

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

    private void SetConnectionState(bool connected)
    {
        var next = connected ? 1 : 0;
        if (Interlocked.Exchange(ref isConnected, next) != next)
            ConnectionStateChanged?.Invoke(connected);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        lifecycleGate.Dispose();
    }
}
