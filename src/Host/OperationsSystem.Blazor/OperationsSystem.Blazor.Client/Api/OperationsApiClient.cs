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
        Guid? operationTypeId = null, IReadOnlyList<string>? statuses = null, DateTimeOffset? fromUtc = null, DateTimeOffset? toUtc = null,
        IReadOnlyList<string>? serviceCategories = null, string? sort = null, CancellationToken ct = default)
    {
        var query = new QueryBuilder()
            .Add("page", page).Add("pageSize", pageSize).Add("search", search)
            .Add("stationId", stationId).Add("customerId", customerId).Add("operationTypeId", operationTypeId).Add("status", JoinValues(statuses))
            .Add("fromUtc", fromUtc).Add("toUtc", toUtc).Add("serviceCategory", JoinValues(serviceCategories)).Add("sort", sort).Build();
        return api.GetAsync<PagedResult<FlightListItem>>($"/operations/flights{query}", ct);
    }

    public Task ExportFlightsAsync(
        string format,
        string? search = null,
        Guid? stationId = null,
        Guid? customerId = null,
        Guid? operationTypeId = null,
        IReadOnlyList<string>? statuses = null,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        IReadOnlyList<string>? serviceCategories = null,
        string? sort = null,
        CancellationToken ct = default)
    {
        var query = new QueryBuilder()
            .Add("format", format)
            .Add("search", search)
            .Add("stationId", stationId)
            .Add("customerId", customerId)
            .Add("operationTypeId", operationTypeId)
            .Add("status", JoinValues(statuses))
            .Add("fromUtc", fromUtc)
            .Add("toUtc", toUtc)
            .Add("serviceCategory", JoinValues(serviceCategories))
            .Add("sort", sort)
            .Build();
        return api.DownloadFileAsync($"/operations/flights/export{query}", cancellationToken: ct);
    }

    public Task<IReadOnlyList<PerLandingExtractionItem>> GetPerLandingExtractionAsync(
        string? search = null,
        Guid? stationId = null,
        Guid? customerId = null,
        Guid? operationTypeId = null,
        IReadOnlyList<string>? statuses = null,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        IReadOnlyList<string>? serviceCategories = null,
        string? sort = null,
        CancellationToken ct = default)
    {
        var query = new QueryBuilder()
            .Add("search", search)
            .Add("stationId", stationId)
            .Add("customerId", customerId)
            .Add("operationTypeId", operationTypeId)
            .Add("status", JoinValues(statuses))
            .Add("fromUtc", fromUtc)
            .Add("toUtc", toUtc)
            .Add("serviceCategory", JoinValues(serviceCategories))
            .Add("sort", sort)
            .Build();
        return api.GetAsync<IReadOnlyList<PerLandingExtractionItem>>($"/operations/flights/per-landing-extract{query}", ct);
    }

    public Task<int> ApprovePerLandingFlightsAsync(
        IReadOnlyList<PerLandingApprovalSelectionModel> selections,
        CancellationToken ct = default) =>
        api.PostAsync<ApprovePerLandingFlightsRequestModel, int>(
            "/operations/flights/per-landing-extract/approve",
            new ApprovePerLandingFlightsRequestModel(selections),
            ct);

    public Task<IReadOnlyList<CalendarFlight>> GetCalendarAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        Guid? stationId = null,
        Guid? customerId = null,
        string? status = null,
        CancellationToken ct = default)
    {
        var query = new QueryBuilder()
            .Add("fromUtc", fromUtc)
            .Add("toUtc", toUtc)
            .Add("stationId", stationId)
            .Add("customerId", customerId)
            .Add("status", status)
            .Build();
        return api.GetAsync<IReadOnlyList<CalendarFlight>>($"/operations/flights/calendar{query}", ct);
    }

    public Task<FlightDetail> GetFlightAsync(Guid id, CancellationToken ct = default) =>
        api.GetAsync<FlightDetail>($"/operations/flights/{id}", ct);

    public Task<IReadOnlyList<FlightTimelineEntryModel>> GetFlightTimelineAsync(Guid id, CancellationToken ct = default) =>
        api.GetAsync<IReadOnlyList<FlightTimelineEntryModel>>($"/operations/flights/{id}/timeline", ct);

    public Task<IReadOnlyList<AssignedEmployeeModel>> GetFlightInviteOptionsAsync(Guid id, CancellationToken ct = default) =>
        api.GetAsync<IReadOnlyList<AssignedEmployeeModel>>($"/operations/flights/{id}/invite-options", ct);

    public Task<Guid> ScheduleFlightAsync(ScheduleFlightRequestModel request, CancellationToken ct = default) =>
        api.PostAsync<ScheduleFlightRequestModel, Guid>("/operations/flights", request, ct);

    public Task<IReadOnlyList<Guid>> ScheduleFlightsAsync(ScheduleFlightsRequestModel request, CancellationToken ct = default) =>
        api.PostAsync<ScheduleFlightsRequestModel, IReadOnlyList<Guid>>("/operations/flights/bulk", request, ct);

    public Task UpdateFlightAsync(Guid id, UpdateScheduledFlightRequestModel request, string rowVersion, CancellationToken ct = default) =>
        api.PutAsync($"/operations/flights/{id}", request, rowVersion, ct);

    public Task ChangeFlightNumberAsync(Guid id, ChangeFlightNumberRequestModel request, string rowVersion, CancellationToken ct = default) =>
        api.PostAsync($"/operations/flights/{id}/change-number", request, rowVersion, ct);

    public Task AssignEmployeesAsync(Guid id, AssignEmployeesRequestModel request, string rowVersion, CancellationToken ct = default) =>
        api.PostAsync($"/operations/flights/{id}/assign", request, rowVersion, ct);

    public Task InviteEmployeesAsync(Guid id, AssignEmployeesRequestModel request, string rowVersion, CancellationToken ct = default) =>
        api.PostAsync($"/operations/flights/{id}/invite", request, rowVersion, ct);

    public Task ClaimPerLandingFlightAsync(Guid id, string rowVersion, CancellationToken ct = default) =>
        api.PostAsync($"/operations/flights/{id}/claim", rowVersion, ct);

    public Task<IReadOnlyList<DuplicateCandidate>> GetDuplicateCandidatesAsync(
        Guid customerId,
        DateTimeOffset scheduledArrivalUtc,
        DateTimeOffset scheduledDepartureUtc,
        Guid? stationId = null,
        Guid? excludeFlightId = null,
        CancellationToken ct = default)
    {
        var query = new QueryBuilder()
            .Add("customerId", customerId)
            .Add("stationId", stationId)
            .Add("scheduledArrivalUtc", scheduledArrivalUtc)
            .Add("scheduledDepartureUtc", scheduledDepartureUtc)
            .Add("excludeFlightId", excludeFlightId)
            .Build();
        return api.GetAsync<IReadOnlyList<DuplicateCandidate>>($"/operations/flights/duplicate-candidates{query}", ct);
    }

    public Task MergeFlightsAsync(Guid survivorFlightId, Guid loserFlightId, CancellationToken ct = default) =>
        api.PostAsync("/operations/flights/merge", new MergeFlightsRequestModel(survivorFlightId, loserFlightId), ct);

    public Task<Guid> CreateAdHocWorkOrderAsync(CreateAdHocWorkOrderRequestModel request, CancellationToken ct = default) =>
        api.PostAsync<CreateAdHocWorkOrderRequestModel, Guid>("/operations/work-orders/from-scratch", request, ct);

    public Task<Guid> SubmitWorkOrderAsync(Guid flightId, WorkOrderRequestModel request, CancellationToken ct = default) =>
        api.PostAsync<WorkOrderRequestModel, Guid>($"/operations/flights/{flightId}/work-orders", request, ct);

    public Task<Guid> MergeWorkOrdersAsync(Guid flightId, MergeWorkOrdersRequestModel request, CancellationToken ct = default) =>
        api.PostAsync<MergeWorkOrdersRequestModel, Guid>($"/operations/flights/{flightId}/work-orders/merge", request, ct);

    public Task<WorkOrderSummaryModel?> GetMyWorkOrderForFlightAsync(Guid flightId, CancellationToken ct = default) =>
        api.GetAsync<WorkOrderSummaryModel?>($"/operations/flights/{flightId}/work-orders/mine", ct);

    public Task DownloadApprovedWorkOrderAsync(Guid flightId, CancellationToken ct = default) =>
        api.DownloadFileAsync(
            $"/operations/flights/{flightId}/work-orders/approved/pdf",
            fallbackFileName: "approved-work-order.pdf",
            cancellationToken: ct);

    public Task<PagedResult<WorkOrderListItem>> GetWorkOrdersAsync(
        int page,
        int pageSize,
        string? search = null,
        Guid? stationId = null,
        string? status = null,
        string? type = null,
        Guid? flightId = null,
        Guid? ownerUserId = null,
        string? sort = null,
        CancellationToken ct = default)
    {
        var query = new QueryBuilder()
            .Add("page", page)
            .Add("pageSize", pageSize)
            .Add("search", search)
            .Add("stationId", stationId)
            .Add("status", status)
            .Add("type", type)
            .Add("flightId", flightId)
            .Add("ownerUserId", ownerUserId)
            .Add("sort", sort)
            .Build();
        return api.GetAsync<PagedResult<WorkOrderListItem>>($"/operations/work-orders{query}", ct);
    }

    public Task<WorkOrderDetail> GetWorkOrderAsync(Guid id, CancellationToken ct = default) =>
        api.GetAsync<WorkOrderDetail>($"/operations/work-orders/{id}", ct);

    public Task<IReadOnlyList<WorkOrderTimelineEntryModel>> GetWorkOrderTimelineAsync(Guid id, CancellationToken ct = default) =>
        api.GetAsync<IReadOnlyList<WorkOrderTimelineEntryModel>>($"/operations/work-orders/{id}/timeline", ct);

    public Task UpdateWorkOrderAsync(Guid id, WorkOrderRequestModel request, string rowVersion, CancellationToken ct = default) =>
        api.PutAsync($"/operations/work-orders/{id}", request, rowVersion, ct);

    public Task DeleteWorkOrderAsync(Guid id, string rowVersion, CancellationToken ct = default) =>
        api.DeleteAsync($"/operations/work-orders/{id}", rowVersion, ct);

    public Task ApproveWorkOrderAsync(Guid id, string rowVersion, CancellationToken ct = default) =>
        api.PostAsync($"/operations/work-orders/{id}/approve", rowVersion, ct);

    public Task ReturnWorkOrderAsync(Guid id, ReturnWorkOrderRequestModel request, string rowVersion, CancellationToken ct = default) =>
        api.PostAsync($"/operations/work-orders/{id}/return", request, rowVersion, ct);

    public Task UploadWorkOrderAttachmentAsync(
        Guid workOrderId,
        Guid taskId,
        string kind,
        byte[] content,
        string fileName,
        string contentType,
        string rowVersion,
        CancellationToken ct = default) =>
        api.UploadFileAsync(
            $"/operations/work-orders/{workOrderId}/tasks/{taskId}/attachments",
            content,
            fileName,
            contentType,
            rowVersion,
            ct,
            new Dictionary<string, string> { ["kind"] = kind });

    public Task<BrowserFileContent> DownloadWorkOrderAttachmentAsync(Guid workOrderId, Guid taskId, Guid attachmentId, CancellationToken ct = default) =>
        api.GetFileAsync($"/operations/work-orders/{workOrderId}/tasks/{taskId}/attachments/{attachmentId}", ct);

    public Task DeleteWorkOrderAttachmentAsync(Guid workOrderId, Guid taskId, Guid attachmentId, string rowVersion, CancellationToken ct = default) =>
        api.DeleteAsync($"/operations/work-orders/{workOrderId}/tasks/{taskId}/attachments/{attachmentId}", rowVersion, ct);

    public Task UploadWorkOrderSignatureAsync(Guid workOrderId, byte[] content, string fileName, string contentType, string rowVersion, CancellationToken ct = default) =>
        api.UploadFileAsync($"/operations/work-orders/{workOrderId}/signature", content, fileName, contentType, rowVersion, ct);

    public Task<BrowserFileContent> DownloadWorkOrderSignatureAsync(Guid workOrderId, CancellationToken ct = default) =>
        api.GetFileAsync($"/operations/work-orders/{workOrderId}/signature", ct);

    public Task DeleteWorkOrderSignatureAsync(Guid workOrderId, string rowVersion, CancellationToken ct = default) =>
        api.DeleteAsync($"/operations/work-orders/{workOrderId}/signature", rowVersion, ct);

    private static string? JoinValues(IReadOnlyList<string>? values) =>
        values is { Count: > 0 } ? string.Join(',', values) : null;

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
