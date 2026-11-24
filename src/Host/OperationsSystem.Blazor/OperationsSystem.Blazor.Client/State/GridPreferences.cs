using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;
using Radzen;

namespace OperationsSystem.Blazor.Client.State;

/// <summary>
/// Persists per-grid page sizes and account-specific column layouts in browser <c>localStorage</c>.
/// All access is best-effort: storage failures fall back to the supplied defaults.
/// </summary>
public sealed class GridPreferences(IJSRuntime jsRuntime)
{
    private const string KeyPrefix = "os:grid:pageSize:";

    private static string ColumnLayoutKey(string gridKey, Guid userId) =>
        $"os:grid:columns:v1:{gridKey}:{userId:N}";

    /// <summary>
    /// Returns the saved page size for <paramref name="gridKey"/> when it is one of <paramref name="allowed"/>;
    /// otherwise returns <paramref name="fallback"/>.
    /// </summary>
    public async Task<int> GetPageSizeAsync(string gridKey, int fallback, IReadOnlyCollection<int> allowed)
    {
        try
        {
            var raw = await jsRuntime.InvokeAsync<string?>("operationsSystem.storage.get", KeyPrefix + gridKey);
            if (int.TryParse(raw, out var value) && allowed.Contains(value))
                return value;
        }
        catch (JSException)
        {
            // Preference is a convenience; fall back to the default if storage is unavailable.
        }

        return fallback;
    }

    /// <summary>Saves the chosen page size for <paramref name="gridKey"/>.</summary>
    public async Task SetPageSizeAsync(string gridKey, int pageSize)
    {
        try
        {
            await jsRuntime.InvokeVoidAsync(
                "operationsSystem.storage.set",
                KeyPrefix + gridKey,
                pageSize.ToString());
        }
        catch (JSException)
        {
            // Best-effort persistence; ignore failures.
        }
    }

    internal async Task<DataGridSettings?> GetColumnLayoutAsync(string gridKey, Guid userId)
    {
        try
        {
            var raw = await jsRuntime.InvokeAsync<string?>("operationsSystem.storage.get", ColumnLayoutKey(gridKey, userId));
            if (string.IsNullOrEmpty(raw))
                return null;

            var columns = JsonSerializer.Deserialize(raw, GridPreferencesJsonContext.Default.GridColumnLayoutArray);
            if (columns is not { Length: > 0 and <= 100 })
                return null;

            return new DataGridSettings
            {
                Columns = columns.Where(column => column is not null && !string.IsNullOrEmpty(column.Id))
                    .Select(column => new DataGridColumnSettings
                    {
                        UniqueID = column.Id,
                        Property = column.Id,
                        Width = ValidWidth(column.Width) ?? string.Empty,
                        OrderIndex = column.OrderIndex is >= 0 and < 100 ? column.OrderIndex : null,
                        Visible = true
                    }).ToList()
            };
        }
        catch (JsonException)
        {
            // Ignore invalid stored preferences.
        }
        catch (JSException)
        {
            // Use the default layout if storage is unavailable.
        }

        return null;
    }

    internal async Task SetColumnLayoutAsync(string gridKey, Guid userId, DataGridSettings settings)
    {
        var columns = settings.Columns
            .Select(column => new GridColumnLayout(ColumnId(column), ValidWidth(column.Width), column.OrderIndex))
            .Where(column => !string.IsNullOrEmpty(column.Id))
            .ToArray();

        try
        {
            await jsRuntime.InvokeVoidAsync("operationsSystem.storage.set", ColumnLayoutKey(gridKey, userId),
                JsonSerializer.Serialize(columns, GridPreferencesJsonContext.Default.GridColumnLayoutArray));
        }
        catch (JSException)
        {
            // The current grid layout is still usable when storage is unavailable.
        }
    }

    internal static string ColumnId(DataGridColumnSettings column) =>
        string.IsNullOrEmpty(column.UniqueID) ? column.Property : column.UniqueID;

    private static string? ValidWidth(string? width) =>
        width is not null && width.EndsWith("px", StringComparison.Ordinal)
        && int.TryParse(width.AsSpan(0, width.Length - 2), NumberStyles.None, CultureInfo.InvariantCulture, out var pixels)
        && pixels > 0 ? width : null;
}

internal sealed record GridColumnLayout(string Id, string? Width, int? OrderIndex);

[JsonSerializable(typeof(GridColumnLayout[]))]
internal sealed partial class GridPreferencesJsonContext : JsonSerializerContext;
