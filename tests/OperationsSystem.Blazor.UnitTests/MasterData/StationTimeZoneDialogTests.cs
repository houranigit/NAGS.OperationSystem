using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using OperationsSystem.Blazor.Client;
using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.Auth;
using OperationsSystem.Blazor.Client.Features.Operations.Components;
using OperationsSystem.Blazor.Client.Features.Stations.Components;
using Radzen;
using Shouldly;

namespace OperationsSystem.Blazor.UnitTests.MasterData;

public sealed class StationTimeZoneDialogTests
{
    [Fact]
    public async Task Edit_renders_the_saved_zone_in_a_combobox_using_server_options()
    {
        var runtime = new StationRuntime();
        await using var services = CreateServices(runtime);
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());

        var component = await renderer.Dispatcher.InvokeAsync(() => renderer.BeginRenderingComponent<StationFormDialog>(
            ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(StationFormDialog.StationId)] = runtime.Id })));
        await component.QuiescenceTask;
        var html = await renderer.Dispatcher.InvokeAsync(component.ToHtmlString);

        runtime.TimeZoneRequests.ShouldBe(1);
        var fieldIndex = html.IndexOf("id=\"station-time-zone\"", StringComparison.Ordinal);
        fieldIndex.ShouldBeGreaterThanOrEqualTo(0);
        var tag = html[html.LastIndexOf('<', fieldIndex)..(html.IndexOf('>', fieldIndex) + 1)];
        tag.ShouldContain("role=\"combobox\"");
        html.ShouldContain("Riyadh (Asia/Riyadh)");
    }

    [Fact]
    public async Task Creating_sends_the_selected_server_time_zone()
    {
        var runtime = new StationRuntime();
        await using var services = CreateServices(runtime);
        var dialog = CreateDialog(services);
        await InvokeAsync(dialog, "OnInitializedAsync");
        SetRequiredFields(dialog, runtime.Id);
        SetModel(dialog, "TimeZoneId", "America/Chicago");

        await InvokeAsync(dialog, "SubmitAsync");

        runtime.TimeZoneRequests.ShouldBe(1);
        runtime.SavedTimeZoneId.ShouldBe("America/Chicago");
    }

    [Fact]
    public async Task Editing_preserves_a_valid_stored_alias_missing_from_the_canonical_options()
    {
        var runtime = new StationRuntime { SavedZone = "US/Eastern" };
        await using var services = CreateServices(runtime);
        var dialog = CreateDialog(services, runtime.Id);
        await InvokeAsync(dialog, "OnInitializedAsync");

        await InvokeAsync(dialog, "SubmitAsync");

        runtime.SavedTimeZoneId.ShouldBe("US/Eastern");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Unknown/Station")]
    public async Task Creation_requires_a_time_zone_from_the_available_options(string? timeZoneId)
    {
        var runtime = new StationRuntime();
        await using var services = CreateServices(runtime);
        var dialog = CreateDialog(services);
        await InvokeAsync(dialog, "OnInitializedAsync");
        SetRequiredFields(dialog, runtime.Id);
        SetModel(dialog, "TimeZoneId", timeZoneId);

        await InvokeAsync(dialog, "SubmitAsync");

        runtime.SavedTimeZoneId.ShouldBeNull();
        ReadField<string?>(dialog, "errorMessage").ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Scheduling_uses_the_selected_station_without_time_zone_captions_or_configuration_prompts()
    {
        var runtime = new StationRuntime();
        await using var services = CreateServices(runtime);
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());

        var component = await renderer.Dispatcher.InvokeAsync(() => renderer.BeginRenderingComponent<ScheduleFlightDialog>());
        await component.QuiescenceTask;
        var html = await renderer.Dispatcher.InvokeAsync(component.ToHtmlString);

        html.ShouldContain("Scheduled arrival");
        html.ShouldContain("Scheduled departure");
        html.ShouldNotContain("Configure");
        html.ShouldNotContain("Asia/Riyadh");
        html.ShouldNotContain("Scheduled arrival (");
    }

    private static ServiceProvider CreateServices(StationRuntime runtime) => new ServiceCollection()
        .AddLogging()
        .AddSingleton<IJSRuntime>(runtime)
        .AddSingleton<NavigationManager>(new TestNavigationManager())
        .AddPortalClientServices()
        .BuildServiceProvider();

    private static StationFormDialog CreateDialog(IServiceProvider services, Guid? stationId = null)
    {
        var dialog = new StationFormDialog();
        typeof(StationFormDialog).GetProperty(nameof(StationFormDialog.StationId))!.SetValue(dialog, stationId);
        Inject("MasterData", services.GetRequiredService<MasterDataApiClient>());
        Inject("Identity", services.GetRequiredService<IdentityApiClient>());
        Inject("Auth", services.GetRequiredService<AuthSession>());
        Inject("DialogService", services.GetRequiredService<DialogService>());
        return dialog;

        void Inject(string name, object value) => typeof(StationFormDialog)
            .GetProperty(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(dialog, value);
    }

    private static Task InvokeAsync(StationFormDialog dialog, string method) =>
        (Task)typeof(StationFormDialog).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(dialog, null)!;

    private static T ReadField<T>(StationFormDialog dialog, string field) =>
        (T)typeof(StationFormDialog).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(dialog)!;

    private static void SetModel(StationFormDialog dialog, string property, object? value)
    {
        var model = ReadField<object>(dialog, "model");
        model.GetType().GetProperty(property)!.SetValue(model, value);
    }

    private static void SetRequiredFields(StationFormDialog dialog, Guid countryId)
    {
        SetModel(dialog, "IataCode", "MED");
        SetModel(dialog, "Name", "Medina");
        SetModel(dialog, "CountryId", countryId);
    }

    private sealed class StationRuntime : IJSRuntime
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        public Guid Id { get; } = Guid.NewGuid();
        public string SavedZone { get; init; } = "Asia/Riyadh";
        public int TimeZoneRequests { get; private set; }
        public string? SavedTimeZoneId { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            identifier.ShouldBe("operationsSystem.api.request");
            var method = (string)args![0]!;
            var path = (string)args[1]!;
            string response;
            if (method != "GET")
            {
                SavedTimeZoneId = (string)args[2]!.GetType().GetProperty("TimeZoneId")!.GetValue(args[2])!;
                response = method == "POST" ? JsonSerializer.Serialize(Id) : string.Empty;
            }
            else if (path == "/masterdata/stations/time-zones")
            {
                TimeZoneRequests++;
                response = JsonSerializer.Serialize(new[]
                {
                    new StationTimeZoneOption("Asia/Riyadh", "Riyadh (Asia/Riyadh)"),
                    new StationTimeZoneOption("America/Chicago", "Chicago (America/Chicago)")
                }, JsonOptions);
            }
            else if (path == $"/masterdata/stations/{Id}")
            {
                response = JsonSerializer.Serialize(new StationDetail(Id, "MED", null, "Medina", "Medina", Id,
                    "Saudi Arabia", true, DateTimeOffset.UtcNow, null, "saved-version", SavedZone), JsonOptions);
            }
            else if (path == "/masterdata/stations/options")
            {
                response = JsonSerializer.Serialize(new[] { new StationOption(Id, "MED", "Medina", "Asia/Riyadh") }, JsonOptions);
            }
            else
            {
                path.ShouldContain("/options");
                response = "[]";
            }

            return ValueTask.FromResult((TValue)(object)response);
        }
    }

    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager() => Initialize("http://localhost/", "http://localhost/");
        protected override void NavigateToCore(string uri, bool forceLoad) { }
    }
}
