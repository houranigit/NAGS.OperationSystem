using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Host.Web.Services;
using Radzen;
using Radzen.Blazor;

namespace Host.Web.Components.Shared.DataGrid;

/// <summary>
/// Base class for pages that use a Radzen DataGrid with persistent column/filter/sort settings.
/// Each load operation runs in its own DI scope so that concurrent calls from virtualization
/// don't collide on the same scoped DbContext (standard Blazor Server pattern).
/// A new load automatically cancels any previous in-flight load.
/// </summary>
/// <remarks>
/// We deliberately do NOT trigger an initial query from <see cref="OnInitializedAsync"/>.
/// The Radzen <c>RadzenDataGrid</c> fires its own <c>LoadData</c> event on first render,
/// so calling it manually here would issue the same query 2–3 times
/// (manual load + grid's first-render load + another load after <c>LoadSettings</c> restores sort/filter).
/// Instead we pre-load saved <see cref="DataGridSettings"/> before the grid mounts so the grid
/// fires <c>LoadData</c> exactly once with the restored sort/filter state.
/// </remarks>
public abstract class GridSettingsComponentBase<TItem> : ComponentBase, IDisposable where TItem : class
{
    [Inject]
    protected IGridSettingsStorage Storage { get; set; } = null!;

    [Inject]
    protected IServiceScopeFactory ScopeFactory { get; set; } = null!;

    protected abstract string SettingsStorageKey { get; }

    /// <summary>
    /// Optional DTO-property-name → entity-property-path map applied to <see cref="LoadDataArgs.Filter"/>
    /// and <see cref="LoadDataArgs.OrderBy"/> before they reach the handler. Override this on grids
    /// where the DTO and entity shapes diverge (e.g. <c>FlightDto.CustomerSnapshot</c> ↔
    /// <c>Flight.Customer</c>) so the server-side <c>IQueryable&lt;TEntity&gt;.Where(filter)</c>
    /// resolves the path correctly. Return <c>null</c> when the DTO and entity property names match.
    /// </summary>
    protected virtual IReadOnlyDictionary<string, string>? FilterPropertyMap => null;

    protected RadzenDataGrid<TItem>? Grid { get; set; }

    protected IReadOnlyList<TItem>? Data { get; private set; }
    protected int Count { get; private set; }

    /// <summary>
    /// <c>true</c> from the moment the component is constructed until the grid's first
    /// <c>LoadData</c> callback resolves. Keeping this <c>true</c> at mount time is what lets
    /// the Radzen grid render immediately (so <c>LoadData</c> can fire) while any consumer
    /// using <c>@if (Data is null &amp;&amp; !IsLoading)</c> as a skeleton gate still short-circuits.
    /// </summary>
    protected bool IsLoading { get; private set; } = true;

    private DataGridSettings? _settings;
    private string? _lastSavedSettingsJson;
    private CancellationTokenSource? _loadCts;

    private static readonly JsonSerializerOptions SettingsCompareOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = null,
        WriteIndented = false
    };

    /// <summary>
    /// Returns property names of currently visible columns, or null when grid is not yet rendered.
    /// </summary>
    protected IReadOnlyList<string>? GetVisibleColumnPropertyNames()
    {
        if (Grid?.Settings?.Columns is null || !Grid.Settings.Columns.Any())
            return null;

        var names = Grid.Settings.Columns
            .Where(c => c.Visible && !string.IsNullOrWhiteSpace(c.Property))
            .Select(c => c.Property!)
            .ToList();

        return names.Count > 0 ? names : null;
    }

    /// <summary>
    /// Cancels any previous in-flight load, creates a fresh DI scope, computes paging,
    /// then invokes loadCore with the scoped IServiceProvider and CancellationToken.
    /// </summary>
    protected async Task LoadDataAsync(
        LoadDataArgs args,
        Func<LoadDataArgs, int, int, IServiceProvider, CancellationToken, Task> loadCore)
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        IsLoading = true;
        StateHasChanged();

        try
        {
            using var scope = ScopeFactory.CreateScope();

            var pageSize = args.Top ?? GridDefaults.DefaultPageSize;
            var pageNumber = pageSize > 0 && args.Skip.HasValue
                ? (args.Skip!.Value / pageSize) + 1
                : 1;

            // Radzen builds the lambda client-side against TItem (the DTO) and serializes it.
            // Two clean-ups happen here so every handler can blindly call query.Where(args.Filter):
            //   1) Strip enum C-style casts ("(Foo.Bar.MyEnum)1" → "1") that Dynamic.Core can't parse.
            //   2) Rewrite DTO property names to the entity-side path when the grid declares a map.
            args.Filter = RadzenFilterNormalizer.StripEnumCasts(args.Filter);
            args.Filter = RadzenFilterNormalizer.RemapProperties(args.Filter, FilterPropertyMap);
            args.OrderBy = RadzenFilterNormalizer.RemapProperties(args.OrderBy, FilterPropertyMap);

            await loadCore(args, pageSize, pageNumber, scope.ServiceProvider, ct);

            ct.ThrowIfCancellationRequested();
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            // A newer load superseded this one -- silently discard.
        }
        finally
        {
            IsLoading = false;
        }
    }

    protected async Task ShowLoadingAsync()
    {
        IsLoading = true;
        await Task.Yield();
        IsLoading = false;
    }

    protected void SetGridResult(IReadOnlyList<TItem>? items, int totalCount)
    {
        Data = items;
        Count = totalCount;
    }

    protected abstract Task HandleLoadDataAsync(LoadDataArgs args);

    protected static LoadDataArgs GetInitialLoadDataArgs() =>
        new() { Skip = 0, Top = GridDefaults.DefaultPageSize };

    /// <remarks>
    /// Only restores saved grid settings here. The grid will fire its own <c>LoadData</c>
    /// on first render (exactly once) with sort/filter pre-applied from the restored settings.
    /// </remarks>
    protected override async Task OnInitializedAsync()
    {
        await LoadStateAsync();
    }

    public async Task ReloadAndResetAsync()
    {
        if (Grid is not null)
        {
            await Grid.Reload();
        }
        else
        {
            await HandleLoadDataAsync(GetInitialLoadDataArgs());
        }

        await InvokeAsync(StateHasChanged);
    }

    protected DataGridSettings? Settings
    {
        get => _settings;
        set
        {
            if (ReferenceEquals(_settings, value))
                return;

            _settings = value;
            InvokeAsync(SaveStateAsync);
        }
    }

    protected void LoadSettings(DataGridLoadSettingsEventArgs args)
    {
        if (Settings is not null)
            args.Settings = Settings;
    }

    protected async Task OnPickedColumnsChanged(DataGridPickedColumnsChangedEventArgs<TItem> args)
    {
        await SaveStateAsync();
    }

    protected async Task LoadStateAsync()
    {
        _settings = await Storage.LoadAsync(SettingsStorageKey);
        _lastSavedSettingsJson = SerializeForCompare(_settings);
    }

    /// <summary>
    /// Writes to localStorage only when the serialized settings actually change.
    /// Prevents the dozen redundant writes Radzen issues during first-render column/settings hydration.
    /// </summary>
    protected async Task SaveStateAsync()
    {
        var json = SerializeForCompare(Settings);
        if (string.Equals(json, _lastSavedSettingsJson, StringComparison.Ordinal))
            return;

        _lastSavedSettingsJson = json;
        await Storage.SaveAsync(SettingsStorageKey, Settings);
    }

    public async Task ClearSavedSettingsAsync()
    {
        _settings = null;
        _lastSavedSettingsJson = null;
        await Storage.ClearAsync(SettingsStorageKey);
        await InvokeAsync(StateHasChanged);
    }

    private static string? SerializeForCompare(DataGridSettings? settings) =>
        settings is null ? null : JsonSerializer.Serialize(settings, SettingsCompareOptions);

    public void Dispose()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        GC.SuppressFinalize(this);
    }
}
