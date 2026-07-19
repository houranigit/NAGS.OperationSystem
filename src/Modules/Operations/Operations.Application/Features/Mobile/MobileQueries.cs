using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Domain.Results;
using MasterData.Contracts.Readers;
using MasterData.Contracts.Seeding;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Application.Authorization;
using Operations.Application.Contracts;
using Operations.Application.Features.WorkOrders;
using Operations.Domain.Enumerations;
using Operations.Domain.Flights;
using Operations.Domain.WorkOrders;

namespace Operations.Application.Features.Mobile;

/// <summary>
/// Guards shared by every mobile query: the mobile surface serves station staff working at one
/// station, so the caller must resolve to an active StaffMember + Station. Administrators and
/// customer contacts are denied — they use the web portal.
/// </summary>
internal static class MobileScope
{
    public static Result<OperationsScopeContext> EnsureStationStaff(Result<OperationsScopeContext> scopeResult)
    {
        if (scopeResult.IsFailure)
            return scopeResult;

        var scope = scopeResult.Value;
        return scope is { StationId: not null, StaffMemberId: not null }
            ? scope
            : Error.Forbidden(
                "The mobile app requires an account linked to an active staff member.",
                "Operations.Mobile.StaffLinkRequired");
    }
}

// --- Me ----------------------------------------------------------------------

public sealed record GetMobileMeQuery : IQuery<MobileMeDto>;

public sealed class GetMobileMeQueryHandler(IOperationsScope scope, IMasterDataReader masterData)
    : IQueryHandler<GetMobileMeQuery, MobileMeDto>
{
    public async Task<Result<MobileMeDto>> Handle(GetMobileMeQuery request, CancellationToken cancellationToken)
    {
        var scopeResult = MobileScope.EnsureStationStaff(await scope.ResolveAsync(cancellationToken));
        if (scopeResult.IsFailure)
            return scopeResult.Error;

        var staff = await masterData.GetStaffMemberAsync(scopeResult.Value.StaffMemberId!.Value, cancellationToken);
        if (staff is null)
            return Error.NotFound("Staff member not found.", "Operations.Mobile.StaffNotFound");

        var station = await masterData.GetStationAsync(staff.StationId, cancellationToken);
        if (station is null)
            return Error.NotFound("Station not found.", "Operations.Mobile.StationNotFound");

        var manpowerType = await masterData.GetManpowerTypeAsync(staff.ManpowerTypeId, cancellationToken);

        return new MobileMeDto(
            staff.Id,
            staff.FullName,
            staff.EmployeeId,
            station.Id,
            station.IataCode,
            station.Name,
            staff.ManpowerTypeId,
            manpowerType?.Name);
    }
}

// --- Catalogs ------------------------------------------------------------------

public sealed record GetMobileCatalogsQuery : IQuery<MobileCatalogsDto>;

public sealed class GetMobileCatalogsQueryHandler(
    IOperationsScope scope,
    IMasterDataReader masterData,
    TimeProvider timeProvider)
    : IQueryHandler<GetMobileCatalogsQuery, MobileCatalogsDto>
{
    public async Task<Result<MobileCatalogsDto>> Handle(GetMobileCatalogsQuery request, CancellationToken cancellationToken)
    {
        var scopeResult = MobileScope.EnsureStationStaff(await scope.ResolveAsync(cancellationToken));
        if (scopeResult.IsFailure)
            return scopeResult.Error;

        var services = await masterData.GetActiveServicesAsync(cancellationToken);
        var allowedPerformedServiceIds = await masterData.GetAllowedActiveServiceIdsAsync(
            scopeResult.Value.ManpowerTypeId!.Value,
            cancellationToken);
        var tools = await masterData.GetActiveToolsAsync(cancellationToken);
        var materials = await masterData.GetActiveMaterialsAsync(cancellationToken);
        var generalSupports = await masterData.GetActiveGeneralSupportsAsync(cancellationToken);
        var customers = await masterData.GetActiveCustomersAsync(cancellationToken);
        var aircraftTypes = await masterData.GetActiveAircraftTypesAsync(cancellationToken);

        return new MobileCatalogsDto(
            services.Select(s => new MobileServiceCatalogItemDto(
                s.Id, s.Name, s.Id == WellKnownMasterDataIds.AircraftPerLandingService)).ToList(),
            allowedPerformedServiceIds.OrderBy(id => id).ToList(),
            tools.Select(t => new MobileCatalogItemDto(t.Id, t.Name)).ToList(),
            materials.Select(m => new MobileCatalogItemDto(m.Id, m.Name)).ToList(),
            generalSupports.Select(g => new MobileCatalogItemDto(g.Id, g.Name)).ToList(),
            customers.Select(c => new MobileCustomerDto(c.Id, c.IataCode, c.Name)).ToList(),
            aircraftTypes.Select(a => new MobileAircraftTypeDto(a.Id, a.Manufacturer, a.Model)).ToList(),
            timeProvider.GetUtcNow());
    }
}

// --- Station staff roster -------------------------------------------------------

public sealed record GetMobileStationStaffQuery : IQuery<IReadOnlyList<AssignedEmployeeDto>>;

public sealed class GetMobileStationStaffQueryHandler(IOperationsScope scope, IMasterDataReader masterData)
    : IQueryHandler<GetMobileStationStaffQuery, IReadOnlyList<AssignedEmployeeDto>>
{
    public async Task<Result<IReadOnlyList<AssignedEmployeeDto>>> Handle(
        GetMobileStationStaffQuery request, CancellationToken cancellationToken)
    {
        var scopeResult = MobileScope.EnsureStationStaff(await scope.ResolveAsync(cancellationToken));
        if (scopeResult.IsFailure)
            return scopeResult.Error;

        var staff = await masterData.GetActiveStaffMembersForStationAsync(scopeResult.Value.StationId!.Value, cancellationToken);
        IReadOnlyList<AssignedEmployeeDto> items = staff
            .Select(member => new AssignedEmployeeDto(member.Id, member.FullName, member.EmployeeId))
            .ToList();

        return Result.Success(items);
    }
}

// --- Flight lists ------------------------------------------------------------------

/// <summary>Which mobile cache table the list feeds.</summary>
public enum MobileFlightList
{
    /// <summary>Non-Per-Landing, non-Ad-Hoc flights the caller is rostered on (Room: flights_my).</summary>
    My = 0,

    /// <summary>Per-Landing flights at the caller's station, station-wide by nature (Room: flights_per_landing).</summary>
    PerLanding = 1,

    /// <summary>Ad Hoc operation-type flights at the caller's station (Room: flights_ad_hoc).</summary>
    AdHoc = 2
}

public sealed record GetMobileFlightsQuery(MobileFlightList List)
    : IQuery<IReadOnlyList<MobileFlightDto>>;

public sealed class GetMobileFlightsQueryHandler(
    IOperationsDbContext db,
    IOperationsScope scope,
    IUserContext user,
    TimeProvider timeProvider)
    : IQueryHandler<GetMobileFlightsQuery, IReadOnlyList<MobileFlightDto>>
{
    public async Task<Result<IReadOnlyList<MobileFlightDto>>> Handle(
        GetMobileFlightsQuery request, CancellationToken cancellationToken)
    {
        var scopeResult = MobileScope.EnsureStationStaff(await scope.ResolveAsync(cancellationToken));
        if (scopeResult.IsFailure)
            return scopeResult.Error;
        var context = scopeResult.Value;

        var now = timeProvider.GetUtcNow();
        var window = TimeSpan.FromHours(MobileFlightWindow.DefaultHours);
        var fromUtc = now - window;
        var toUtc = now + window;

        var query = db.Flights.AsNoTracking()
            .Include(f => f.PlannedServices)
            .Include(f => f.AssignedEmployees)
            .Where(f =>
                f.Status == FlightStatus.Scheduled ||
                f.Status == FlightStatus.InProgress ||
                f.Status == FlightStatus.Completed)
            .Where(f => f.Station.StationId == context.StationId)
            .Where(f => f.Schedule.Sta >= fromUtc && f.Schedule.Sta <= toUtc);

        var staffId = context.StaffMemberId!.Value;
        query = request.List switch
        {
            MobileFlightList.My => query
                .Where(f => !f.PlannedServices.Any(p => p.Service.ServiceId == WellKnownMasterDataIds.AircraftPerLandingService))
                .Where(f => f.OperationType.OperationTypeId != WellKnownMasterDataIds.AdHocOperationType)
                .Where(f => f.AssignedEmployees.Any(e => e.Employee.StaffMemberId == staffId)),
            MobileFlightList.PerLanding => query
                .Where(f => f.PlannedServices.Any(p => p.Service.ServiceId == WellKnownMasterDataIds.AircraftPerLandingService))
                .Where(f => f.OperationType.OperationTypeId != WellKnownMasterDataIds.AdHocOperationType),
            MobileFlightList.AdHoc => query
                .Where(f => f.OperationType.OperationTypeId == WellKnownMasterDataIds.AdHocOperationType),
            _ => query.Where(_ => false)
        };

        var flights = await query
            .OrderBy(f => f.Schedule.Sta).ThenBy(f => f.Id)
            .ToListAsync(cancellationToken);

        var dtos = await MobileFlightDtoMapper.MapWithWorkOrdersAsync(
            db,
            flights,
            user.UserId,
            now,
            cancellationToken);
        return Result.Success(dtos);
    }
}

// --- Flight by id (realtime apply + notification detail; intentionally not window-filtered) ---

public sealed record GetMobileFlightByIdQuery(Guid Id) : IQuery<MobileFlightDto>;

public sealed class GetMobileFlightByIdQueryHandler(
    IOperationsDbContext db,
    IOperationsScope scope,
    IUserContext user,
    TimeProvider timeProvider)
    : IQueryHandler<GetMobileFlightByIdQuery, MobileFlightDto>
{
    public async Task<Result<MobileFlightDto>> Handle(GetMobileFlightByIdQuery request, CancellationToken cancellationToken)
    {
        var scopeResult = MobileScope.EnsureStationStaff(await scope.ResolveAsync(cancellationToken));
        if (scopeResult.IsFailure)
            return scopeResult.Error;

        var flight = await db.Flights.AsNoTracking()
            .Include(f => f.PlannedServices)
            .Include(f => f.AssignedEmployees)
            .FirstOrDefaultAsync(f => f.Id == request.Id, cancellationToken);
        if (flight is null)
            return Error.NotFound("Flight not found.", "Operations.Flight.NotFound");

        // Ad Hoc list membership is station-wide, so its realtime station broadcast must be able
        // to re-fetch the row by id even when the recipient is not assigned. Keep ordinary
        // non-Per-Landing flights on the existing assigned/station-wide access rule.
        var isSameStationAdHoc =
            flight.Station.StationId == scopeResult.Value.StationId &&
            flight.OperationType.OperationTypeId == WellKnownMasterDataIds.AdHocOperationType;
        var access = isSameStationAdHoc
            ? Result.Success()
            : scopeResult.Value.EnsureFlightAccess(flight);
        if (access.IsFailure)
            return access.Error;

        var dtos = await MobileFlightDtoMapper.MapWithWorkOrdersAsync(
            db,
            [flight],
            user.UserId,
            timeProvider.GetUtcNow(),
            cancellationToken);
        return dtos[0];
    }
}

// --- My work order for a flight -----------------------------------------------------

public sealed record GetMobileMyWorkOrderForFlightQuery(Guid FlightId) : IQuery<WorkOrderDetailDto?>;

public sealed class GetMobileMyWorkOrderForFlightQueryHandler(
    IOperationsDbContext db,
    IOperationsScope scope,
    IUserContext user)
    : IQueryHandler<GetMobileMyWorkOrderForFlightQuery, WorkOrderDetailDto?>
{
    public async Task<Result<WorkOrderDetailDto?>> Handle(
        GetMobileMyWorkOrderForFlightQuery request, CancellationToken cancellationToken)
    {
        var scopeResult = MobileScope.EnsureStationStaff(await scope.ResolveAsync(cancellationToken));
        if (scopeResult.IsFailure)
            return scopeResult.Error;

        if (user.UserId is not { } userId)
            return Error.Forbidden("The request is not authenticated.", "Operations.WorkOrder.Unauthenticated");

        var workOrder = await WorkOrderLoader.ForMutation(db.WorkOrders.AsNoTracking())
            .Where(w => w.FlightId == request.FlightId && w.OwnerUserId == userId)
            .Where(w => w.Status == WorkOrderStatus.Submitted || w.Status == WorkOrderStatus.Returned || w.Status == WorkOrderStatus.Approved)
            .OrderByDescending(w => w.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return workOrder is null
            ? Result.Success<WorkOrderDetailDto?>(null)
            : Result.Success<WorkOrderDetailDto?>(WorkOrderDtoMapper.Detail(workOrder));
    }
}

// --- Shared flight mapping -----------------------------------------------------------

internal static class MobileFlightDtoMapper
{
    /// <summary>
    /// Maps loaded flights to mobile DTOs, embedding the caller's active work order per flight
    /// (full detail, for offline form hydration) and whether other users' active work orders exist.
    /// </summary>
    public static async Task<IReadOnlyList<MobileFlightDto>> MapWithWorkOrdersAsync(
        IOperationsDbContext db,
        IReadOnlyList<Flight> flights,
        Guid? callerUserId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (flights.Count == 0)
            return [];

        var flightIds = flights.Select(f => f.Id).ToList();

        var myWorkOrders = new Dictionary<Guid, WorkOrder>();
        var otherWorkOrderFlightIds = new HashSet<Guid>();

        if (callerUserId is { } userId)
        {
            var mine = await WorkOrderLoader.ForMutation(db.WorkOrders.AsNoTracking())
                .Where(w => flightIds.Contains(w.FlightId) && w.OwnerUserId == userId)
                .Where(w => w.Status == WorkOrderStatus.Submitted || w.Status == WorkOrderStatus.Returned || w.Status == WorkOrderStatus.Approved)
                .ToListAsync(cancellationToken);

            foreach (var group in mine.GroupBy(w => w.FlightId))
                myWorkOrders[group.Key] = group.OrderByDescending(w => w.CreatedAtUtc).First();

            var others = await db.WorkOrders.AsNoTracking()
                .Where(w => flightIds.Contains(w.FlightId) && w.OwnerUserId != userId)
                .Where(w => w.Status == WorkOrderStatus.Submitted || w.Status == WorkOrderStatus.Returned || w.Status == WorkOrderStatus.Approved)
                .Select(w => w.FlightId)
                .Distinct()
                .ToListAsync(cancellationToken);
            otherWorkOrderFlightIds = [.. others];
        }

        return flights
            .Select(flight => Map(
                flight,
                myWorkOrders.TryGetValue(flight.Id, out var mine) ? mine : null,
                otherWorkOrderFlightIds.Contains(flight.Id),
                nowUtc))
            .ToList();
    }

    private static MobileFlightDto Map(
        Flight flight,
        WorkOrder? myWorkOrder,
        bool otherWorkOrdersExist,
        DateTimeOffset nowUtc)
    {
        var mobileWindow = MobileFlightWindow.Evaluate(flight.Schedule.Sta, nowUtc);

        return new(
            flight.Id,
            flight.FlightNumber.Value,
            flight.OriginalFlightNumber,
            flight.Customer.CustomerId,
            flight.Customer.IataCode,
            flight.Customer.Name,
            flight.Station.StationId,
            flight.Station.IataCode,
            flight.OperationType.OperationTypeId,
            flight.OperationType.Name,
            flight.AircraftType?.AircraftTypeId,
            flight.AircraftType?.Model,
            flight.Schedule.Sta,
            flight.Schedule.Std,
            flight.Status.ToString(),
            flight.IsPerLanding,
            flight.OperationType.OperationTypeId == WellKnownMasterDataIds.AdHocOperationType,
            flight.PlannedServices.Select(p => new PlannedServiceDto(p.Service.ServiceId, p.Service.Name, p.IsAircraftPerLanding)).ToList(),
            flight.AssignedEmployees.Select(e => new AssignedEmployeeDto(e.Employee.StaffMemberId, e.Employee.FullName, e.Employee.EmployeeId)).ToList(),
            myWorkOrder is null ? null : WorkOrderDtoMapper.Detail(myWorkOrder),
            otherWorkOrdersExist,
            flight.UpdatedAtUtc,
            Convert.ToBase64String(flight.RowVersion),
            mobileWindow.IsWithinWindow,
            mobileWindow.StartsAtUtc,
            mobileWindow.EndsAtUtc);
    }
}
