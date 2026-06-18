using BuildingBlocks.Application.Abstractions.Queries;
using Operations.Contracts.Dashboard;

namespace Operations.Application.Features.Dashboard.Queries.GetOperationsDashboard;

/// <summary>
/// Aggregated operational metrics over the last <paramref name="LookBackDays"/> days
/// powering the home dashboard. Cheap aggregate queries — single GroupBy per slice.
/// </summary>
public sealed record GetOperationsDashboardQuery(int LookBackDays = 30)
    : IQuery<OperationsDashboardDto>;
