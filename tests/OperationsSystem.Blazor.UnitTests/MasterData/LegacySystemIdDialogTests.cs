using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.Auth;
using OperationsSystem.Blazor.Client.Features.AircraftTypes.Components;
using OperationsSystem.Blazor.Client.Features.Catalogs;
using OperationsSystem.Blazor.Client.Features.Catalogs.Components;
using OperationsSystem.Blazor.Client.Features.Customers.Components;
using OperationsSystem.Blazor.Client.Features.ManpowerTypes.Components;
using OperationsSystem.Blazor.Client.Features.StaffMembers.Components;
using OperationsSystem.Blazor.Client.Features.Stations.Components;
using OperationsSystem.Blazor.Client.Features.Tools.Components;
using OperationsSystem.Blazor.Client.State;
using Radzen;
using Shouldly;

namespace OperationsSystem.Blazor.UnitTests.MasterData;

public sealed class LegacySystemIdDialogTests
{
    public static TheoryData<string> Catalogs => new()
    {
        "services", "operation-types", "materials", "general-supports", "tools",
        "aircraft-types", "manpower-types", "stations", "customers", "staff-members"
    };

    [Theory]
    [MemberData(nameof(Catalogs))]
    public async Task Editing_loads_the_saved_legacy_id_and_retains_it_when_other_fields_are_saved(string catalog)
    {
        var harness = CreateDialog(catalog, editing: true);

        await harness.InitializeAsync();

        harness.ReadModel("LegacySystemId").ShouldBe("XYZ123");
        harness.SetModel(catalog == "aircraft-types" ? "Model" : catalog == "staff-members" ? "FullName" : "Name", "Changed name");
        await harness.SubmitAsync();

        harness.Runtime.SavedMethod.ShouldBe("PUT");
        harness.Runtime.SavedPath.ShouldBe($"/masterdata/{catalog}/{harness.Runtime.Id}");
        harness.Runtime.SavedLegacyId.ShouldBe("XYZ123");
        harness.Runtime.IfMatch.ShouldBe("saved-version");
        harness.ReadError().ShouldBeNull();
    }

    [Theory]
    [MemberData(nameof(Catalogs))]
    public async Task Creating_accepts_and_trims_a_legacy_id_without_changing_its_case(string catalog)
    {
        var harness = CreateDialog(catalog, editing: false);
        await harness.InitializeAsync();
        harness.SetRequiredFields(catalog);
        harness.SetModel("LegacySystemId", "  Xyz-00123  ");

        await harness.SubmitAsync();

        harness.Runtime.SavedMethod.ShouldBe("POST");
        harness.Runtime.SavedPath.ShouldBe($"/masterdata/{catalog}");
        harness.Runtime.SavedLegacyId.ShouldBe("Xyz-00123");
        harness.ReadError().ShouldBeNull();
    }

    [Theory]
    [MemberData(nameof(Catalogs))]
    public async Task Blank_is_optional_on_create_and_explicitly_clears_a_saved_id_on_update(string catalog)
    {
        var create = CreateDialog(catalog, editing: false);
        await create.InitializeAsync();
        create.SetRequiredFields(catalog);
        create.SetModel("LegacySystemId", "  ");
        await create.SubmitAsync();
        create.Runtime.SavedMethod.ShouldBe("POST");
        create.Runtime.SavedLegacyId.ShouldBeNull();
        create.ReadError().ShouldBeNull();

        var edit = CreateDialog(catalog, editing: true);
        await edit.InitializeAsync();
        edit.SetModel("LegacySystemId", "  ");
        await edit.SubmitAsync();
        edit.Runtime.SavedMethod.ShouldBe("PUT");
        edit.Runtime.SavedLegacyId.ShouldBe(string.Empty);
        edit.ReadError().ShouldBeNull();
    }

    [Theory]
    [InlineData("services")]
    [InlineData("operation-types")]
    [InlineData("customers")]
    [InlineData("tools")]
    [InlineData("materials")]
    [InlineData("general-supports")]
    public async Task Protected_records_load_the_mapping_editor_and_save_the_legacy_id(string catalog)
    {
        var harness = CreateDialog(catalog, editing: true, system: true);

        await harness.InitializeAsync();
        harness.ReadSystemFlag().ShouldBeTrue();
        harness.SetModel("LegacySystemId", "  Protected-123  ");
        await harness.SubmitAsync();

        harness.Runtime.SavedMethod.ShouldBe("PUT");
        harness.Runtime.SavedLegacyId.ShouldBe("Protected-123");
        harness.Runtime.SavedName.ShouldBe("Test name");
        harness.ReadError().ShouldBeNull();
    }

    private static DialogHarness CreateDialog(string catalog, bool editing, bool system = false)
    {
        (Type type, string idParameter, SimpleCatalogKind? kind) = catalog switch
        {
            "services" => (typeof(SimpleCatalogFormDialog), "EntityId", (SimpleCatalogKind?)SimpleCatalogKind.Services),
            "operation-types" => (typeof(SimpleCatalogFormDialog), "EntityId", SimpleCatalogKind.OperationTypes),
            "materials" => (typeof(SimpleCatalogFormDialog), "EntityId", SimpleCatalogKind.Materials),
            "general-supports" => (typeof(SimpleCatalogFormDialog), "EntityId", SimpleCatalogKind.GeneralSupports),
            "tools" => (typeof(ToolFormDialog), "ToolId", null),
            "aircraft-types" => (typeof(AircraftTypeFormDialog), "AircraftTypeId", null),
            "manpower-types" => (typeof(ManpowerTypeFormDialog), "ManpowerTypeId", null),
            "stations" => (typeof(StationFormDialog), "StationId", null),
            "customers" => (typeof(CustomerFormDialog), "CustomerId", null),
            "staff-members" => (typeof(StaffMemberFormDialog), "StaffMemberId", null),
            _ => throw new ArgumentOutOfRangeException(nameof(catalog))
        };
        var runtime = new CatalogRuntime { IsSystem = system };
        if (system)
        {
            runtime.Id = catalog switch
            {
                "tools" => new Guid("60000000-0000-0000-0000-000000000001"),
                "materials" => new Guid("70000000-0000-0000-0000-000000000001"),
                "general-supports" => new Guid("80000000-0000-0000-0000-000000000001"),
                _ => runtime.Id
            };
        }
        var tokens = new AuthTokenStore();
        var locale = new LocaleState(runtime);
        var refresher = new ClientTokenRefresher(runtime, tokens, locale);
        var api = new BrowserApiClient(runtime, tokens, locale, refresher);
        var dialog = Activator.CreateInstance(type)!;
        Inject("MasterData", new MasterDataApiClient(api));
        Inject("Identity", new IdentityApiClient(api));
        Inject("Auth", new AuthSession(api, tokens, refresher));
        Inject("DialogService", new DialogService(new TestNavigationManager(), runtime));
        if (kind is not null)
            type.GetProperty("Kind")!.SetValue(dialog, kind.Value);
        if (editing)
            type.GetProperty(idParameter)!.SetValue(dialog, runtime.Id);
        return new DialogHarness(dialog, runtime);

        void Inject(string name, object value) =>
            type.GetProperty(name, BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(dialog, value);
    }

    private sealed class DialogHarness(object dialog, CatalogRuntime runtime)
    {
        public CatalogRuntime Runtime { get; } = runtime;
        private Type DialogType => dialog.GetType();
        private object Model => DialogType.GetField("model", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;

        public Task InitializeAsync() => InvokeAsync("OnInitializedAsync");
        public Task SubmitAsync() => InvokeAsync("SubmitAsync");
        private Task InvokeAsync(string name) =>
            (Task)DialogType.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dialog, null)!;
        public object? ReadModel(string name) => Model.GetType().GetProperty(name)!.GetValue(Model);
        public void SetModel(string name, object? value) => Model.GetType().GetProperty(name)!.SetValue(Model, value);
        public string? ReadError() =>
            (string?)DialogType.GetField("errorMessage", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog);
        public bool ReadSystemFlag() =>
            (bool)DialogType.GetField("isSystem", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;

        public void SetRequiredFields(string catalog)
        {
            if (catalog == "aircraft-types")
                SetModel("Model", "B737");
            else if (catalog == "staff-members")
            {
                SetModel("FullName", "Test staff");
                SetModel("EmployeeId", "EMP-1");
                SetModel("Email", "staff@example.com");
                SetModel("StationId", Runtime.Id);
                SetModel("ManpowerTypeId", Runtime.Id);
            }
            else
                SetModel("Name", "Test name");

            if (catalog is "customers" or "stations")
                SetModel("CountryId", Runtime.Id);
            if (catalog == "stations")
                SetModel("IataCode", "MED");
        }
    }

    private sealed class CatalogRuntime : IJSRuntime
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        public Guid Id { get; set; } = Guid.NewGuid();
        public string? SavedMethod { get; private set; }
        public string? SavedPath { get; private set; }
        public string? SavedLegacyId { get; private set; }
        public string? SavedName { get; private set; }
        public string? IfMatch { get; private set; }
        public bool IsSystem { get; set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            identifier.ShouldBe("operationsSystem.api.request");
            var method = (string)args![0]!;
            var path = (string)args[1]!;
            string response;
            if (method == "GET")
            {
                response = path.Contains("/options", StringComparison.Ordinal)
                    ? "[]"
                    : JsonSerializer.Serialize(new
                    {
                        Id, Name = "Test name", FullName = "Test staff", EmployeeId = "EMP-1", Email = "staff@example.com",
                        IataCode = path.Contains("/customers/", StringComparison.Ordinal) ? "SV" : "MED",
                        CountryId = Id, StationId = Id, ManpowerTypeId = Id, Model = "B737", Manufacturer = 0,
                        IsActive = true, IsSystem, CreatedAtUtc = "2026-10-04T10:00:00Z", RowVersion = "saved-version",
                        LegacySystemId = "XYZ123", Address = new { }, Equipments = Array.Empty<object>(),
                        Contacts = Array.Empty<object>(), Licenses = Array.Empty<object>()
                    }, JsonOptions);
            }
            else
            {
                SavedMethod = method;
                SavedPath = path;
                var body = args[2]!;
                SavedLegacyId = (string?)body.GetType().GetProperty("LegacySystemId")!.GetValue(body);
                SavedName = (string?)body.GetType().GetProperty("Name")?.GetValue(body);
                IfMatch = (string?)args[5];
                response = method == "POST" ? JsonSerializer.Serialize(Id) : "";
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
