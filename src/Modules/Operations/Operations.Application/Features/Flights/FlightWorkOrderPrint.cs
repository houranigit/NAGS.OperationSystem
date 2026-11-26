using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Domain.Results;
using Identity.Contracts;
using MasterData.Contracts.Readers;
using Microsoft.EntityFrameworkCore;
using Operations.Application.Abstractions;
using Operations.Application.Authorization;
using Operations.Application.Common;
using Operations.Application.Contracts;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;

namespace Operations.Application.Features.Flights;

public interface IFlightWorkOrderPdfRenderer
{
    public WorkOrderPrintFile Render(WorkOrderPrintDto source);
}

public sealed record GetFlightWorkOrderPrintQuery(Guid Id, bool MobileOnly = false) : IQuery<WorkOrderPrintDto>;

public sealed class GetFlightWorkOrderPrintQueryHandler(IOperationsDbContext db, IOperationsScope scope, IMobileFlightScope mobileScope,
    FlightWorkOrderPrintSource printSource)
    : IQueryHandler<GetFlightWorkOrderPrintQuery, WorkOrderPrintDto>
{
    public async Task<Result<WorkOrderPrintDto>> Handle(GetFlightWorkOrderPrintQuery request, CancellationToken ct)
    {
        var flights = db.Flights.AsNoTracking();
        IQueryable<Flight> visible;
        if (request.MobileOnly)
        {
            var resolved = await mobileScope.ResolveAsync(ct);
            if (resolved.IsFailure)
                return resolved.Error;
            visible = MobileFlightQuery.ApplyReadScope(flights, resolved.Value);
        }
        else
        {
            var resolved = await scope.ResolveAsync(ct);
            if (resolved.IsFailure)
                return resolved.Error;
            visible = FlightListQuery.ApplyPortalVisibility(flights, resolved.Value);
        }

        var flight = await FlightServiceWrites.Load(visible).SingleOrDefaultAsync(f => f.Id == request.Id, ct);
        if (flight is null)
            return Error.NotFound("Flight not found.", "Operations.Flight.NotFound");

        return await printSource.BuildAsync(flight, ct);
    }
}

public sealed class FlightWorkOrderPrintSource(IMasterDataReader masterData, IFileStorage storage, IUserDisplayNameReader userNames)
{
    public async Task<Result<WorkOrderPrintDto>> BuildAsync(Flight flight, CancellationToken ct)
    {
        var station = await masterData.GetStationAsync(flight.Station.StationId, ct);
        if (station is null || string.IsNullOrWhiteSpace(station.TimeZoneId))
            return Error.Failure("Flight data could not be loaded. Please try again.", "Operations.Flight.StationDataUnavailable");

        var names = await FlightWorkAttribution.NamesAsync(userNames, FlightWorkAttribution.UserIds(flight), ct);
        var arrival = flight.ArrivalSubmissions.OrderByDescending(item => item.IsAccepted)
            .ThenBy(item => item.CreatedAtUtc).ThenBy(item => item.Id).FirstOrDefault()?.Details;
        var departure = flight.DepartureSubmissions.OrderByDescending(item => item.IsAccepted)
            .ThenBy(item => item.CreatedAtUtc).ThenBy(item => item.Id).FirstOrDefault()?.Details;

        var reports = flight.ArrivalSubmissions.Select(item => new WorkOrderPrintSubmissionDto("Arrival", item.Details.Ata,
                Person(item.Details.ReceivedByEmployee), item.SubmittedByEmployee?.FullName ?? names.GetValueOrDefault(item.CreatedByUserId),
                item.CreatedAtUtc, item.Details.Remarks))
            .Concat(flight.DepartureSubmissions.Select(item => new WorkOrderPrintSubmissionDto("Departure", item.Details.Atd,
                Person(item.Details.HandledByEmployee), item.SubmittedByEmployee?.FullName ?? names.GetValueOrDefault(item.CreatedByUserId),
                item.CreatedAtUtc, item.Details.Remarks)))
            .Concat(flight.CancellationSubmissions.Select(item => new WorkOrderPrintSubmissionDto("Cancellation", item.Details.CanceledAtUtc,
                null, item.SubmittedByEmployee?.FullName ?? names.GetValueOrDefault(item.CreatedByUserId), item.CreatedAtUtc, item.Details.Remarks)))
            .OrderBy(item => item.SubmittedAtUtc).ThenBy(item => item.Type, StringComparer.Ordinal).ToList();

        var firstRecordedWork = reports.Select(item => (At: item.SubmittedAtUtc, By: item.RecordedBy))
            .Concat(flight.ServiceRecords.Select(item => (At: item.CreatedAtUtc, By: names.GetValueOrDefault(item.CreatedByUserId))))
            .Concat(flight.TaskRecords.Select(item => (At: item.CreatedAtUtc, By: names.GetValueOrDefault(item.CreatedByUserId))))
            .Concat(flight.ReturnToRamps.Select(item => (At: item.CreatedAtUtc, By: names.GetValueOrDefault(item.CreatedByUserId))))
            .OrderBy(item => item.At).Select(item => (At: (DateTimeOffset?)item.At, item.By)).FirstOrDefault();

        var rampSignatures = new Dictionary<Guid, byte[]>();
        var ramps = new List<WorkOrderReturnToRampDto>();
        foreach (var ramp in flight.ReturnToRamps.OrderBy(item => item.Sequence))
        {
            var signature = ramp.DepartureDetails?.CustomerSignature;
            if (await ReadSignatureAsync(signature, ct) is { } content)
                rampSignatures[ramp.Id] = content;
            var arrivalSummary = $"Received by {Person(ramp.ArrivalDetails.ReceivedByEmployee)}";
            var departureSummary = ramp.DepartureDetails is { } released
                ? $"Handled by {Person(released.HandledByEmployee)}"
                : null;
            var remarks = JoinDetails(arrivalSummary, ramp.ArrivalDetails.Remarks, departureSummary, ramp.DepartureDetails?.Remarks);
            ramps.Add(new(ramp.Id, ramp.Sequence, ramp.ArrivalDetails.ArrivedAtUtc, ramp.DepartureDetails?.DepartedAtUtc,
                remarks, Services(flight, ramp.Id), Tasks(flight, ramp.Id), signature?.SignedAtUtc));
        }

        var status = flight.ApprovalStatus switch
        {
            FlightApprovalStatus.Approved => "Approved",
            FlightApprovalStatus.PendingRtr => "Pending RTR",
            FlightApprovalStatus.Unlocked => "Unlocked",
            _ => "Not Approved"
        };

        var workOrder = new WorkOrderDetailDto(flight.Id, status, firstRecordedWork.By, flight.Customer.IataCode,
            flight.Customer.Name, flight.Station.IataCode, flight.Station.Name, flight.OperationType.Name, flight.FlightNumber.Value,
            flight.Schedule.Sta, flight.Schedule.Std, arrival?.AircraftType.Model, arrival?.AircraftTailNumber,
            arrival?.Ata, departure?.Atd, departure?.CustomerSignature?.SignedAtUtc, flight.ApprovalNumber,
            flight.ApprovedAtUtc, Services(flight, null), Tasks(flight, null), firstRecordedWork.At, ramps, reports,
            arrival?.CustomerWorkReferenceNumber);
        var plannedFlight = new WorkOrderPrintFlightDto(flight.IsPerLanding, flight.AircraftType?.Manufacturer, flight.AircraftType?.Model,
            flight.PlannedServices.OrderBy(item => item.Service.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Service.ServiceId)
                .Select(item => new PlannedServiceDto(item.Service.ServiceId, item.Service.Name, item.IsAircraftPerLanding)).ToList(),
            flight.AssignedEmployees.OrderBy(item => item.Employee.FullName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Employee.EmployeeId, StringComparer.OrdinalIgnoreCase)
                .Select(item => new AssignedEmployeeDto(item.Employee.StaffMemberId, item.Employee.FullName, item.Employee.EmployeeId)).ToList());

        var staff = await LoadStaffAsync(flight, ct);
        return new WorkOrderPrintDto(workOrder, plannedFlight, station.TimeZoneId, arrival?.AircraftType.Manufacturer, staff,
            await ReadSignatureAsync(departure?.CustomerSignature, ct), rampSignatures);
    }

    private async Task<IReadOnlyList<WorkOrderPrintStaffDto>> LoadStaffAsync(Flight flight, CancellationToken ct)
    {
        var ids = flight.ServiceRecords.SelectMany(item => item.Performers.Select(person => person.Employee.StaffMemberId))
            .Concat(flight.TaskRecords.SelectMany(item => item.Performers.Select(person => person.Employee.StaffMemberId)))
            .Distinct().ToArray();
        if (ids.Length == 0)
            return [];

        var staff = await masterData.GetStaffMembersAsync(ids, ct);
        var atStation = staff.Where(item => item.StationId == flight.Station.StationId).ToList();
        var names = new Dictionary<Guid, string>();
        foreach (var id in atStation.Select(item => item.ManpowerTypeId).Distinct())
        {
            if (await masterData.GetManpowerTypeAsync(id, ct) is { } type)
                names[id] = type.Name;
        }
        return atStation.Select(item => new WorkOrderPrintStaffDto(item.Id, names.GetValueOrDefault(item.ManpowerTypeId))).ToList();
    }

    private async Task<byte[]?> ReadSignatureAsync(CustomerSignature? signature, CancellationToken ct)
    {
        if (signature is null)
            return null;

        try
        {
            await using var stream = await storage.OpenAsync(signature.StorageKey, ct);
            if (stream is null)
                return null;

            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory, ct);
            return memory.ToArray();
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    private static IReadOnlyList<WorkOrderServiceLineDto> Services(Flight flight, Guid? ramp) =>
        flight.ServiceRecords.Where(item => item.ReturnToRampId == ramp)
            .OrderBy(item => item.Window.FromUtc).ThenBy(item => item.Window.ToUtc)
            .ThenBy(item => item.Service.Name, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Id)
            .Select(item => new WorkOrderServiceLineDto(item.Service.Name, item.Window.FromUtc, item.Window.ToUtc,
                item.Notes, item.CustomerReferenceNumber, Performers(item.Performers.Select(person => (person.Employee, person.Window))),
                Attachments(item.Attachments.Select(attachment => attachment.Details)))).ToList();

    private static IReadOnlyList<WorkOrderTaskDto> Tasks(Flight flight, Guid? ramp) =>
        flight.TaskRecords.Where(item => item.ReturnToRampId == ramp)
            .OrderBy(item => item.Window.FromUtc).ThenBy(item => item.Window.ToUtc).ThenBy(item => item.TaskType).ThenBy(item => item.Id)
            .Select(item => new WorkOrderTaskDto(item.TaskType.ToString(), item.Notes, item.Window.FromUtc, item.Window.ToUtc,
                item.AtaChapter?.Code, item.AtaChapter?.Title, item.CustomerReferenceNumber,
                Performers(item.Performers.Select(person => (person.Employee, person.Window))),
                Resources(item.Tools.Select(resource => (resource.Tool.Name, resource.Usage, resource.Notes))),
                Resources(item.Materials.Select(resource => (resource.Material.Name, resource.Usage, resource.Notes))),
                Resources(item.GeneralSupports.Select(resource => (resource.GeneralSupport.Name, resource.Usage, resource.Notes))),
                Attachments(item.Attachments.Select(attachment => attachment.Details)))).ToList();

    private static IReadOnlyList<WorkOrderPrintPerformerDto> Performers(IEnumerable<(StaffMemberSnapshot Employee, TimeWindow Window)> rows) =>
        rows.OrderBy(item => item.Employee.FullName, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Employee.StaffMemberId)
            .Select(item => new WorkOrderPrintPerformerDto(item.Employee.StaffMemberId, item.Employee.FullName,
                item.Employee.EmployeeId, item.Window.FromUtc, item.Window.ToUtc)).ToList();

    private static IReadOnlyList<WorkOrderPrintResourceDto> Resources(IEnumerable<(string Name, ResourceUsage Usage, string? Notes)> rows) =>
        rows.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(item => new WorkOrderPrintResourceDto(item.Name, item.Usage.CalculationType, item.Usage.Quantity,
                item.Usage.FromUtc, item.Usage.ToUtc, item.Notes)).ToList();

    private static IReadOnlyList<WorkOrderPrintAttachmentDto> Attachments(IEnumerable<AttachmentDetails> rows) =>
        rows.OrderBy(item => item.FileName, StringComparer.OrdinalIgnoreCase)
            .Select(item => new WorkOrderPrintAttachmentDto(item.Kind.ToString(), item.FileName, item.ContentType, item.SizeBytes)).ToList();

    private static string Person(StaffMemberSnapshot person) => $"{person.FullName} ({person.EmployeeId})";

    private static string JoinDetails(params string?[] parts) =>
        string.Join("\n", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
}
