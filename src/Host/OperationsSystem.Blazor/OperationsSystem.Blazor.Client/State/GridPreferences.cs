using Microsoft.JSInterop;

namespace OperationsSystem.Blazor.Client.State;

/// <summary>
/// Persists per-grid display preferences (currently the chosen page size) in the browser's
/// <c>localStorage</c>, keyed by a stable grid identifier so each list page remembers its own value.
/// All access is best-effort: storage failures fall back to the supplied defaults.
/// </summary>
public sealed class GridPreferences(IJSRuntime jsRuntime)
{
    private const string KeyPrefix = "os:grid:pageSize:";

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
}
