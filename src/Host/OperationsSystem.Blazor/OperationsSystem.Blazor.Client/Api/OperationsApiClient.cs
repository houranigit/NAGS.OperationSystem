using System.Globalization;

namespace OperationsSystem.Blazor.Client.Api;

/// <summary>
/// Typed access to the Operations module API (<c>/api/v1/operations</c>). Editable records use
/// optimistic concurrency: reads return a <c>RowVersion</c> that mutations echo back as <c>If-Match</c>.
/// </summary>
public sealed class OperationsApiClient(BrowserApiClient api)
{
    public Task<OperationsDashboard> GetDashboardAsync(CancellationToken ct = default) =>
        api.GetAsync<OperationsDashboard>("/operations/dashboard", ct);

    public Task<PagedResult<FlightListItem>> GetFlightsAsync(
        int page, int pageSize, string? search = null, Guid? stationId = null, Guid? customerId = null,
        string? status = null, DateTimeOffset? fromUtc = null, DateTimeOffset? toUtc = null, string? sort = null, CancellationToken ct = default)
    {
        var query = new QueryBuilder()
            .Add("page", page).Add("pageSize", pageSize).Add("search", search)
            .Add("stationId", stationId).Add("customerId", customerId).Add("status", status)
            .Add("fromUtc", fromUtc).Add("toUtc", toUtc).Add("sort", sort).Build();
        return api.GetAsync<PagedResult<FlightListItem>>($"/operations/flights{query}", ct);
    }

    public Task<IReadOnlyList<CalendarFlight>> GetCalendarAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, Guid? stationId = null, CancellationToken ct = default)
    {
        var query = new QueryBuilder().Add("fromUtc", fromUtc).Add("toUtc", toUtc).Add("stationId", stationId).Build();
        return api.GetAsync<IReadOnlyList<CalendarFlight>>($"/operations/flights/calendar{query}", ct);
    }

    public Task<FlightDetail> GetFlightAsync(Guid id, CancellationToken ct = default) =>
        api.GetAsync<FlightDetail>($"/operations/flights/{id}", ct);

    public Task<Guid> ScheduleFlightAsync(ScheduleFlightRequestModel request, CancellationToken ct = default) =>
        api.PostAsync<ScheduleFlightRequestModel, Guid>("/operations/flights", request, ct);

    public Task<Guid> CancelFlightAsync(Guid flightId, CancelFlightRequestModel request, CancellationToken ct = default) =>
        api.PostAsync<CancelFlightRequestModel, Guid>($"/operations/flights/{flightId}/cancel", request, ct);

    public Task<PagedResult<ReviewQueueItem>> GetReviewQueueAsync(int page, int pageSize, Guid? stationId = null, CancellationToken ct = default)
    {
        var query = new QueryBuilder().Add("page", page).Add("pageSize", pageSize).Add("stationId", stationId).Build();
        return api.GetAsync<PagedResult<ReviewQueueItem>>($"/operations/work-orders/review-queue{query}", ct);
    }

    public Task ApproveWorkOrderAsync(Guid id, string rowVersion, CancellationToken ct = default) =>
        api.PostAsync($"/operations/work-orders/{id}/approve", rowVersion, ct);

    public Task RejectWorkOrderAsync(Guid id, string rowVersion, CancellationToken ct = default) =>
        api.PostAsync($"/operations/work-orders/{id}/reject", rowVersion, ct);

    public Task ReturnWorkOrderAsync(Guid id, string rowVersion, CancellationToken ct = default) =>
        api.PostAsync($"/operations/work-orders/{id}/return", rowVersion, ct);

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
