using System.Text.Json;
using Microsoft.JSInterop;
using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.Auth;
using OperationsSystem.Blazor.Client.State;
using Shouldly;

namespace OperationsSystem.Blazor.UnitTests.Auth;

public sealed class WorkOrderEmailPreferenceTests
{
    [Fact]
    public async Task Profile_loads_server_preference_and_saves_both_toggle_values_to_self_endpoint()
    {
        var runtime = new PreferenceRuntime(true);
        var session = CreateSession(runtime);
        await session.InitializeAsync();
        session.User!.ReceiveWorkOrderSubmissionEmails.ShouldBeTrue();

        await session.UpdateWorkOrderEmailPreferenceAsync(false);

        session.User.ReceiveWorkOrderSubmissionEmails.ShouldBeFalse();
        runtime.SavedValues.ShouldBe([false]);
        await session.UpdateWorkOrderEmailPreferenceAsync(true);
        session.User.ReceiveWorkOrderSubmissionEmails.ShouldBeTrue();
        runtime.SavedValues.ShouldBe([false, true]);
    }

    [Fact]
    public async Task Failed_save_keeps_the_last_confirmed_preference()
    {
        var runtime = new PreferenceRuntime(false) { RejectSave = true };
        var session = CreateSession(runtime);
        await session.InitializeAsync();

        await Should.ThrowAsync<ApiException>(() => session.UpdateWorkOrderEmailPreferenceAsync(true));

        session.User!.ReceiveWorkOrderSubmissionEmails.ShouldBeFalse();
    }

    [Fact]
    public async Task Pending_save_does_not_change_the_confirmed_switch_value()
    {
        var runtime = new PreferenceRuntime(false) { SaveCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var session = CreateSession(runtime);
        await session.InitializeAsync();

        var pending = session.UpdateWorkOrderEmailPreferenceAsync(true);
        pending.IsCompleted.ShouldBeFalse();
        session.User!.ReceiveWorkOrderSubmissionEmails.ShouldBeFalse();
        runtime.SaveCompletion.SetResult(string.Empty);
        await pending;

        session.User.ReceiveWorkOrderSubmissionEmails.ShouldBeTrue();
    }

    [Fact]
    public async Task Completing_save_after_logout_does_not_restore_the_old_session()
    {
        var runtime = new PreferenceRuntime(false) { SaveCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var session = CreateSession(runtime);
        await session.InitializeAsync();

        var pending = session.UpdateWorkOrderEmailPreferenceAsync(true);
        await session.LogoutAsync();
        runtime.SaveCompletion.SetResult(string.Empty);
        await pending;

        session.Status.ShouldBe(AuthStatus.Anonymous);
        session.User.ShouldBeNull();
    }

    [Fact]
    public async Task Opening_profile_refreshes_a_preference_changed_on_mobile()
    {
        var runtime = new PreferenceRuntime(false);
        var session = CreateSession(runtime);
        await session.InitializeAsync();
        runtime.ServerProfile = runtime.ServerProfile with { ReceiveWorkOrderSubmissionEmails = true };

        var refreshed = await session.RefreshProfileAsync();

        refreshed!.ReceiveWorkOrderSubmissionEmails.ShouldBeTrue();
        session.User!.ReceiveWorkOrderSubmissionEmails.ShouldBeTrue();
        runtime.RefreshCalls.ShouldBe(1); // Profile refresh does not rotate the credential session.
    }

    [Fact]
    public async Task Failed_profile_refresh_does_not_replace_the_last_known_user()
    {
        var runtime = new PreferenceRuntime(true);
        var session = CreateSession(runtime);
        await session.InitializeAsync();
        runtime.RejectProfileRead = true;

        await Should.ThrowAsync<ApiException>(() => session.RefreshProfileAsync());

        session.User!.ReceiveWorkOrderSubmissionEmails.ShouldBeTrue();
    }

    [Fact]
    public async Task Raw_browser_network_failure_during_profile_refresh_preserves_the_last_known_value()
    {
        var runtime = new PreferenceRuntime(true);
        var session = CreateSession(runtime);
        await session.InitializeAsync();
        runtime.ProfileNetworkFailure = true;

        await Should.ThrowAsync<JSException>(() => session.RefreshProfileAsync());

        session.User!.ReceiveWorkOrderSubmissionEmails.ShouldBeTrue();
        runtime.ProfileNetworkFailure = false;
        runtime.ServerProfile = runtime.ServerProfile with { ReceiveWorkOrderSubmissionEmails = false };
        (await session.RefreshProfileAsync())!.ReceiveWorkOrderSubmissionEmails.ShouldBeFalse();
    }

    [Fact]
    public async Task Raw_browser_network_failure_during_save_preserves_value_until_a_fresh_read_confirms_the_write()
    {
        var runtime = new PreferenceRuntime(false);
        var session = CreateSession(runtime);
        await session.InitializeAsync();
        runtime.SaveNetworkFailure = true;

        await Should.ThrowAsync<JSException>(() => session.UpdateWorkOrderEmailPreferenceAsync(true));

        session.User!.ReceiveWorkOrderSubmissionEmails.ShouldBeFalse();
        // The write may have reached the server even though the browser lost its response.
        runtime.ServerProfile = runtime.ServerProfile with { ReceiveWorkOrderSubmissionEmails = true };
        (await session.RefreshProfileAsync())!.ReceiveWorkOrderSubmissionEmails.ShouldBeTrue();
    }

    [Fact]
    public async Task Old_profile_response_does_not_overwrite_a_newly_signed_in_account()
    {
        var runtime = new PreferenceRuntime(true);
        var session = CreateSession(runtime);
        await session.InitializeAsync();
        var oldProfile = runtime.ServerProfile;
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.ProfileCompletion = completion;
        var pending = session.RefreshProfileAsync();
        pending.IsCompleted.ShouldBeFalse();

        await session.LogoutAsync();
        runtime.ServerProfile = runtime.ServerProfile with { Id = Guid.NewGuid(), ReceiveWorkOrderSubmissionEmails = false };
        await session.InitializeAsync();
        completion.SetResult(JsonSerializer.Serialize(oldProfile));
        (await pending).ShouldBeNull();

        session.User!.Id.ShouldBe(runtime.ServerProfile.Id);
        session.User.ReceiveWorkOrderSubmissionEmails.ShouldBeFalse();
    }

    private static AuthSession CreateSession(IJSRuntime runtime)
    {
        var tokens = new AuthTokenStore();
        var locale = new LocaleState(runtime);
        var refresher = new ClientTokenRefresher(runtime, tokens, locale);
        return new AuthSession(new BrowserApiClient(runtime, tokens, locale, refresher), tokens, refresher);
    }

    private sealed class PreferenceRuntime(bool enabled) : IJSRuntime
    {
        public AuthenticatedUser ServerProfile { get; set; } = new(Guid.NewGuid(), "employee@example.com", "Employee",
            Guid.NewGuid(), "Staff", UserTypes.StationStaff, Guid.NewGuid(), "MasterData", false, false, [], enabled);
        public bool RejectSave { get; init; }
        public bool RejectProfileRead { get; set; }
        public bool ProfileNetworkFailure { get; set; }
        public bool SaveNetworkFailure { get; set; }
        public int RefreshCalls { get; private set; }
        public TaskCompletionSource<string>? ProfileCompletion { get; set; }
        public TaskCompletionSource<string>? SaveCompletion { get; init; }
        public List<bool> SavedValues { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            identifier.ShouldBe("operationsSystem.api.request");
            var method = (string)args![0]!;
            var path = (string)args[1]!;
            string response;
            switch (path)
            {
                case "/identity/auth/refresh":
                    RefreshCalls++;
                    response = JsonSerializer.Serialize(new AccessTokenResponse("token", DateTimeOffset.UtcNow.AddMinutes(15)));
                    break;
                case "/identity/me":
                    if (ProfileNetworkFailure)
                        throw new JSException("TypeError: Failed to fetch");
                    if (RejectProfileRead)
                        throw new JSException(JsonSerializer.Serialize(new { status = 500, body = "" }));
                    var profileCompletion = ProfileCompletion;
                    ProfileCompletion = null;
                    response = profileCompletion is null ? JsonSerializer.Serialize(ServerProfile) : await profileCompletion.Task;
                    break;
                case "/identity/me/work-order-email-preference":
                    method.ShouldBe("PUT");
                    SavedValues.Add(((UpdateWorkOrderEmailPreferenceRequest)args[2]!).Enabled);
                    if (SaveNetworkFailure)
                        throw new JSException("TypeError: Failed to fetch");
                    if (RejectSave)
                        throw new JSException(JsonSerializer.Serialize(new { status = 500, body = "" }));
                    response = SaveCompletion is null ? string.Empty : await SaveCompletion.Task;
                    break;
                case "/identity/auth/logout":
                    response = string.Empty;
                    break;
                default:
                    throw new InvalidOperationException($"Unexpected path {path}.");
            }

            return (TValue)(object)response;
        }
    }
}
