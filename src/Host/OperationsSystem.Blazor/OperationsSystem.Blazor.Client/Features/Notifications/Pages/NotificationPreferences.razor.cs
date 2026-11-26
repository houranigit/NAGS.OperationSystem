using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.Auth;
using OperationsSystem.Blazor.Client.Localization;

namespace OperationsSystem.Blazor.Client.Features.Notifications.Pages;

public partial class NotificationPreferences : IAsyncDisposable
{
    [Inject] private AuthSession Auth { get; set; } = default!;
    [Inject] private NotificationsApiClient Api { get; set; } = default!;
    [Inject] private ILogger<NotificationPreferences> Logger { get; set; } = default!;

    private const string WorkOrderEmailKey = "workOrderEmail";
    private NotificationPreferencesDto? preferences;
    private bool workOrderEmailLoaded;
    private string? workOrderEmailError;
    private bool isLoading = true;
    private bool saved;
    private string? savingKey;
    private PreferenceChange? failedChange;
    private Guid? attemptedUserId;
    private long operationVersion;
    private readonly CancellationTokenSource lifetime = new();

    private static readonly PreferenceGroup[] Groups =
    [
        new("Flights",
        [
            new("flightAssignments", "Flight assignments", "When a flight is assigned to you.", value => value.FlightAssignments),
            new("flightInvitations", "Flight invitations", "When a teammate invites you to a flight.", value => value.FlightInvitations),
            new("scheduleChanges", "Schedule changes", "When flights are added to or changed in your schedule, including bulk scheduling.", value => value.ScheduleChanges),
            new("colleagueEdits", "Changes to my input", "When another assigned employee changes or removes a record you submitted.", value => value.ColleagueEdits),
            new("flightReminders", "Flight reminders", "Reminders before the scheduled arrival of your assigned flights.", value => value.FlightReminders)
        ]),
        new("Shared flights",
        [
            new("directShares", "Shared directly with me", "When someone shares a flight directly with you.", value => value.DirectShares),
            new("stationShares", "Shared with my station", "When someone shares a flight with a station you belong to.", value => value.StationShares)
        ]),
        new("Saved work",
        [
            new("syncIssues", "Sync failures and review", "When an Android record fails to sync or needs your review after reconnecting.", value => value.SyncIssues)
        ])
    ];

    protected override void OnInitialized() => Auth.StateChanged += OnAuthStateChanged;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (Auth.User is { } user && attemptedUserId != user.Id)
            await LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (Auth.User is not { } user || savingKey is not null)
            return;

        var version = ++operationVersion;
        attemptedUserId = user.Id;
        isLoading = true;
        workOrderEmailLoaded = false;
        workOrderEmailError = null;
        saved = false;
        failedChange = null;
        StateHasChanged();
        try
        {
            await Task.WhenAll(LoadNotificationPreferencesAsync(user.Id, version), LoadWorkOrderEmailPreferenceAsync(user.Id, version));
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        finally
        {
            if (IsCurrent(user.Id, version))
            {
                isLoading = false;
                StateHasChanged();
            }
        }
    }

    private async Task LoadNotificationPreferencesAsync(Guid userId, long version)
    {
        try
        {
            var loaded = await Api.GetPreferencesAsync(lifetime.Token);
            if (IsCurrent(userId, version)) preferences = loaded;
        }
        catch (Exception exception) when ((exception is ApiException or JSException or OperationCanceledException) && !lifetime.IsCancellationRequested)
        {
            if (IsCurrent(userId, version)) preferences = null;
            Logger.LogWarning(exception, "Could not load notification preferences");
        }
    }

    private async Task LoadWorkOrderEmailPreferenceAsync(Guid userId, long version)
    {
        try
        {
            var profile = await Auth.RefreshProfileAsync(lifetime.Token);
            if (IsCurrent(userId, version)) workOrderEmailLoaded = profile?.Id == userId;
        }
        catch (Exception exception) when ((exception is ApiException or JSException or OperationCanceledException) && !lifetime.IsCancellationRequested)
        {
            if (IsCurrent(userId, version)) workOrderEmailError = UiStrings.Notifications.EmailPreferenceLoadFailed;
            Logger.LogWarning(exception, "Could not load the work order email preference");
        }
    }

    private async Task SaveAsync(string key, bool enabled)
    {
        var isWorkOrderEmail = key == WorkOrderEmailKey;
        if (Auth.User is not { } user || isLoading || savingKey is not null ||
            (isWorkOrderEmail ? !workOrderEmailLoaded || workOrderEmailError is not null : preferences is null))
            return;

        var version = ++operationVersion;
        savingKey = key;
        saved = false;
        failedChange = null;
        try
        {
            if (isWorkOrderEmail)
            {
                await Auth.UpdateWorkOrderEmailPreferenceAsync(enabled, lifetime.Token);
            }
            else
            {
                var updated = await Api.UpdatePreferenceAsync(key, enabled, lifetime.Token);
                if (IsCurrent(user.Id, version)) preferences = updated;
            }
            if (IsCurrent(user.Id, version)) saved = true;
        }
        catch (Exception exception) when ((exception is ApiException or JSException or OperationCanceledException) && !lifetime.IsCancellationRequested)
        {
            if (IsCurrent(user.Id, version))
            {
                if (isWorkOrderEmail)
                    workOrderEmailError = UiStrings.Notifications.EmailPreferenceSaveUnconfirmed;
                else
                    failedChange = new(key, enabled);
            }
            Logger.LogWarning(exception, "Could not save notification preference");
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        finally
        {
            if (IsCurrent(user.Id, version)) savingKey = null;
        }
    }

    private Task RetrySaveAsync() => failedChange is { } change ? SaveAsync(change.Key, change.Enabled) : Task.CompletedTask;

    private bool IsCurrent(Guid userId, long version) => !lifetime.IsCancellationRequested
        && Auth.User?.Id == userId && operationVersion == version;

    private async void OnAuthStateChanged()
    {
        try
        {
            await InvokeAsync(() =>
            {
                if (Auth.User?.Id != attemptedUserId)
                {
                    operationVersion++;
                    attemptedUserId = null;
                    preferences = null;
                    workOrderEmailLoaded = false;
                    workOrderEmailError = null;
                    isLoading = true;
                    savingKey = null;
                    failedChange = null;
                    saved = false;
                }
                StateHasChanged();
            });
        }
        catch (Exception exception)
        {
            await DispatchExceptionAsync(exception);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Auth.StateChanged -= OnAuthStateChanged;
        await lifetime.CancelAsync();
        lifetime.Dispose();
    }

    private sealed record PreferenceGroup(string Title, PreferenceOption[] Options);
    private sealed record PreferenceOption(string Key, string Title, string Description, Func<NotificationPreferencesDto, bool> Value);
    private sealed record PreferenceChange(string Key, bool Enabled);
}
