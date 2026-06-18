using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;
using Radzen;

namespace Host.Web.Services;

internal sealed class GridSettingsStorage(IJSRuntime jsRuntime) : IGridSettingsStorage
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = null,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<DataGridSettings?> LoadAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var json = await jsRuntime.InvokeAsync<string?>(
                "window.localStorage.getItem", cancellationToken, key);

            if (string.IsNullOrEmpty(json) || json == "null")
                return null;

            return JsonSerializer.Deserialize<DataGridSettings>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public async Task SaveAsync(string key, DataGridSettings? settings, CancellationToken cancellationToken = default)
    {
        try
        {
            var json = settings is null
                ? "null"
                : JsonSerializer.Serialize(new DataGridSettings
                {
                    Columns = settings.Columns.Select(c => new DataGridColumnSettings
                    {
                        Visible = c.Visible,
                        Property = c.Property,
                        UniqueID = c.UniqueID,
                        Width = c.Width,
                        OrderIndex = c.OrderIndex,
                        FilterOperator = c.FilterOperator,
                    }),
                    PageSize = settings.PageSize,
                }, JsonOptions);

            await jsRuntime.InvokeVoidAsync(
                "window.localStorage.setItem", cancellationToken, key, json);
        }
        catch
        {
            // Ignore storage failures (e.g. private browsing mode)
        }
    }

    public async Task ClearAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            await jsRuntime.InvokeVoidAsync(
                "window.localStorage.removeItem", cancellationToken, key);
        }
        catch
        {
            // Ignore
        }
    }
}
