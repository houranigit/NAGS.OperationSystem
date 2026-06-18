using BuildingBlocks.Application.Abstractions;
using Store.Domain.Aggregates.GeneralSupport;
using Store.Domain.Aggregates.GeneralSupportPricePlan;
using Store.Domain.Aggregates.Material;
using Store.Domain.Aggregates.MaterialPricePlan;
using Store.Domain.Aggregates.Tool;
using Store.Domain.Aggregates.ToolPricePlan;
using Store.Domain.Aggregates.Unit;
using Unit = Store.Domain.Aggregates.Unit.Unit;

namespace Store.Application.Abstractions;

/// <summary>Read/query surface for the Store module's aggregates.</summary>
public interface IStoreDbContext : IUnitOfWork
{
    IQueryable<Unit> Units { get; }
    IQueryable<Tool> Tools { get; }
    IQueryable<Material> Materials { get; }
    IQueryable<GeneralSupport> GeneralSupports { get; }
    IQueryable<ToolPricePlan> ToolPricePlans { get; }
    IQueryable<MaterialPricePlan> MaterialPricePlans { get; }
    IQueryable<GeneralSupportPricePlan> GeneralSupportPricePlans { get; }

    /// <summary>True if an inbox row already exists for the given EventId — handlers should short-circuit.</summary>
    Task<bool> IsAlreadyProcessedAsync(Guid eventId, CancellationToken cancellationToken = default);

    /// <summary>Adds an inbox row keyed by <paramref name="eventId"/>.</summary>
    void MarkProcessed(Guid eventId, string eventType);
}
