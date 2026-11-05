using System.Reflection;
using System.Text.Json;
using Microsoft.JSInterop;
using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.Auth;
using OperationsSystem.Blazor.Client.Features.Operations.Components;
using OperationsSystem.Blazor.Client.State;
using Shouldly;

namespace OperationsSystem.Blazor.UnitTests.Operations;

public sealed class FlightNumberDialogTests
{
    [Theory]
    [InlineData(false, "XY", "123", "123")]
    [InlineData(true, "XY", "123", "123")]
    [InlineData(false, "XY", " xy123 ", "XY123")]
    [InlineData(true, "XY", " xy123 ", "XY123")]
    [InlineData(false, null, "123", "123")]
    public async Task Scheduling_accepts_suffix_or_full_number_for_server_prefixing(bool bulkMode, string? code, string input, string expected)
    {
        var (dialog, runtime) = CreateDialog(code, input, bulkMode);

        ValidateDetails(dialog).ShouldBeNull();
        await InvokeAsync(dialog, bulkMode ? "CreateBulkAsync" : "CreateAsync");

        runtime.ScheduledNumber.ShouldBe(expected);
    }

    [Theory]
    [InlineData("XY", "456", "XY456")]
    [InlineData("XY", " xy456 ", "XY456")]
    [InlineData(null, "456", "456")]
    [InlineData("  ", "456", "456")]
    public async Task Number_edits_apply_the_saved_customer_code_once(string? savedCode, string input, string expected)
    {
        var (dialog, runtime) = CreateDialog("ZZ", input);
        runtime.Detail = SetOriginal(dialog, savedCode, "XY123");

        await InvokeAsync(dialog, "UpdateAsync", runtime.Detail.Id);

        runtime.ChangedNumber.ShouldBe(expected);
        runtime.PlanningVersion.ShouldBe("refreshed-version");
    }

    [Theory]
    [InlineData("XY123", "XY123")]
    [InlineData("XY123", "123")]
    [InlineData("123", "123")]
    public async Task Planning_edits_skip_number_changes_when_the_result_is_unchanged(string savedNumber, string input)
    {
        var (dialog, runtime) = CreateDialog("ZZ", input);
        runtime.Detail = SetOriginal(dialog, "XY", savedNumber);

        await InvokeAsync(dialog, "UpdateAsync", runtime.Detail.Id);

        runtime.ChangedNumber.ShouldBeNull();
        runtime.DetailReads.ShouldBe(0);
        runtime.PlanningVersion.ShouldBe("saved-version");
    }

    private static (ScheduleFlightDialog, FlightRuntime) CreateDialog(string? code, string flightNumber, bool bulkMode = false)
    {
        var runtime = new FlightRuntime();
        var tokens = new AuthTokenStore();
        var locale = new LocaleState(runtime);
        var refresher = new ClientTokenRefresher(runtime, tokens, locale);
        var api = new BrowserApiClient(runtime, tokens, locale, refresher);
        var dialog = new ScheduleFlightDialog();
        typeof(ScheduleFlightDialog).GetProperty(nameof(ScheduleFlightDialog.BulkMode))!.SetValue(dialog, bulkMode);
        Inject("Operations", new OperationsApiClient(api));
        Inject("Auth", new AuthSession(api, tokens, refresher));
        Inject("UserTimeZone", new UserTimeZone(runtime));
        var customerId = Guid.NewGuid();
        SetField(dialog, "customers", new CustomerOption[] { new(customerId, code, "Test customer") });
        var model = typeof(ScheduleFlightDialog).GetField("model", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
        SetModel("CustomerId", customerId);
        SetModel("StationId", Guid.NewGuid());
        SetModel("OperationTypeId", Guid.NewGuid());
        SetModel("FlightNumber", flightNumber);
        var date = new DateTime(2026, 11, 5, 10, 0, 0, DateTimeKind.Unspecified);
        SetModel("ScheduledArrivalUtc", date);
        SetModel("ScheduledDepartureUtc", date.AddHours(2));
        SetModel("PeriodStartDate", date.Date);
        SetModel("PeriodEndDate", date.Date);
        SetModel("ScheduledArrivalTimeUtc", date);
        SetModel("ScheduledDepartureTimeUtc", date.AddHours(2));
        return (dialog, runtime);

        void SetModel(string name, object value) => model.GetType().GetProperty(name)!.SetValue(model, value);
        void Inject(string name, object value) =>
            typeof(ScheduleFlightDialog).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(dialog, value);
    }

    private static FlightDetail SetOriginal(ScheduleFlightDialog dialog, string? customerCode, string flightNumber)
    {
        var date = new DateTimeOffset(2026, 11, 5, 10, 0, 0, TimeSpan.Zero);
        var original = new FlightDetail(Guid.NewGuid(), flightNumber, Guid.NewGuid(), customerCode, "Test customer",
            Guid.NewGuid(), "MED", "Medina", Guid.NewGuid(), "Transit", null, null, date, date.AddHours(2), false,
            [], [], date, null, "saved-version");
        typeof(ScheduleFlightDialog).GetProperty(nameof(ScheduleFlightDialog.FlightId))!.SetValue(dialog, original.Id);
        SetField(dialog, "original", original);
        SetField(dialog, "rowVersion", original.RowVersion);
        return original;
    }

    private static string? ValidateDetails(ScheduleFlightDialog dialog) =>
        (string?)typeof(ScheduleFlightDialog).GetMethod("ValidateDetails", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dialog, null);

    private static Task InvokeAsync(ScheduleFlightDialog dialog, string method, params object[] arguments) =>
        (Task)typeof(ScheduleFlightDialog).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dialog, arguments)!;

    private static void SetField(ScheduleFlightDialog dialog, string name, object value) =>
        typeof(ScheduleFlightDialog).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(dialog, value);

    private sealed class FlightRuntime : IJSRuntime
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        public FlightDetail? Detail { get; set; }
        public string? ScheduledNumber { get; private set; }
        public string? ChangedNumber { get; private set; }
        public string? PlanningVersion { get; private set; }
        public int DetailReads { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            identifier.ShouldBe("operationsSystem.api.request");
            var method = (string)args![0]!;
            var path = (string)args[1]!;
            var response = string.Empty;
            if (method == "GET")
            {
                DetailReads++;
                Detail.ShouldNotBeNull();
                response = JsonSerializer.Serialize(Detail with { RowVersion = "refreshed-version" }, JsonOptions);
            }
            else if (path.EndsWith("/change-number", StringComparison.Ordinal))
            {
                ChangedNumber = ((ChangeFlightNumberRequestModel)args[2]!).FlightNumber;
            }
            else if (method == "PUT")
            {
                PlanningVersion = (string?)args[5];
            }
            else if (args[2] is ScheduleFlightsRequestModel bulk)
            {
                ScheduledNumber = bulk.FlightNumber;
                response = JsonSerializer.Serialize(new[] { Guid.NewGuid() });
            }
            else
            {
                ScheduledNumber = ((ScheduleFlightRequestModel)args[2]!).FlightNumber;
                response = JsonSerializer.Serialize(Guid.NewGuid());
            }
            return ValueTask.FromResult((TValue)(object)response);
        }
    }
}
