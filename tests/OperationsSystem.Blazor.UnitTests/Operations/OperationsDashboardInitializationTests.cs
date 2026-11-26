using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.Auth;
using OperationsSystem.Blazor.Client.Features.Operations;
using OperationsSystem.Blazor.Client.Features.Operations.Pages;
using OperationsSystem.Blazor.Client.State;
using Radzen;
using Shouldly;

namespace OperationsSystem.Blazor.UnitTests.Operations;

public sealed class OperationsDashboardInitializationTests
{
    [Fact]
    public async Task First_dashboard_request_waits_for_browser_zone_and_uses_its_local_day()
    {
        var runtime = new DashboardRuntime();
        var tokens = new AuthTokenStore();
        var locale = new LocaleState(runtime);
        var refresher = new ClientTokenRefresher(runtime, tokens, locale);
        var api = new BrowserApiClient(runtime, tokens, locale, refresher);
        var auth = new AuthSession(api, tokens, refresher);
        await auth.InitializeAsync();
        var timeZone = new UserTimeZone(runtime);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IJSRuntime>(runtime);
        services.AddSingleton<NavigationManager>(new DashboardNavigationManager());
        services.AddSingleton(tokens);
        services.AddSingleton(refresher);
        services.AddSingleton(auth);
        services.AddSingleton(timeZone);
        services.AddSingleton(new GridPreferences(runtime));
        services.AddSingleton(new OperationsApiClient(api));
        services.AddSingleton<OperationsDashboardRealtimeClient>();
        services.AddScoped<NotificationService>();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<DashboardProbe>());
        await runtime.ZoneRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));

        runtime.DashboardRequested.Task.IsCompleted.ShouldBeFalse();

        await renderer.Dispatcher.InvokeAsync(() => runtime.AllowZone.TrySetResult());
        var request = await runtime.DashboardRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var query = QueryHelpers.ParseQuery(new Uri("http://localhost" + request.Path).Query);
        var expectedDate = timeZone.ToLocal(request.RequestedAtUtc).Date;

        query["timeZoneId"].ToString().ShouldBe("America/Chicago");
        DateTimeOffset.Parse(query["fromUtc"]!, CultureInfo.InvariantCulture).ShouldBe(
            timeZone.DateBoundaryUtc(expectedDate, endOfDay: false));
        DateTimeOffset.Parse(query["toUtc"]!, CultureInfo.InvariantCulture).ShouldBe(
            timeZone.DateBoundaryUtc(expectedDate, endOfDay: true).AddTicks(1));
    }

    // Exercise the page lifecycle and requests without rendering unrelated charts or data-grid internals.
    private sealed class DashboardProbe : OperationsDashboardPage
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder) { }
    }

    private sealed class DashboardNavigationManager : NavigationManager
    {
        public DashboardNavigationManager() => Initialize("http://localhost:1/", "http://localhost:1/operations/dashboard");
    }

    private sealed record DashboardRequest(string Path, DateTimeOffset RequestedAtUtc);

    private sealed class DashboardRuntime : IJSRuntime
    {
        public TaskCompletionSource ZoneRequested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowZone { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<DashboardRequest> DashboardRequested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (identifier == "operationsSystem.timeZone.get")
            {
                ZoneRequested.TrySetResult();
                await AllowZone.Task.WaitAsync(cancellationToken);
                return JsonSerializer.Deserialize<TValue>("{\"Id\":\"America/Chicago\",\"OffsetMinutes\":-300}")!;
            }
            if (identifier == "operationsSystem.storage.get")
                return default!;

            identifier.ShouldBe("operationsSystem.api.request");
            var path = (string)args![1]!;
            string response;
            if (path == "/identity/auth/refresh")
                response = JsonSerializer.Serialize(new AccessTokenResponse("fake-token", DateTimeOffset.UtcNow.AddMinutes(10)));
            else if (path == "/identity/me")
                response = JsonSerializer.Serialize(new AuthenticatedUser(Guid.NewGuid(), "viewer@example.test", "Viewer", Guid.NewGuid(),
                    "Operations", UserTypes.ViewerOnly, null, "Direct", false, false, [OperationsPermissions.DashboardAnalyticsView]));
            else
            {
                path.ShouldStartWith("/operations/analytics-dashboard?");
                DashboardRequested.TrySetResult(new(path, DateTimeOffset.UtcNow));
                // Component disposal cancels the pending request before starting any real-time transport.
                await Task.Delay(Timeout.Infinite, cancellationToken);
                throw new InvalidOperationException("The pending request should have been cancelled.");
            }
            return (TValue)(object)response;
        }
    }
}
