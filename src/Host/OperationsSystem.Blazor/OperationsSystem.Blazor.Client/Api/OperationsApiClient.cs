using System.Globalization;

namespace OperationsSystem.Blazor.Client.Api;

public sealed class OperationsApiClient(BrowserApiClient api)
{
    public Task<PagedResult<FlightListItem>> GetFlightsAsync(
        int page, int pageSize, string? search = null, Guid? stationId = null, Guid? customerId = null,
        Guid? operationTypeId = null, DateTimeOffset? fromUtc = null, DateTimeOffset? toUtc = null,
        string? sort = null, CancellationToken ct = default)
    {
        var query = new QueryBuilder()
            .Add("page", page).Add("pageSize", pageSize).Add("search", search)
            .Add("stationId", stationId).Add("customerId", customerId).Add("operationTypeId", operationTypeId)
            .Add("fromUtc", fromUtc).Add("toUtc", toUtc).Add("sort", sort).Build();
        return api.GetAsync<PagedResult<FlightListItem>>($"/operations/flights{query}", ct);
    }

    public Task<FlightDetail> GetFlightAsync(Guid id, CancellationToken ct = default) =>
        api.GetAsync<FlightDetail>($"/operations/flights/{id}", ct);

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
