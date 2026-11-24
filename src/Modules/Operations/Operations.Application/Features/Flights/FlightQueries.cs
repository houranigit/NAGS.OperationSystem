using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Pagination;
using BuildingBlocks.Domain.Results;
using Identity.Contracts;
using MasterData.Contracts.Readers;
using MasterData.Contracts.Seeding;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Application.Authorization;
using Operations.Application.Common;
using Operations.Application.Contracts;
using Operations.Domain.Flights;

namespace Operations.Application.Features.Flights;

public enum FlightListType
{
    All,
    Scheduled,
    PerLanding,
    AdHoc
}

public sealed record GetFlightsQuery(
    int Page = 1,
    int PageSize = 20,
    string? Search = null,
    Guid? StationId = null,
    Guid? CustomerId = null,
    Guid? OperationTypeId = null,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    string? Sort = null,
    IReadOnlyList<FlightReportStatus>? Statuses = null,
    FlightListType FlightType = FlightListType.All) : IQuery<PagedResult<FlightListItemDto>>;

public sealed class GetFlightsQueryHandler(IOperationsDbContext db, IOperationsScope scope, ICustomerLogoReader logos,
    IMasterDataReader masterData, TimeProvider? timeProvider = null)
    : IQueryHandler<GetFlightsQuery, PagedResult<FlightListItemDto>>
{
    public async Task<Result<PagedResult<FlightListItemDto>>> Handle(GetFlightsQuery request, CancellationToken cancellationToken)
    {
        var scopeResult = await scope.ResolveAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;

        var paging = PageRequest.From(request.Page, request.PageSize);
        var flights = FlightListQuery.ApplyScopeAndFilters(db.Flights.AsNoTracking(), scopeResult.Value,
            new FlightListFilter(request.Search, request.StationId, request.CustomerId,
                request.OperationTypeId, request.FromUtc, request.ToUtc));
        flights = request.FlightType switch
        {
            FlightListType.Scheduled => flights.Where(flight =>
                flight.OperationType.OperationTypeId != WellKnownMasterDataIds.AdHocOperationType &&
                !flight.PlannedServices.Any(service => service.Service.ServiceId == WellKnownMasterDataIds.AircraftPerLandingService)),
            FlightListType.PerLanding => flights.Where(flight =>
                flight.OperationType.OperationTypeId != WellKnownMasterDataIds.AdHocOperationType &&
                flight.PlannedServices.Any(service => service.Service.ServiceId == WellKnownMasterDataIds.AircraftPerLandingService)),
            FlightListType.AdHoc => flights.Where(flight => flight.OperationType.OperationTypeId == WellKnownMasterDataIds.AdHocOperationType),
            _ => flights
        };
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        var dueSoonBy = now.AddMinutes(30);
        // Keep the report precedence and STA thresholds in sync with Android's FlightCardReportStatus.
        var query =
            from flight in FlightListQuery.ApplySort(flights, request.Sort)
            let hasArrival = flight.ArrivalSubmissions.Any()
            let hasDeparture = flight.DepartureSubmissions.Any()
            let hasCancellation = flight.CancellationSubmissions.Any()
            select new
            {
                flight.Id,
                FlightNumber = flight.FlightNumber.Value,
                CustomerIataCode = flight.Customer.IataCode,
                CustomerName = flight.Customer.Name,
                flight.Customer.CustomerId,
                StationIataCode = flight.Station.IataCode,
                flight.Station.StationId,
                OperationTypeName = flight.OperationType.Name,
                ScheduledArrivalUtc = flight.Schedule.Sta,
                ScheduledDepartureUtc = flight.Schedule.Std,
                IsPerLanding = flight.PlannedServices.Any(service => service.Service.ServiceId == WellKnownMasterDataIds.AircraftPerLandingService),
                AircraftTypeModel = flight.AircraftType == null ? null : flight.AircraftType.Model,
                HasArrivalSubmission = hasArrival,
                HasDepartureSubmission = hasDeparture,
                HasCancellationSubmission = hasCancellation,
                flight.ApprovalStatus,
                flight.ApprovedByDisplayName,
                flight.ApprovedAtUtc,
                flight.ApprovedReturnToRampSequence,
                flight.ApprovalNumber,
                Status = hasCancellation && (hasArrival || hasDeparture || flight.ReturnToRamps.Any()) ? FlightReportStatus.Conflict
                    : hasCancellation ? FlightReportStatus.Canceled
                    : flight.ReturnToRamps.Any(ramp => ramp.DepartureDetails == null) ? FlightReportStatus.ReturnedToRamp
                    : hasDeparture ? FlightReportStatus.Departed
                    : hasArrival ? FlightReportStatus.OnGround
                    : flight.Schedule.Sta <= now ? FlightReportStatus.ArrivalNotRecorded
                    : flight.Schedule.Sta <= dueSoonBy ? FlightReportStatus.DueSoon
                    : FlightReportStatus.Scheduled
            };
        if (request.Statuses is { Count: > 0 } statuses)
            query = query.Where(item => statuses.Contains(item.Status));

        var total = await query.LongCountAsync(cancellationToken);
        if (paging.IsOutOfRange(total))
            return paging.Empty<FlightListItemDto>(total);

        var items = await query.Skip(paging.Skip).Take(paging.PageSize).ToListAsync(cancellationToken);
        var customerIds = items.Select(item => item.CustomerId).Distinct().ToArray();
        var versions = await logos.GetLogoVersionsAsync(customerIds, cancellationToken);
        var stations = await masterData.GetStationsAsync(items.Select(item => item.StationId).Distinct().ToArray(), cancellationToken);
        var stationTimeZones = stations.ToDictionary(station => station.Id, station => station.TimeZoneId);
        if (items.Any(item => !stationTimeZones.TryGetValue(item.StationId, out var zone) || string.IsNullOrWhiteSpace(zone)))
            return Error.Failure("Flight data could not be loaded. Please try again.", "Operations.Flight.StationDataUnavailable");

        var versionedItems = items.Select(item => new FlightListItemDto(
            item.Id, item.FlightNumber, item.CustomerIataCode, item.CustomerName, item.StationIataCode,
            item.OperationTypeName, item.ScheduledArrivalUtc, item.ScheduledDepartureUtc, item.IsPerLanding,
            stationTimeZones[item.StationId], item.AircraftTypeModel, item.CustomerId,
            versions.TryGetValue(item.CustomerId, out var version) ? version : CustomerLogoVersions.None,
            item.StationId, item.HasArrivalSubmission, item.HasDepartureSubmission, item.HasCancellationSubmission,
            item.Status.ToString(), item.ApprovalStatus, item.ApprovedByDisplayName, item.ApprovedAtUtc,
            item.ApprovedReturnToRampSequence, item.ApprovalNumber)).ToList();
        return paging.ToResult(versionedItems, total);
    }
}

public sealed record GetFlightByIdQuery(Guid Id) : IQuery<FlightDetailDto>;

public sealed class GetFlightByIdQueryHandler(IOperationsDbContext db, IOperationsScope scope, IMasterDataReader masterData, IUserDisplayNameReader? userNames = null)
    : IQueryHandler<GetFlightByIdQuery, FlightDetailDto>
{
    public async Task<Result<FlightDetailDto>> Handle(GetFlightByIdQuery request, CancellationToken cancellationToken)
    {
        var flight = await db.Flights.AsNoTracking().AsSplitQuery()
            .Include(f => f.PlannedServices)
            .Include(f => f.AssignedEmployees)
            .Include(f => f.ArrivalSubmissions)
            .Include(f => f.DepartureSubmissions)
            .Include(f => f.CancellationSubmissions)
            .Include(f => f.ReturnToRamps)
            .Include(f => f.ServiceRecords).ThenInclude(s => s.Performers)
            .Include(f => f.ServiceRecords).ThenInclude(s => s.Attachments)
            .Include(f => f.TaskRecords).ThenInclude(t => t.Performers)
            .Include(f => f.TaskRecords).ThenInclude(t => t.Tools)
            .Include(f => f.TaskRecords).ThenInclude(t => t.Materials)
            .Include(f => f.TaskRecords).ThenInclude(t => t.GeneralSupports)
            .Include(f => f.TaskRecords).ThenInclude(t => t.Attachments)
            .FirstOrDefaultAsync(f => f.Id == request.Id, cancellationToken);
        if (flight is null)
            return Error.NotFound("Flight not found.", "Operations.Flight.NotFound");

        var scopeResult = await scope.ResolveAsync(cancellationToken);
        if (scopeResult.IsFailure)
            return scopeResult.Error;
        var access = scopeResult.Value.EnsureFlightAccess(flight);
        if (access.IsFailure)
            return access.Error;

        var station = await masterData.GetStationAsync(flight.Station.StationId, cancellationToken);
        if (station is null || string.IsNullOrWhiteSpace(station.TimeZoneId))
            return Error.Failure("Flight data could not be loaded. Please try again.", "Operations.Flight.StationDataUnavailable");
        var names = await FlightWorkAttribution.NamesAsync(userNames, FlightWorkAttribution.UserIds(flight), cancellationToken);
        return FlightDetails.Map(flight, station.TimeZoneId, userDisplayNames: names);
    }
}

internal static class FlightDetails
{
    internal static FlightDetailDto MapAdministrator(Flight flight, string stationTimeZoneId,
        IReadOnlyDictionary<Guid, string> names, bool canCorrect, Guid currentUserId, bool canReview = true)
    {
        var canSaveWork = canCorrect && flight.CancellationSubmissions.Count == 0;
        var canSaveActuals = canCorrect && !flight.CancellationSubmissions.Any(item => item.IsOwnedBy(null, currentUserId));
        var canSaveCancellation = canCorrect && flight.ServiceRecords.Count == 0 && flight.TaskRecords.Count == 0 &&
            flight.ReturnToRamps.Count == 0 && (flight.CancellationSubmissions.Count > 0 || !flight.HasSubmittedActuals(null, currentUserId));
        return Map(flight, stationTimeZoneId,
            canSaveArrival: canSaveActuals,
            canSaveDeparture: canSaveActuals,
            canSaveCancellation: canSaveCancellation,
            canSaveServices: canSaveWork,
            currentUserId: currentUserId,
            canSaveTasks: canSaveWork,
            assignedWorkCorrection: canSaveWork,
            userDisplayNames: names, creatorOnlyWorkDelete: true, unrestrictedServices: true, canReview: canReview);
    }

    internal static FlightDetailDto MapOperational(Flight flight, string stationTimeZoneId, MobileFlightScopeContext actor,
        bool canOperate, IReadOnlyList<Guid> allowedServiceIds, IReadOnlyDictionary<Guid, string> names, bool portalOnly, bool canReview = false)
    {
        var staffId = actor.StaffMemberId;
        var ownCancellation = flight.CancellationSubmissions.Any(item => item.SubmittedByEmployee?.StaffMemberId == staffId);
        var ownWork = flight.ArrivalSubmissions.Any(item => item.SubmittedByEmployee?.StaffMemberId == staffId) ||
            flight.DepartureSubmissions.Any(item => item.SubmittedByEmployee?.StaffMemberId == staffId);
        var canSaveWork = canOperate && flight.CancellationSubmissions.Count == 0;
        var canSaveCancellation = canOperate && !ownWork && flight.ServiceRecords.Count == 0 && flight.TaskRecords.Count == 0 &&
            (!portalOnly || flight.ReturnToRamps.Count == 0);
        return Map(flight, stationTimeZoneId, staffId, canOperate && !ownCancellation, canOperate && !ownCancellation,
            canSaveCancellation, canSaveWork && allowedServiceIds.Count > 0, actor.UserId, allowedServiceIds,
            canSaveWork, flight.CanCorrectOperationalWork(staffId), names, canEditFlightDetails: canOperate, creatorOnlyWorkDelete: portalOnly,
            legacyStaffAttribution: !portalOnly, canReview: canReview);
    }

    public static FlightDetailDto Map(Flight flight, string stationTimeZoneId, Guid? currentStaffMemberId = null, bool canSaveArrival = false, bool canSaveDeparture = false, bool canSaveCancellation = false, bool canSaveServices = false, Guid? currentUserId = null, IReadOnlyList<Guid>? allowedServiceIds = null, bool canSaveTasks = false, bool assignedWorkCorrection = false, IReadOnlyDictionary<Guid, string>? userDisplayNames = null, bool canEditFlightDetails = false, bool creatorOnlyWorkDelete = false, bool unrestrictedServices = false, bool legacyStaffAttribution = false, bool canReview = false)
    {
        var canSaveReturnsToRamp = canSaveTasks && flight.CancellationSubmissions.Count == 0 && flight.DepartureSubmissions.Count > 0 &&
            (flight.EnsureApprovalAllowsReturnToRamp(Guid.Empty).IsSuccess ||
                flight.ReturnToRamps.Any(occurrence => flight.EnsureApprovalAllowsReturnToRamp(occurrence.Id).IsSuccess));
        var canShare = canSaveTasks && flight.CancellationSubmissions.Count == 0 && currentStaffMemberId is { } sharingStaffId &&
            flight.OperationType.OperationTypeId != WellKnownMasterDataIds.AdHocOperationType && !flight.IsPerLanding &&
            flight.AssignedEmployees.Any(employee => employee.Employee.StaffMemberId == sharingStaffId);
        if (flight.IsApprovalLocked)
        {
            canSaveArrival = false;
            canSaveDeparture = false;
            canSaveCancellation = false;
            canSaveServices = false;
            canSaveTasks = false;
            canEditFlightDetails = false;
        }

        var search = currentStaffMemberId is { } staffId && FlightSearchPolicy.IsUnassignedRegular(flight, staffId);
        var canAdd = !search || !flight.HasOperationalRecords();
        var serviceWrites = canSaveServices;
        var taskWrites = canSaveTasks;
        if (search)
        {
            canSaveArrival &= canAdd || flight.ArrivalSubmissions.Any(item => item.SubmittedByEmployee?.StaffMemberId == currentStaffMemberId);
            canSaveDeparture &= flight.DepartureSubmissions.Count == 0 || flight.DepartureSubmissions.Any(item => item.SubmittedByEmployee?.StaffMemberId == currentStaffMemberId);
            canSaveCancellation &= flight.CancellationSubmissions.Any(item => item.SubmittedByEmployee?.StaffMemberId == currentStaffMemberId);
            serviceWrites = canSaveTasks && (canAdd && canSaveServices || flight.Services.Any(item => item.Window.ToUtc is null ||
                item.CreatedByUserId == currentUserId && allowedServiceIds?.Contains(item.Service.ServiceId) == true));
            taskWrites &= canAdd || flight.Tasks.Any(item => item.Window.ToUtc is null || item.CreatedByUserId == currentUserId);
        }
        return new(flight.Id, flight.FlightNumber.Value,
            flight.Customer.CustomerId, flight.Customer.IataCode, flight.Customer.Name,
            flight.Station.StationId, flight.Station.IataCode, flight.Station.Name,
            flight.OperationType.OperationTypeId, flight.OperationType.Name,
            flight.AircraftType?.AircraftTypeId, flight.AircraftType?.Model,
            flight.Schedule.Sta, flight.Schedule.Std, flight.IsPerLanding,
            flight.PlannedServices.Select(service => new PlannedServiceDto(service.Service.ServiceId,
                service.Service.Name, service.IsAircraftPerLanding)).ToList(),
            flight.AssignedEmployees.Select(employee => new AssignedEmployeeDto(employee.Employee.StaffMemberId,
                employee.Employee.FullName, employee.Employee.EmployeeId)).ToList(),
            flight.CreatedAtUtc, flight.UpdatedAtUtc, Convert.ToBase64String(flight.RowVersion), stationTimeZoneId,
            flight.ArrivalSubmissions.OrderBy(a => a.CreatedAtUtc).ThenBy(a => a.Id).Select(item => FlightArrivalSubmissionDto.Map(item, userDisplayNames, legacyStaffAttribution)).ToList(),
            currentStaffMemberId, canSaveArrival, flight.CancellationSubmissions.Count > 0,
            flight.DepartureSubmissions.OrderBy(d => d.CreatedAtUtc).ThenBy(d => d.Id).Select(item => FlightDepartureSubmissionDto.Map(item, userDisplayNames, legacyStaffAttribution)).ToList(), canSaveDeparture,
            flight.CancellationSubmissions.OrderBy(c => c.CreatedAtUtc).ThenBy(c => c.Id)
                .Select(c => FlightCancellationSubmissionDto.Map(c, userDisplayNames, legacyStaffAttribution)).ToList(), canSaveCancellation,
            flight.Services.OrderBy(s => s.CreatedAtUtc).ThenBy(s => s.Id).Select(s => FlightServiceDto.Map(s, currentUserId, serviceWrites && (unrestrictedServices || allowedServiceIds?.Contains(s.Service.ServiceId) == true || search && s.Window.ToUtc is null), assignedWorkCorrection,
                userDisplayNames?.GetValueOrDefault(s.CreatedByUserId), s.EndedByUserId is { } closer ? userDisplayNames?.GetValueOrDefault(closer) : null,
                s.UpdatedByUserId is { } updater ? userDisplayNames?.GetValueOrDefault(updater) : null,
                closeOnly: search && (s.CreatedByUserId != currentUserId || allowedServiceIds?.Contains(s.Service.ServiceId) != true),
                creatorOnlyDelete: creatorOnlyWorkDelete)).ToList(),
            serviceWrites, allowedServiceIds, flight.Tasks.OrderBy(t => t.CreatedAtUtc).ThenBy(t => t.Id).Select(t => FlightTaskDto.Map(t, currentUserId, taskWrites, assignedWorkCorrection,
                userDisplayNames?.GetValueOrDefault(t.CreatedByUserId), t.EndedByUserId is { } closer ? userDisplayNames?.GetValueOrDefault(closer) : null,
                t.UpdatedByUserId is { } updater ? userDisplayNames?.GetValueOrDefault(updater) : null,
                closeOnly: search && t.CreatedByUserId != currentUserId, creatorOnlyDelete: creatorOnlyWorkDelete)).ToList(), taskWrites,
            flight.CreatedByUserId, userDisplayNames?.GetValueOrDefault(flight.CreatedByUserId),
            canEditFlightDetails && currentUserId is { } userId && flight.CanEditAdHocDetails(userId),
            flight.OperationType.OperationTypeId == WellKnownMasterDataIds.AdHocOperationType, search, canSaveServices && canAdd, canSaveTasks && canAdd,
            flight.ReturnToRamps.OrderBy(r => r.Sequence).Select(r => FlightReturnToRampDto.Map(flight, r, currentUserId,
                canSaveReturnsToRamp && flight.EnsureApprovalAllowsReturnToRamp(r.Id).IsSuccess,
                allowedServiceIds, userDisplayNames, creatorOnlyWorkDelete, unrestrictedServices)).ToList(),
            canSaveReturnsToRamp, flight.LastReturnToRampSequence, canShare,
            flight.ApprovalStatus, flight.ApprovedByDisplayName, flight.ApprovedAtUtc, flight.ApprovedReturnToRampSequence,
            canReview && flight.ApprovalStatus != FlightApprovalStatus.Approved,
            canReview && flight.ApprovalStatus == FlightApprovalStatus.Approved,
            flight.ApprovalNumber);
    }
}

internal sealed record FlightListFilter(
    string? Search,
    Guid? StationId,
    Guid? CustomerId,
    Guid? OperationTypeId,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc);

internal static class FlightListQuery
{
    public static IQueryable<Flight> ApplyScopeAndFilters(
        IQueryable<Flight> query,
        OperationsScopeContext scope,
        FlightListFilter filter)
    {
        // Station staff see their station's Per-Landing flights (station-wide) plus the flights they
        // are assigned to; holders of view-station (station dispatchers) see every flight at their
        // station; global readers see everything (optionally filtered).
        if (!scope.HasGlobalReadAccess && scope.StationId.HasValue)
            query = ApplyPortalVisibility(query, scope);
        else if (filter.StationId is { } filterStation)
            query = query.Where(f => f.Station.StationId == filterStation);

        if (filter.CustomerId is { } customer)
            query = query.Where(f => f.Customer.CustomerId == customer);
        if (filter.OperationTypeId is { } operationType)
            query = query.Where(f => f.OperationType.OperationTypeId == operationType);
        if (filter.FromUtc is { } from)
            query = query.Where(f => f.Schedule.Sta >= from);
        if (filter.ToUtc is { } to)
            query = query.Where(f => f.Schedule.Sta <= to);
        return ApplySearch(query, filter.Search);
    }

    internal static IQueryable<Flight> ApplyPortalVisibility(IQueryable<Flight> query, OperationsScopeContext scope)
    {
        if (scope.HasGlobalReadAccess)
            return query;

        var stationId = scope.StationId;
        query = query.Where(f => f.Station.StationId == stationId);
        if (scope.CanViewStationWide)
            return query;

        var staffId = scope.StaffMemberId;
        return query.Where(f =>
            f.PlannedServices.Any(p => p.Service.ServiceId == WellKnownMasterDataIds.AircraftPerLandingService) ||
            f.AssignedEmployees.Any(e => e.Employee.StaffMemberId == staffId));
    }

    public static IQueryable<Flight> ApplySearch(IQueryable<Flight> query, string? search)
    {
        if (SearchFilter.Term(search) is { } term)
        {
            var compactFlightTerm = CompactFlightSearchTerm(term);
            var flightIdTerm = ParseFlightIdSearchTerm(term);
            var hasFlightIdTerm = flightIdTerm.HasValue;
            var flightId = flightIdTerm.GetValueOrDefault();
            query = query.Where(f =>
                (hasFlightIdTerm && f.Id == flightId) ||
                f.FlightNumber.Value.ToLower().Contains(term) ||
                f.Customer.Name.ToLower().Contains(term) ||
                (f.Customer.IataCode != null &&
                 ((f.Customer.IataCode.ToLower() + "-" + f.FlightNumber.Value.ToLower()).Contains(term) ||
                  (f.Customer.IataCode.ToLower() + f.FlightNumber.Value.ToLower()).Contains(compactFlightTerm))));
        }

        return query;
    }

    public static IOrderedQueryable<Flight> ApplySort(IQueryable<Flight> query, string? sort)
    {
        if (SortSpec.Parse(sort) is not { } spec)
            return DefaultSort(query);

        return spec.Field switch
        {
            "flightnumber" => spec.Descending
                ? query.OrderByDescending(f => f.FlightNumber.Value).ThenByDescending(f => f.Id)
                : query.OrderBy(f => f.FlightNumber.Value).ThenBy(f => f.Id),
            "customername" => spec.Descending
                ? query.OrderByDescending(f => f.Customer.Name).ThenByDescending(f => f.Id)
                : query.OrderBy(f => f.Customer.Name).ThenBy(f => f.Id),
            "stationiata" or "stationiatacode" => spec.Descending
                ? query.OrderByDescending(f => f.Station.IataCode).ThenByDescending(f => f.Id)
                : query.OrderBy(f => f.Station.IataCode).ThenBy(f => f.Id),
            "operationtypename" => spec.Descending
                ? query.OrderByDescending(f => f.OperationType.Name).ThenByDescending(f => f.Id)
                : query.OrderBy(f => f.OperationType.Name).ThenBy(f => f.Id),
            "scheduledarrivalutc" => spec.Descending
                ? query.OrderByDescending(f => f.Schedule.Sta).ThenByDescending(f => f.Id)
                : query.OrderBy(f => f.Schedule.Sta).ThenBy(f => f.Id),
            "scheduleddepartureutc" => spec.Descending
                ? query.OrderByDescending(f => f.Schedule.Std).ThenByDescending(f => f.Id)
                : query.OrderBy(f => f.Schedule.Std).ThenBy(f => f.Id),
            _ => DefaultSort(query)
        };
    }

    private static IOrderedQueryable<Flight> DefaultSort(IQueryable<Flight> query) =>
        query.OrderByDescending(f => f.Schedule.Sta).ThenBy(f => f.Id);

    private static string CompactFlightSearchTerm(string term) =>
        term.Replace("-", string.Empty).Replace(" ", string.Empty);

    private static Guid? ParseFlightIdSearchTerm(string term)
    {
        if (Guid.TryParse(term, out var flightId))
            return flightId;

        var compactTerm = CompactFlightSearchTerm(term);
        return compactTerm.Length == 32 && Guid.TryParseExact(compactTerm, "N", out flightId)
            ? flightId
            : null;
    }
}
