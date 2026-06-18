using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Domain.Results;
using MediatR;
using Operations.Application.Features.Flight.Queries.GetMobileFlightContext;
using Operations.Application.Features.Flight.Queries.GetMyAssignedFlightsForMobile;
using Operations.Application.Features.Flight.Queries.GetMyStationAogFlights;
using Operations.Application.Features.Mobile.Queries.GetMobileLookups;
using Operations.Contracts.Mobile;

namespace Operations.Application.Features.Mobile.Queries.GetMobileBootstrap;

/// <summary>
/// Composes the mobile bootstrap by delegating to the existing single-purpose queries
/// (lookups + my flights + per-flight context + AOG flights at my station) so the
/// contract for each individual screen stays identical and the mobile client can fall
/// back to the N+1 calls on older builds. Per-flight contexts are fetched sequentially —
/// each query reuses the same scoped <c>IOperationsDbContext</c> instance and EF's
/// concurrency detector forbids parallel access. Each call is cheap so sequential is
/// fine.
/// </summary>
public sealed class GetMobileBootstrapQueryHandler(ISender sender)
    : IQueryHandler<GetMobileBootstrapQuery, MobileBootstrapDto>
{
    public async Task<Result<MobileBootstrapDto>> Handle(
        GetMobileBootstrapQuery request,
        CancellationToken cancellationToken)
    {
        if (request.EmployeeId == Guid.Empty)
            return Error.Validation("Employee id is required.");

        var lookupsResult = await sender.Send(new GetMobileLookupsQuery(request.StationId), cancellationToken);
        if (!lookupsResult.IsSuccess)
            return lookupsResult.Error;

        // The mobile UI never paginates past the first page in practice (12-24h window
        // rarely exceeds 100 flights for one employee) — we ask for the maximum the
        // upstream query allows so the bootstrap actually returns "everything" the
        // user can act on offline. AOG defaults to false so the Scheduled tab and the
        // bootstrap stay aligned with the new "non-AOG only" semantic.
        var flightsResult = await sender.Send(
            new GetMyAssignedFlightsForMobileQuery(
                request.EmployeeId,
                Page: 1,
                PageSize: 100,
                Search: null,
                WindowHours: request.WindowHours,
                Status: null,
                IncludeAog: false),
            cancellationToken);
        if (!flightsResult.IsSuccess)
            return flightsResult.Error;

        var aogResult = await sender.Send(
            new GetMyStationAogFlightsQuery(request.EmployeeId, request.WindowHours),
            cancellationToken);
        if (!aogResult.IsSuccess)
            return aogResult.Error;

        var flights = await ResolveContextsAsync(flightsResult.Value!.Items, request.EmployeeId, cancellationToken);

        // The v2 AOG endpoint dropped the per-flight Services list (the AOG chip is
        // implicit on the AOG tab). Bootstrap, however, still ships a single uniform
        // flight DTO so the legacy v1 client can iterate one list, so we widen the
        // AOG rows back into MobileFlightSummaryDto with an empty Services array.
        // No information is lost — every flight here has the AOG service by construction.
        var aogSummaries = aogResult.Value!
            .Select(a => new MobileFlightSummaryDto(
                a.Id,
                a.FlightNumber,
                a.CustomerName,
                a.CustomerIataCode,
                a.StationCode,
                a.OperationTypeCode,
                a.Sta,
                a.Std,
                a.AircraftModel,
                a.Status,
                a.CanceledAt,
                a.AssignedEmployeesCount,
                a.MyWorkOrder,
                a.OtherWorkOrdersExist,
                Array.Empty<MobileFlightServiceDto>(),
                Array.Empty<MobileFlightAssignedEmployeeDto>()))
            .ToList();
        var aogFlights = await ResolveContextsAsync(aogSummaries, request.EmployeeId, cancellationToken);

        return new MobileBootstrapDto(lookupsResult.Value!, flights, aogFlights, DateTimeOffset.UtcNow);
    }

    private async Task<IReadOnlyList<MobileBootstrapFlightDto>> ResolveContextsAsync(
        IReadOnlyList<MobileFlightSummaryDto> summaries,
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        // Per-flight context queries run sequentially: <see cref="IOperationsDbContext"/>
        // is scoped per request, so issuing multiple <see cref="GetMobileFlightContextQuery"/>
        // calls in parallel reuses the same DbContext instance and trips EF Core's
        // concurrency detector. Each call is fast (a few projections), so sequential
        // keeps things simple and avoids dragging in a child IServiceScopeFactory for
        // parallel scopes.
        var flights = new List<MobileBootstrapFlightDto>(summaries.Count);
        foreach (var summary in summaries)
        {
            var contextResult = await sender.Send(
                new GetMobileFlightContextQuery(summary.Id, employeeId),
                cancellationToken);

            // Skip a flight when its context cannot be resolved (e.g. it was canceled
            // and the assignment row was concurrently removed) instead of failing the
            // whole bootstrap — the mobile client treats missing flights as removed.
            if (!contextResult.IsSuccess)
                continue;

            flights.Add(new MobileBootstrapFlightDto(summary, contextResult.Value!));
        }

        return flights;
    }
}
