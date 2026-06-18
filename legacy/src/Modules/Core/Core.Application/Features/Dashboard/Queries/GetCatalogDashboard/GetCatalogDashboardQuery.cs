using BuildingBlocks.Application.Abstractions.Queries;
using Core.Contracts.Features.Dashboard;

namespace Core.Application.Features.Dashboard.Queries.GetCatalogDashboard;

/// <summary>
/// Snapshot of master/reference data for the home dashboard. Counts are point-in-time
/// totals (no period filter).
/// </summary>
public sealed record GetCatalogDashboardQuery() : IQuery<CatalogDashboardDto>;
