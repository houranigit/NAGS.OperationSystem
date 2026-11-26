using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Domain.Results;
using MasterData.Contracts.Readers;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Application.Authorization;
using Operations.Application.Contracts;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;

namespace Operations.Application.Features.Flights;

public sealed record GetFlightsExportQuery(
    string? Search,
    Guid? StationId,
    Guid? CustomerId,
    Guid? OperationTypeId,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    string? Sort,
    IReadOnlyList<FlightReportStatus>? Statuses,
    FlightListType FlightType,
    bool IncludeManpower) : IQuery<IReadOnlyList<FlightExportRowDto>>;

public sealed class GetFlightsExportQueryHandler(
    IOperationsDbContext db, IOperationsScope scope, IMasterDataReader masterData, TimeProvider clock)
    : IQueryHandler<GetFlightsExportQuery, IReadOnlyList<FlightExportRowDto>>
{
    public async Task<Result<IReadOnlyList<FlightExportRowDto>>> Handle(GetFlightsExportQuery request, CancellationToken ct)
    {
        var resolved = await scope.ResolveAsync(ct);
        if (resolved.IsFailure)
            return resolved.Error;

        var visible = FlightListQuery.ApplyScopeAndFilters(db.Flights.AsNoTracking(), resolved.Value,
            new FlightListFilter(request.Search, request.StationId, request.CustomerId, request.OperationTypeId, request.FromUtc, request.ToUtc));
        var matches = await FlightListQuery.MatchReportFilters(FlightListQuery.ApplySort(visible, request.Sort),
                request.FlightType, request.Statuses, clock.GetUtcNow())
            .Select(item => new { item.Flight.Id, item.Status, item.IsPerLanding }).ToListAsync(ct);
        if (matches.Count == 0)
            return Array.Empty<FlightExportRowDto>();

        var ids = matches.Select(match => match.Id).ToArray();
        // Reapply the same scoped base query when hydrating the operational snapshot.
        var flights = await visible.Where(flight => ids.Contains(flight.Id)).AsSplitQuery()
            .Include(flight => flight.PlannedServices)
            .Include(flight => flight.AssignedEmployees)
            .Include(flight => flight.ArrivalSubmissions)
            .Include(flight => flight.DepartureSubmissions)
            .Include(flight => flight.CancellationSubmissions)
            .Include(flight => flight.ReturnToRamps)
            .Include(flight => flight.ServiceRecords).ThenInclude(service => service.Performers)
            .Include(flight => flight.TaskRecords).ThenInclude(task => task.Performers)
            .Include(flight => flight.TaskRecords).ThenInclude(task => task.Tools)
            .Include(flight => flight.TaskRecords).ThenInclude(task => task.Materials)
            .Include(flight => flight.TaskRecords).ThenInclude(task => task.GeneralSupports)
            .ToDictionaryAsync(flight => flight.Id, ct);

        var stations = await masterData.GetStationsAsync(flights.Values.Select(flight => flight.Station.StationId).Distinct().ToArray(), ct);
        var stationZones = stations.ToDictionary(station => station.Id, station => station.TimeZoneId);
        if (flights.Values.Any(flight => !stationZones.TryGetValue(flight.Station.StationId, out var zone) || string.IsNullOrWhiteSpace(zone)))
            return Error.Failure("Flight data could not be loaded. Please try again.", "Operations.Flight.StationDataUnavailable");

        var manpower = request.IncludeManpower ? await LoadManpowerTypesAsync(flights.Values, ct) : null;
        return matches.Where(match => flights.ContainsKey(match.Id)).Select(match =>
        {
            var flight = flights[match.Id];
            return MapRow(flight, stationZones[flight.Station.StationId], match.Status, match.IsPerLanding, manpower);
        }).ToArray();
    }

    private static FlightExportRowDto MapRow(Flight flight, string stationZone, FlightReportStatus status, bool isPerLanding,
        IReadOnlyDictionary<Guid, string>? manpower) =>
        new(flight.Id, flight.FlightNumber.Value, flight.Customer.IataCode, flight.Customer.Name, flight.Station.IataCode,
            flight.Station.Name, flight.OperationType.Name, flight.Schedule.Sta, flight.Schedule.Std, stationZone,
            status.ToString(), isPerLanding, Names(flight.PlannedServices.Select(service => service.Service.Name)),
            Names(flight.AssignedEmployees.Select(assignment => assignment.Employee.FullName)), MapWork(flight, manpower))
        {
            CustomerId = flight.Customer.CustomerId,
            StationId = flight.Station.StationId,
            OperationTypeId = flight.OperationType.OperationTypeId,
            PlannedServiceIds = flight.PlannedServices.Select(service => service.Service.ServiceId).Distinct().ToArray(),
            AssignedStaffMemberIds = flight.AssignedEmployees.Select(assignment => assignment.Employee.StaffMemberId).Distinct().ToArray()
        };

    private static ApprovedWorkOrderExportDto? MapWork(Flight flight, IReadOnlyDictionary<Guid, string>? manpower)
    {
        if (!flight.HasOperationalRecords() && flight.ApprovalNumber is null)
            return null;

        var arrival = flight.ArrivalSubmissions.OrderByDescending(item => item.IsAccepted)
            .ThenBy(item => item.CreatedAtUtc).ThenBy(item => item.Id).FirstOrDefault()?.Details;
        var departure = flight.DepartureSubmissions.OrderByDescending(item => item.IsAccepted)
            .ThenBy(item => item.CreatedAtUtc).ThenBy(item => item.Id).FirstOrDefault()?.Details;
        var cancellation = flight.CancellationSubmissions.OrderByDescending(item => item.IsAccepted)
            .ThenBy(item => item.CreatedAtUtc).ThenBy(item => item.Id).FirstOrDefault()?.Details;
        var ramps = flight.ReturnToRamps.ToDictionary(ramp => ramp.Id, ramp =>
            new FlightExportReturnToRampContextDto(ramp.Id, ramp.Sequence, ramp.ArrivalDetails.ArrivedAtUtc,
                ramp.DepartureDetails?.DepartedAtUtc, Details(ramp.ArrivalDetails.Remarks, ramp.DepartureDetails?.Remarks)));

        var services = flight.ServiceRecords.OrderBy(service => service.Window.FromUtc).ThenBy(service => service.Id)
            .Select(service => new FlightExportServiceDetailDto(service.Id, service.Service.ServiceId, service.Service.Name,
                service.Window.FromUtc, service.Window.ToUtc, Names(service.Performers.Select(person => person.Employee.FullName)),
                service.Notes, service.ReturnToRampId is { } ramp ? ramps.GetValueOrDefault(ramp) : null)).ToArray();
        var tasks = flight.TaskRecords.OrderBy(task => task.Window.FromUtc).ThenBy(task => task.Id)
            .Select(task => new FlightExportTaskDetailDto(task.Id, task.TaskType.ToString(), task.Notes ?? task.AtaChapter?.Title,
                task.Window.FromUtc, task.Window.ToUtc, Names(task.Performers.Select(person => person.Employee.FullName)),
                task.Tools.Select(item => Resource(item.Tool.ToolId, item.Tool.Name, item.Usage)).ToArray(),
                task.Materials.Select(item => Resource(item.Material.MaterialId, item.Material.Name, item.Usage)).ToArray(),
                task.GeneralSupports.Select(item => Resource(item.GeneralSupport.GeneralSupportId, item.GeneralSupport.Name, item.Usage)).ToArray(),
                task.ReturnToRampId is { } ramp ? ramps.GetValueOrDefault(ramp) : null)).ToArray();

        return new(flight.ApprovalNumber, flight.FlightNumber.Value, arrival?.Ata, departure?.Atd,
            arrival?.AircraftType.Manufacturer, arrival?.AircraftType.Model, arrival?.AircraftTailNumber,
            Names(services.Select(service => service.ServiceName)),
            Names(tasks.SelectMany(task => task.Tools.Select(item => item.Name))),
            Names(tasks.SelectMany(task => task.Materials.Select(item => item.Name))),
            Names(tasks.SelectMany(task => task.GeneralSupports.Select(item => item.Name))),
            Details(arrival?.Remarks, departure?.Remarks, cancellation?.Remarks))
        {
            AircraftTypeId = arrival?.AircraftType.AircraftTypeId,
            WorkOrderStatus = flight.ApprovalStatus.ToString(),
            ServiceDetails = services,
            TaskDetails = tasks,
            TaskNames = Names(tasks.Select(task => string.IsNullOrWhiteSpace(task.Description)
                ? task.TaskType : $"{task.TaskType} - {task.Description}")),
            ManpowerDetails = manpower is null ? [] : Performers(flight).OrderBy(person => person.Window.FromUtc)
                .ThenBy(person => person.Window.ToUtc).ThenBy(person => person.StaffMemberId)
                .Select(person => new FlightExportManpowerDetailDto(manpower.GetValueOrDefault(person.StaffMemberId) ?? string.Empty,
                    person.Window.FromUtc, person.Window.ToUtc)).ToArray()
        };
    }

    private async Task<IReadOnlyDictionary<Guid, string>> LoadManpowerTypesAsync(IEnumerable<Flight> flights, CancellationToken ct)
    {
        var ids = flights.SelectMany(Performers).Select(person => person.StaffMemberId).Distinct().ToArray();
        if (ids.Length == 0)
            return new Dictionary<Guid, string>();

        var staff = await masterData.GetStaffMembersAsync(ids, ct);
        var types = new Dictionary<Guid, string>();
        foreach (var type in staff.Select(person => person.ManpowerTypeId).Distinct())
        {
            if (await masterData.GetManpowerTypeAsync(type, ct) is { } found)
                types[type] = found.Name;
        }
        return staff.Where(person => types.ContainsKey(person.ManpowerTypeId))
            .ToDictionary(person => person.Id, person => types[person.ManpowerTypeId]);
    }

    private static IEnumerable<(Guid StaffMemberId, TimeWindow Window)> Performers(Flight flight) =>
        flight.ServiceRecords.SelectMany(service => service.Performers.Select(person => (person.Employee.StaffMemberId, person.Window)))
            .Concat(flight.TaskRecords.SelectMany(task => task.Performers.Select(person => (person.Employee.StaffMemberId, person.Window))));

    private static FlightExportResourceUsageDto Resource(Guid id, string name, ResourceUsage usage) =>
        new(id, name, usage.CalculationType, usage.Quantity, usage.FromUtc, usage.ToUtc);

    private static string[] Names(IEnumerable<string> names) => names.Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.OrdinalIgnoreCase).ToArray();

    private static string? Details(params string?[] details)
    {
        var text = string.Join("\n", details.Where(detail => !string.IsNullOrWhiteSpace(detail)).Distinct());
        return text.Length == 0 ? null : text;
    }
}
