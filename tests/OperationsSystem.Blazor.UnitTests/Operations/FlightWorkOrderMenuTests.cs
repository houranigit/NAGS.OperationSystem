using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.Auth;
using OperationsSystem.Blazor.Client.Features.Operations.Components;
using OperationsSystem.Blazor.Client.State;
using Radzen;
using Shouldly;

namespace OperationsSystem.Blazor.UnitTests.Operations;

public sealed class FlightWorkOrderMenuTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid FlightId = Guid.NewGuid();

    [Fact]
    public async Task A_flight_without_orders_does_not_show_an_empty_submenu()
    {
        var runtime = new FlightRuntime([]);

        var html = await RenderAsync(runtime);

        html.ShouldNotContain("<details");
        runtime.FlightRequests.ShouldBe(1);
    }

    [Fact]
    public async Task Single_order_uses_the_shared_actions_directly_inside_the_work_order_submenu()
    {
        var runtime = new FlightRuntime([Order("Approved", "WO-0042")]);

        var html = await RenderAsync(runtime);

        html.ShouldContain("<span>Work order</span>");
        html.ShouldContain("WO-0042");
        html.ShouldContain("<span>View</span>");
        html.ShouldContain("<span>Return</span>");
        html.ShouldNotContain("<span>Edit</span>");
        html.ShouldNotContain("<span>Delete</span>");
        CountDetails(html).ShouldBe(1);
    }

    [Fact]
    public async Task Multiple_orders_have_separate_identified_submenus_and_state_specific_actions()
    {
        var submitted = Order("Submitted", null);
        var approved = Order("Approved", "WO-0042");
        var runtime = new FlightRuntime([submitted, approved]);

        var html = await RenderAsync(runtime);

        CountDetails(html).ShouldBe(3);
        html.ShouldContain(submitted.Id.ToString("N")[..8].ToUpperInvariant());
        html.ShouldContain("WO-0042");
        html.ShouldContain("<span>Edit</span>");
        html.ShouldContain("<span>Approve</span>");
        html.ShouldContain("<span>Merge work orders</span>");
        html.ShouldContain("<span>Delete</span>");
        html.ShouldContain("<span>Return</span>");
    }

    [Fact]
    public async Task Missing_view_permission_does_not_load_work_order_data()
    {
        var runtime = new FlightRuntime([Order("Submitted", null)]) { Permissions = [] };

        var html = await RenderAsync(runtime);

        html.ShouldNotContain("<details");
        runtime.FlightRequests.ShouldBe(0);
    }

    [Fact]
    public async Task A_network_failure_offers_retry_without_closing_the_flight_menu()
    {
        var runtime = new FlightRuntime([]) { FailFlightRequest = true };

        var html = await RenderAsync(runtime);

        html.ShouldContain("Retry loading work orders");
        html.ShouldNotContain("<details");
    }

    private static async Task<string> RenderAsync(FlightRuntime runtime)
    {
        var tokens = new AuthTokenStore();
        var locale = new LocaleState(runtime);
        var refresher = new ClientTokenRefresher(runtime, tokens, locale);
        var api = new BrowserApiClient(runtime, tokens, locale, refresher);
        var auth = new AuthSession(api, tokens, refresher);
        await auth.InitializeAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IJSRuntime>(runtime);
        services.AddSingleton<NavigationManager>(new TestNavigationManager());
        services.AddSingleton(auth);
        services.AddSingleton(new OperationsApiClient(api));
        services.AddScoped<DialogService>();
        services.AddScoped<ContextMenuService>();
        services.AddScoped<NotificationService>();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = await renderer.RenderComponentAsync<FlightWorkOrderMenu>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(FlightWorkOrderMenu.FlightId)] = FlightId }));
            return rendered.ToHtmlString();
        });
    }

    private static int CountDetails(string html) => html.Split("<details", StringSplitOptions.None).Length - 1;

    private static WorkOrderSummaryModel Order(string status, string? approvalNumber) =>
        new(Guid.NewGuid(), FlightId, "Completion", status, approvalNumber, Owner, "Owner", "row-version");

    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager() => Initialize("http://localhost/", "http://localhost/operations/flights");
    }

    private sealed class FlightRuntime(IReadOnlyList<WorkOrderSummaryModel> orders) : IJSRuntime
    {
        public IReadOnlyList<string> Permissions { get; init; } =
        [
            OperationsPermissions.WorkOrdersView,
            OperationsPermissions.WorkOrdersAuthor,
            OperationsPermissions.WorkOrdersApprove,
            OperationsPermissions.WorkOrdersMerge
        ];
        public bool FailFlightRequest { get; init; }
        public int FlightRequests { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            identifier.ShouldBe("operationsSystem.api.request");
            var path = (string)args![1]!;
            string response;
            if (path == "/identity/auth/refresh")
            {
                response = JsonSerializer.Serialize(new AccessTokenResponse("fake-access-token", DateTimeOffset.UtcNow.AddMinutes(10)));
            }
            else if (path == "/identity/me")
            {
                response = JsonSerializer.Serialize(new AuthenticatedUser(Owner, "owner@example.test", "Owner", Guid.NewGuid(),
                    "Operations", UserTypes.SystemAdministrator, null, "Direct", false, false, Permissions));
            }
            else
            {
                args[0].ShouldBe("GET");
                path.ShouldBe($"/operations/flights/{FlightId}");
                FlightRequests++;
                if (FailFlightRequest)
                    throw new JSException("TypeError: Failed to fetch");
                response = JsonSerializer.Serialize(new { Id = FlightId, WorkOrders = orders });
            }

            return ValueTask.FromResult((TValue)(object)response);
        }
    }
}
