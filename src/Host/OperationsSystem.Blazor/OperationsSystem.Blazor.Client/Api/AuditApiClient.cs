using System.Globalization;

namespace OperationsSystem.Blazor.Client.Api;

/// <summary>Typed access to the Audit module API (<c>/api/v1/audit</c>). Administrator-only.</summary>
public sealed class AuditApiClient(BrowserApiClient api)
{
    public Task<PagedResult<AuditTrailListItem>> GetTrailsAsync(
        int page = 1,
        int pageSize = 20,
        string? subjectType = null,
        Guid? subjectId = null,
        string? entityType = null,
        Guid? entityId = null,
        Guid? actorId = null,
        string? action = null,
        string? sort = null,
        CancellationToken ct = default)
    {
        var parts = new List<string>
        {
            $"page={page.ToString(CultureInfo.InvariantCulture)}",
            $"pageSize={pageSize.ToString(CultureInfo.InvariantCulture)}"
        };
        AddIfSet(parts, "subjectType", subjectType);
        AddIfSet(parts, "subjectId", subjectId?.ToString());
        AddIfSet(parts, "entityType", entityType);
        AddIfSet(parts, "entityId", entityId?.ToString());
        AddIfSet(parts, "actorId", actorId?.ToString());
        AddIfSet(parts, "action", action);
        AddIfSet(parts, "sort", sort);

        return api.GetAsync<PagedResult<AuditTrailListItem>>($"/audit/trails?{string.Join('&', parts)}", ct);
    }

    public Task<AuditTrailDetail> GetTrailAsync(Guid id, CancellationToken ct = default) =>
        api.GetAsync<AuditTrailDetail>($"/audit/trails/{id}", ct);

    private static void AddIfSet(List<string> parts, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            parts.Add($"{key}={Uri.EscapeDataString(value)}");
    }
}
