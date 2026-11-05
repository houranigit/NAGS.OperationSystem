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
    [InlineData(false, "XY", " xy123 ", " xy123 ")]
    [InlineData(true, "XY", " xy123 ", " xy123 ")]
    [InlineData(false, null, "123", "123")]
    [InlineData(false, "XY", "123/1", "123/1")]
    [InlineData(true, "XY", " xy123/12 ", " xy123/12 ")]
    public async Task Scheduling_preserves_the_entered_number_for_server_validation(bool bulkMode, string? code, string input, string expected)
    {
        var (dialog, runtime) = CreateDialog(code, input, bulkMode);

        await InvokeAsync(dialog, bulkMode ? "CreateBulkAsync" : "CreateAsync");

        runtime.ScheduledNumber.ShouldBe(expected);
    }

    [Theory]
    [InlineData("456", "456")]
    [InlineData(" XY123 ", " XY123 ")]
    [InlineData(" xy456 ", " xy456 ")]
    [InlineData("456/12", "456/12")]
    [InlineData(" xy456/1 ", " xy456/1 ")]
    public async Task Number_edits_send_the_entered_number_for_server_validation(string input, string expected)
    {
        var (dialog, runtime) = CreateDialog("XY", input);
        runtime.Detail = SetOriginal(dialog, "XY", "XY123");

        await InvokeAsync(dialog, "UpdateAsync", runtime.Detail.Id);

        runtime.ChangedNumber.ShouldBe(expected);
        runtime.PlanningVersion.ShouldBe("refreshed-version");
    }

    [Theory]
    [InlineData("CustomerId")]
    [InlineData("StationId")]
    [InlineData("OperationTypeId")]
    public async Task First_next_blocks_missing_required_selections(string field)
    {
        var (dialog, runtime) = CreateDialog("XY", "123");
        SetModel(dialog, field, Guid.Empty);

        await InvokeAsync(dialog, "NextAsync");

        ReadField<int>(dialog, "step").ShouldBe(0);
        ReadField<string?>(dialog, "errorMessage").ShouldNotBeNull();
        runtime.Requests.ShouldBe(0);
        runtime.Writes.ShouldBe(0);
    }

    [Theory]
    [InlineData("XY", "12345")]
    [InlineData("XY", " 123 ")]
    [InlineData("XY", "12 3")]
    [InlineData("XY", "12-3")]
    [InlineData("XY", "123/")]
    [InlineData("XY", "123/123")]
    [InlineData("XY", "123AB/1")]
    [InlineData("XY", "XY123AB/1")]
    [InlineData("XY", "123ABC")]
    [InlineData("XY", "123AB")]
    [InlineData("XY", "123A/1")]
    [InlineData(null, "1234A/12")]
    [InlineData("XY", "ZZ123")]
    [InlineData(null, "XY123")]
    [InlineData("XY", "١٢٣")]
    public async Task First_next_rejects_bad_numbers_locally(string? code, string input)
    {
        var (dialog, runtime) = CreateDialog(code, input);

        await InvokeAsync(dialog, "NextAsync");

        ReadField<int>(dialog, "step").ShouldBe(0);
        ReadField<string?>(dialog, "errorMessage").ShouldNotBeNull();
        runtime.Requests.ShouldBe(0);
        runtime.Writes.ShouldBe(0);
    }

    [Theory]
    [InlineData(false, "XY", "1")]
    [InlineData(false, "XY", "1234")]
    [InlineData(false, "XY", "123a")]
    [InlineData(false, "XY", "123/1")]
    [InlineData(false, "XY", "XY123")]
    [InlineData(false, "XY", "xy123a")]
    [InlineData(true, "XY", "123/01")]
    [InlineData(true, null, "1234/12")]
    [InlineData(false, null, "123")]
    [InlineData(false, null, "123A")]
    [InlineData(false, "5Y", "5Y")]
    [InlineData(false, "12", "12")]
    public async Task First_next_accepts_valid_numbers_locally_without_writes(bool bulkMode, string? code, string input)
    {
        var (dialog, runtime) = CreateDialog(code, input, bulkMode);

        await InvokeAsync(dialog, "NextAsync");

        ReadField<int>(dialog, "step").ShouldBe(1);
        ReadField<string?>(dialog, "errorMessage").ShouldBeNull();
        ReadField<object>(dialog, "model").GetType().GetProperty("FlightNumber")!
            .GetValue(ReadField<object>(dialog, "model")).ShouldBe(input);
        runtime.Requests.ShouldBe(0);
        runtime.Writes.ShouldBe(0);
    }

    [Theory]
    [InlineData("XY", "XY123", true)]
    [InlineData("XY", "ZZ123", false)]
    [InlineData(null, "123", true)]
    [InlineData(null, "ZZ123", false)]
    public async Task First_next_uses_the_saved_customer_code_for_number_edits(string? savedCode, string input, bool valid)
    {
        var (dialog, runtime) = CreateDialog("ZZ", input);
        SetOriginal(dialog, savedCode, "100");

        await InvokeAsync(dialog, "NextAsync");

        ReadField<int>(dialog, "step").ShouldBe(valid ? 1 : 0);
        if (valid)
            ReadField<string?>(dialog, "errorMessage").ShouldBeNull();
        else
            ReadField<string?>(dialog, "errorMessage").ShouldNotBeNull();
        runtime.Requests.ShouldBe(0);
        runtime.Writes.ShouldBe(0);
    }

    [Fact]
    public async Task First_next_preserves_an_unchanged_legacy_number_during_a_planning_edit()
    {
        var (dialog, runtime) = CreateDialog("XY", "LEGACY-123");
        SetOriginal(dialog, "XY", "LEGACY-123");

        await InvokeAsync(dialog, "NextAsync");

        ReadField<int>(dialog, "step").ShouldBe(1);
        runtime.Requests.ShouldBe(0);
        runtime.Writes.ShouldBe(0);
    }

    [Fact]
    public async Task First_next_rechecks_required_details_after_waiting_for_station_staff()
    {
        var (dialog, runtime) = CreateDialog("XY", "123");
        var auth = (AuthSession)typeof(ScheduleFlightDialog).GetProperty("Auth", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(dialog)!;
        typeof(AuthSession).GetProperty(nameof(AuthSession.User))!.SetValue(auth, new AuthenticatedUser(
            Guid.NewGuid(), "dispatcher@example.com", "Dispatcher", Guid.NewGuid(), "Dispatcher", "StationStaff", null,
            "Portal", false, false, [OperationsPermissions.FlightsAssign]));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.StaffOptionsResponse = () =>
        {
            started.SetResult();
            return response.Task;
        };
        var next = InvokeAsync(dialog, "NextAsync");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        SetModel(dialog, "StationId", Guid.Empty);
        response.SetResult("[]");
        await next;

        ReadField<int>(dialog, "step").ShouldBe(0);
        ReadField<string?>(dialog, "errorMessage").ShouldBe("Station is required.");
        runtime.Writes.ShouldBe(0);
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
        Inject("MasterData", new MasterDataApiClient(api));
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

    private static Task InvokeAsync(ScheduleFlightDialog dialog, string method, params object[] arguments) =>
        (Task)typeof(ScheduleFlightDialog).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dialog, arguments)!;

    private static T ReadField<T>(ScheduleFlightDialog dialog, string name) =>
        (T)typeof(ScheduleFlightDialog).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;

    private static void SetModel(ScheduleFlightDialog dialog, string name, object value)
    {
        var model = ReadField<object>(dialog, "model");
        model.GetType().GetProperty(name)!.SetValue(model, value);
    }

    private static void SetField(ScheduleFlightDialog dialog, string name, object value) =>
        typeof(ScheduleFlightDialog).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(dialog, value);

    private sealed class FlightRuntime : IJSRuntime
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        public FlightDetail? Detail { get; set; }
        public string? ScheduledNumber { get; private set; }
        public string? ChangedNumber { get; private set; }
        public string? PlanningVersion { get; private set; }
        public int Requests { get; private set; }
        public int Writes { get; private set; }
        public Func<Task<string>>? StaffOptionsResponse { get; set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            identifier.ShouldBe("operationsSystem.api.request");
            var method = (string)args![0]!;
            var path = (string)args[1]!;
            var response = string.Empty;
            Requests++;
            if (method == "GET" && path.StartsWith("/masterdata/staff-members/options", StringComparison.Ordinal))
            {
                response = StaffOptionsResponse is null ? "[]" : await StaffOptionsResponse();
            }
            else if (method == "GET")
            {
                Detail.ShouldNotBeNull();
                response = JsonSerializer.Serialize(Detail with { RowVersion = "refreshed-version" }, JsonOptions);
            }
            else if (path.EndsWith("/change-number", StringComparison.Ordinal))
            {
                Writes++;
                ChangedNumber = ((ChangeFlightNumberRequestModel)args[2]!).FlightNumber;
            }
            else if (method == "PUT")
            {
                Writes++;
                PlanningVersion = (string?)args[5];
            }
            else if (args[2] is ScheduleFlightsRequestModel bulk)
            {
                Writes++;
                ScheduledNumber = bulk.FlightNumber;
                response = JsonSerializer.Serialize(new[] { Guid.NewGuid() });
            }
            else
            {
                Writes++;
                ScheduledNumber = ((ScheduleFlightRequestModel)args[2]!).FlightNumber;
                response = JsonSerializer.Serialize(Guid.NewGuid());
            }
            return (TValue)(object)response;
        }
    }
}
