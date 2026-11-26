using System.Globalization;
using System.Text.Json;

namespace OperationsSystem.Blazor.Client.Api;

public sealed class OperationsApiClient(BrowserApiClient api)
{
    public Task<PagedResult<FlightListItem>> GetFlightsAsync(
        int page, int pageSize, string? search = null, Guid? stationId = null, Guid? customerId = null,
        Guid? operationTypeId = null, DateTimeOffset? fromUtc = null, DateTimeOffset? toUtc = null,
        string? sort = null, CancellationToken ct = default, IReadOnlyList<string>? statuses = null, string? flightType = null)
    {
        var query = WithFlightFilters(new QueryBuilder().Add("page", page).Add("pageSize", pageSize),
            search, stationId, customerId, operationTypeId, fromUtc, toUtc, sort, statuses, flightType).Build();
        return api.GetAsync<PagedResult<FlightListItem>>($"/operations/flights{query}", ct);
    }

    public Task DownloadFlightsExportAsync(string format, string? search, Guid? stationId, Guid? customerId,
        Guid? operationTypeId, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, string? sort,
        IReadOnlyList<string>? statuses, string? flightType, string? report = null, CancellationToken ct = default)
    {
        var query = WithFlightFilters(new QueryBuilder().Add("format", format),
            search, stationId, customerId, operationTypeId, fromUtc, toUtc, sort, statuses, flightType)
            .Add("report", report).Build();
        return api.DownloadFileAsync($"/operations/flights/export{query}", cancellationToken: ct);
    }

    private static QueryBuilder WithFlightFilters(QueryBuilder query, string? search, Guid? stationId, Guid? customerId,
        Guid? operationTypeId, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, string? sort,
        IReadOnlyList<string>? statuses, string? flightType) => query
            .Add("search", search)
            .Add("stationId", stationId).Add("customerId", customerId).Add("operationTypeId", operationTypeId)
            .Add("status", statuses is { Count: > 0 } ? string.Join(',', statuses) : null)
            .Add("flightType", flightType)
            .Add("fromUtc", fromUtc).Add("toUtc", toUtc).Add("sort", sort);

    public Task<FlightDetail> GetFlightAsync(Guid id, CancellationToken ct = default) =>
        api.GetAsync<FlightDetail>($"/operations/flights/{id}", ct);

    public Task<PortalFlightOperations> GetPortalFlightOperationsAsync(Guid id, CancellationToken ct = default) =>
        api.GetAsync<PortalFlightOperations>(PortalOperationsPath(id), ct);

    public Task DownloadPortalWorkOrderAsync(Guid id, CancellationToken ct = default) =>
        api.DownloadFileAsync($"{PortalOperationsPath(id)}/print", "work-order.pdf", ct);

    public Task<PortalFlightOperations> ApprovePortalFlightAsync(Guid id, string rowVersion, CancellationToken ct = default) =>
        api.PutAsync<object, PortalFlightOperations>($"{PortalOperationsPath(id)}/approval", new { }, rowVersion, ct);

    public Task<PortalFlightOperations> UnlockPortalFlightAsync(Guid id, string rowVersion, CancellationToken ct = default) =>
        api.DeleteAsync<PortalFlightOperations>($"{PortalOperationsPath(id)}/approval", rowVersion, ct);

    internal Task DeletePortalOrderAsync(Guid id, PortalFlightOrderKind kind, string rowVersion, CancellationToken ct = default)
    {
        var order = kind == PortalFlightOrderKind.WorkOrder ? "work-order" : "cancellation-order";
        return api.DeleteAsync($"{PortalOperationsPath(id)}/{order}", rowVersion, ct);
    }

    public Task<PortalFlightOperations> SavePortalFlightWorkAsync(Guid id, PortalWorkBatchRequest request,
        string rowVersion, CancellationToken ct = default) =>
        PutPortalWorkAsync(
            $"{PortalOperationsPath(id)}/batch", request, rowVersion, ct);

    public Task<PortalFlightOperations> SavePortalReturnToRampAsync(Guid id, Guid occurrenceId,
        PortalReturnToRampRequest request, string rowVersion, CancellationToken ct = default) =>
        PutPortalWorkAsync(
            $"{PortalOperationsPath(id)}/returns-to-ramp/{occurrenceId}", request, rowVersion, ct);

    // Serialize media before JS interop converts nested byte arrays to typed arrays.
    private Task<PortalFlightOperations> PutPortalWorkAsync<TRequest>(string path, TRequest request, string rowVersion,
        CancellationToken ct) => api.PutAsync<JsonElement, PortalFlightOperations>(
            path, JsonSerializer.SerializeToElement(request, JsonSerializerOptions.Web), rowVersion, ct);

    public Task SavePortalCancellationAsync(Guid id, PortalFlightCancellationRequest request, string rowVersion,
        CancellationToken ct = default) =>
        api.PutAsync($"{PortalOperationsPath(id)}/cancellation", request, rowVersion, ct);

    public Task UpdatePortalCancellationAsync(Guid id, Guid submissionId, PortalFlightCancellationRequest request,
        string rowVersion, CancellationToken ct = default) =>
        api.PutAsync($"{PortalOperationsPath(id)}/cancellation/{submissionId}", request, rowVersion, ct);

    public Task UndoPortalCancellationAsync(Guid id, Guid submissionId, string rowVersion, CancellationToken ct = default) =>
        api.DeleteAsync($"{PortalOperationsPath(id)}/cancellation/{submissionId}", rowVersion, ct);

    public Task DownloadPortalFlightMediaAsync(Guid id, string relativePath, string? fallbackFileName = null,
        CancellationToken ct = default) =>
        api.DownloadFileAsync($"{PortalOperationsPath(id)}/{relativePath}", fallbackFileName, ct);

    public Task<string> GetPortalFlightMediaPreviewUrlAsync(Guid id, string relativePath, CancellationToken ct = default) =>
        api.GetFilePreviewUrlAsync($"{PortalOperationsPath(id)}/{relativePath}", ct);

    public Task<IReadOnlyList<Guid>> GetNotificationTargetsAsync(IReadOnlyList<Guid> flightIds, CancellationToken ct = default) =>
        api.PostAsync<object, IReadOnlyList<Guid>>("/operations/flights/notification-targets", new { flightIds }, ct);

    public Task<Guid> ScheduleFlightAsync(ScheduleFlightRequestModel request, CancellationToken ct = default) =>
        api.PostAsync<ScheduleFlightRequestModel, Guid>("/operations/flights", request, ct);

    public Task<IReadOnlyList<Guid>> ScheduleFlightsAsync(ScheduleFlightsRequestModel request, CancellationToken ct = default) =>
        api.PostAsync<ScheduleFlightsRequestModel, IReadOnlyList<Guid>>("/operations/flights/bulk", request, ct);

    public Task UpdateFlightAsync(Guid id, UpdateScheduledFlightRequestModel request, string rowVersion, CancellationToken ct = default) =>
        api.PutAsync($"/operations/flights/{id}", request, rowVersion, ct);

    public Task ChangeFlightNumberAsync(Guid id, ChangeFlightNumberRequestModel request, string rowVersion, CancellationToken ct = default) =>
        api.PostAsync($"/operations/flights/{id}/change-number", request, rowVersion, ct);

    public Task AssignFlightEmployeesAsync(Guid id, IReadOnlyList<Guid> staffMemberIds, string rowVersion, CancellationToken ct = default) =>
        api.PostAsync($"/operations/flights/{id}/assign", new AssignFlightEmployeesRequestModel(staffMemberIds), rowVersion, ct);

    private static string PortalOperationsPath(Guid id) => $"/operations/flights/{id}/operations";

    private sealed class QueryBuilder
    {
        private readonly List<string> _parts = [];

        public QueryBuilder Add(string key, int value)
        {
            _parts.Add($"{key}={value.ToString(CultureInfo.InvariantCulture)}");
            return this;
        }

        public QueryBuilder Add(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                _parts.Add($"{key}={Uri.EscapeDataString(value)}");
            return this;
        }

        public QueryBuilder Add(string key, Guid? value)
        {
            if (value is { } v)
                _parts.Add($"{key}={v}");
            return this;
        }

        public QueryBuilder Add(string key, DateTimeOffset? value)
        {
            if (value is { } v)
                _parts.Add($"{key}={Uri.EscapeDataString(v.ToString("O", CultureInfo.InvariantCulture))}");
            return this;
        }

        public string Build() => _parts.Count == 0 ? string.Empty : "?" + string.Join('&', _parts);
    }
}
