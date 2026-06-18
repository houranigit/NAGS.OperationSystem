using Radzen;

namespace Host.Web.Services;

/// <summary>
/// Persists and restores Radzen DataGrid settings (columns, sort, filters, page size, column picker) per grid.
/// Use a unique key per grid instance (e.g. "StationList-Settings").
/// </summary>
public interface IGridSettingsStorage
{
    Task<DataGridSettings?> LoadAsync(string key, CancellationToken cancellationToken = default);
    Task SaveAsync(string key, DataGridSettings? settings, CancellationToken cancellationToken = default);
    Task ClearAsync(string key, CancellationToken cancellationToken = default);
}
