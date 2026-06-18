using BuildingBlocks.Application.Abstractions.Queries;
using BuildingBlocks.Domain.Results;
using Core.Contracts.Features.Employee;
using Core.Contracts.Readers;
using Microsoft.Extensions.DependencyInjection;
using Operations.Contracts.Mobile;
using Store.Contracts.Readers;

namespace Operations.Application.Features.Mobile.Queries.GetMobileLookups;

/// <summary>
/// Returns every catalog the mobile work-order forms (scheduled and ad-hoc) need so
/// the Android client can hydrate every dropdown from local cache while offline.
/// Catalog queries run in parallel again; each invocation uses its own DI scope so
/// readers resolve separate <c>DbContext</c> instances and EF Core does not see
/// concurrent use of the HTTP request's shared contexts.
/// </summary>
public sealed class GetMobileLookupsQueryHandler(IServiceScopeFactory scopeFactory)
    : IQueryHandler<GetMobileLookupsQuery, MobileLookupsDto>
{
    public async Task<Result<MobileLookupsDto>> Handle(
        GetMobileLookupsQuery request,
        CancellationToken cancellationToken)
    {
        var aircraftTypesTask = InvokeScopedAsync(
            scopeFactory,
            static async (IAircraftTypeReader r, CancellationToken ct) =>
                await r.ListActiveAsync(ct).ConfigureAwait(false),
            cancellationToken);

        // Mobile work-order pickers never need AOG (work orders can't bill AOG), so we
        // exclude the seed row at the SQL layer — it never enters the result set, no
        // wire bytes, no Room rows.
        var servicesTask = InvokeScopedAsync(
            scopeFactory,
            static async (IServiceReader r, CancellationToken ct) =>
                await r.ListActiveAsync(excludeAog: true, ct).ConfigureAwait(false),
            cancellationToken);

        var toolsTask = InvokeScopedAsync(
            scopeFactory,
            static async (IToolReader r, CancellationToken ct) =>
                await r.ListActiveAsync(ct).ConfigureAwait(false),
            cancellationToken);

        var materialsTask = InvokeScopedAsync(
            scopeFactory,
            static async (IMaterialReader r, CancellationToken ct) =>
                await r.ListActiveAsync(ct).ConfigureAwait(false),
            cancellationToken);

        var generalSupportsTask = InvokeScopedAsync(
            scopeFactory,
            static async (IGeneralSupportReader r, CancellationToken ct) =>
                await r.ListActiveAsync(ct).ConfigureAwait(false),
            cancellationToken);

        var customersTask = InvokeScopedAsync(
            scopeFactory,
            static async (ICustomerReader r, CancellationToken ct) =>
                await r.ListActiveAsync(ct).ConfigureAwait(false),
            cancellationToken);

        Task<IReadOnlyList<EmployeeSnapshot>> stationEmployeesTask =
            request.StationId == Guid.Empty
                ? Task.FromResult<IReadOnlyList<EmployeeSnapshot>>(Array.Empty<EmployeeSnapshot>())
                : InvokeScopedAsync(
                    scopeFactory,
                    async (IEmployeeReader r, CancellationToken ct) =>
                        await r.SearchActiveSnapshotsByStationAsync(
                            request.StationId,
                            search: null,
                            take: 500,
                            ct).ConfigureAwait(false),
                    cancellationToken);

        await Task.WhenAll(
            aircraftTypesTask,
            servicesTask,
            toolsTask,
            materialsTask,
            generalSupportsTask,
            customersTask,
            stationEmployeesTask).ConfigureAwait(false);

        return new MobileLookupsDto(
            await aircraftTypesTask.ConfigureAwait(false),
            // AOG is already excluded at the SQL layer via `ListActiveAsync(excludeAog: true)`
            // above — no further filtering needed here.
            await servicesTask.ConfigureAwait(false),
            await toolsTask.ConfigureAwait(false),
            await materialsTask.ConfigureAwait(false),
            await generalSupportsTask.ConfigureAwait(false),
            await customersTask.ConfigureAwait(false),
            await stationEmployeesTask.ConfigureAwait(false),
            DateTimeOffset.UtcNow);
    }

    private static async Task<T> InvokeScopedAsync<TReader, T>(
        IServiceScopeFactory scopeFactory,
        Func<TReader, CancellationToken, Task<T>> invoke,
        CancellationToken cancellationToken)
        where TReader : notnull
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<TReader>();
        return await invoke(reader, cancellationToken).ConfigureAwait(false);
    }
}
