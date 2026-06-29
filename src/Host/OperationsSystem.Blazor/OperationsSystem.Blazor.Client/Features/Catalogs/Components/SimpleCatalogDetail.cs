namespace OperationsSystem.Blazor.Client.Features.Catalogs.Components;

internal sealed record SimpleCatalogDetail(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    string RowVersion);
