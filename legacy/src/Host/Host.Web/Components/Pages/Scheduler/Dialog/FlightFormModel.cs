using Core.Contracts.Features.AircraftType;
using Core.Contracts.Features.Customer;
using Core.Contracts.Features.Employee;
using Core.Contracts.Features.ManpowerType;
using Core.Contracts.Features.OperationType;
using Core.Contracts.Features.Station;
using Operations.Application.Features.Flight.Commands.BatchCreateFlights;
using Operations.Application.Features.Flight.Commands.CreateFlight;
using Operations.Application.Features.Flight.Commands.UpdateFlight;
using Operations.Contracts.Flight;

namespace Host.Web.Components.Pages.Scheduler.Dialog;

/// <summary>
/// UI form state for Flight Add/Update/Schedule dialogs. Maps to
/// <see cref="CreateFlightCommand"/>, <see cref="UpdateFlightCommand"/>, and <see cref="BatchCreateFlightsCommand"/>.
/// Mirrors <c>CustomerFormModel</c>: primitives only; sections bind via <c>[Parameter] Model</c>;
/// the command mappers deep-clone snapshots so EF's owned-type single-owner rule is never violated.
/// </summary>
public sealed class FlightFormModel
{
    public Guid? Id { get; set; }

    // Route
    public Guid? CustomerId { get; set; }
    public Guid? StationId { get; set; }
    public Guid? OperationTypeId { get; set; }
    public Guid? AircraftTypeId { get; set; }
    public string FlightNumber { get; set; } = "";

    // Schedule (single flight: use Sta/Std)
    public DateTime? StaLocal { get; set; }
    public DateTime? StdLocal { get; set; }

    // Bulk schedule (period + weekday pattern + daily times)
    public DateTime? PeriodFrom { get; set; }
    public DateTime? PeriodTo { get; set; }
    public DateTime? ArrivalTimeClock { get; set; }
    public DateTime? DepartureTimeClock { get; set; }
    public List<int> Weekdays { get; set; } = [1, 2, 3, 4, 5];

    // Calendar selection for bulk (final confirmed days)
    public HashSet<DateOnly> SelectedDays { get; set; } = [];

    // Crew
    public List<Guid> AssignedEmployeeIds { get; set; } = [];

    // ---------- Validators used by Radzen custom validators ----------

    public bool IsFlightNumberValid()
    {
        var fn = (FlightNumber ?? "").Trim();
        return fn.Length is > 0 and <= 32;
    }

    public bool IsRouteStep0Valid() =>
        CustomerId.HasValue
        && StationId.HasValue
        && OperationTypeId.HasValue
        && IsFlightNumberValid();

    public bool IsSingleScheduleValid() => StaLocal.HasValue && StdLocal.HasValue;

    public bool IsBulkScheduleValid()
    {
        if (PeriodFrom is null || PeriodTo is null) return false;
        if (PeriodTo.Value.Date < PeriodFrom.Value.Date) return false;
        if (ArrivalTimeClock is null || DepartureTimeClock is null) return false;
        if (Weekdays is null || Weekdays.Count == 0) return false;
        return true;
    }

    public bool HasAssignedEmployees() => AssignedEmployeeIds is { Count: > 0 };

    // ---------- Factories ----------

    public static FlightFormModel NewForAdd(DateTime baseDay)
    {
        var arrival = baseDay.Date.AddHours(10);
        var departure = baseDay.Date.AddHours(12);
        return new FlightFormModel
        {
            StaLocal = arrival,
            StdLocal = departure
        };
    }

    public static FlightFormModel NewForBulk()
    {
        var today = DateTime.Today;
        return new FlightFormModel
        {
            PeriodFrom = today,
            PeriodTo = today,
            ArrivalTimeClock = today,
            DepartureTimeClock = today.AddHours(1),
            Weekdays = [1, 2, 3, 4, 5]
        };
    }

    public static FlightFormModel FromDto(FlightDto dto) =>
        new()
        {
            Id = dto.Id,
            CustomerId = dto.CustomerSnapshot.CustomerId,
            StationId = dto.StationSnapshot.StationId,
            OperationTypeId = dto.OperationTypeSnapshot.OperationTypeId,
            AircraftTypeId = dto.AircraftTypeId?.AircraftTypeId,
            FlightNumber = dto.FlightNumber,
            StaLocal = dto.Sta.UtcDateTime,
            StdLocal = dto.Std.UtcDateTime,
            AssignedEmployeeIds = dto.AssignedEmployees.Select(a => a.EmployeeId).ToList()
        };

    public FlightFormModel Clone() =>
        new()
        {
            Id = Id,
            CustomerId = CustomerId,
            StationId = StationId,
            OperationTypeId = OperationTypeId,
            AircraftTypeId = AircraftTypeId,
            FlightNumber = FlightNumber,
            StaLocal = StaLocal,
            StdLocal = StdLocal,
            PeriodFrom = PeriodFrom,
            PeriodTo = PeriodTo,
            ArrivalTimeClock = ArrivalTimeClock,
            DepartureTimeClock = DepartureTimeClock,
            Weekdays = Weekdays.ToList(),
            SelectedDays = [..SelectedDays],
            AssignedEmployeeIds = AssignedEmployeeIds.ToList()
        };

    // ---------- Command mapping ----------

    /// <summary>
    /// Resolved selection from the lookup lists — all snapshot names/codes in one place.
    /// Throws if the form is missing a required selection; callers should gate with <see cref="IsRouteStep0Valid"/>.
    /// </summary>
    public FlightRouteContext ResolveRoute(
        IReadOnlyList<CustomerDto> customers,
        IReadOnlyList<StationSelectOption> stations,
        IReadOnlyList<OperationTypeSelectOption> operationTypes,
        IReadOnlyList<AircraftTypeSelectOption> aircraftTypes)
    {
        var cust = customers.First(c => c.Id == CustomerId!.Value);
        var st = stations.First(s => s.Id == StationId!.Value);
        var op = operationTypes.First(o => o.Id == OperationTypeId!.Value);
        AircraftTypeSelectOption? ac = AircraftTypeId is { } aid ? aircraftTypes.First(a => a.Id == aid) : null;

        return new FlightRouteContext(
            new CustomerSnapshot(cust.Id, cust.IataCode, cust.Name),
            new StationSnapshot(st.Id, st.Name, st.IataCode),
            new OperationTypeSnapshot(op.Id, op.Name),
            ac is null ? null : new AircraftTypeSnapshot(ac.Id, ac.Model));
    }

    public IReadOnlyList<EmployeeSnapshot> ResolveAssignedCrew(IReadOnlyList<EmployeeDto> employees) =>
        AssignedEmployeeIds
            .Select(id => employees.First(e => e.Id == id))
            .Select(e => new EmployeeSnapshot(
                e.Id,
                e.FullName,
                new StationSnapshot(e.StationSnapshot.StationId, e.StationSnapshot.Name, e.StationSnapshot.IataCode),
                new ManpowerTypeSnapshot(e.ManpowerTypeSnapshot.ManpowerTypeId, e.ManpowerTypeSnapshot.Name)))
            .ToList();

    public CreateFlightCommand ToCreateFlightCommand(
        FlightRouteContext route,
        IReadOnlyList<EmployeeSnapshot> crew)
    {
        var (sta, std) = NormalizeSingleSchedule();
        return new CreateFlightCommand(
            route.Customer,
            route.Station,
            route.OperationType,
            route.AircraftType,
            NormalizedFlightNumber(),
            sta,
            std,
            crew);
    }

    public UpdateFlightCommand ToUpdateFlightCommand(
        Guid id,
        FlightRouteContext route,
        IReadOnlyList<EmployeeSnapshot> crew)
    {
        var (sta, std) = NormalizeSingleSchedule();
        return new UpdateFlightCommand(
            id,
            route.Customer,
            route.Station,
            route.OperationType,
            route.AircraftType,
            NormalizedFlightNumber(),
            sta,
            std,
            crew);
    }

    public BatchCreateFlightsCommand ToBatchCreateFlightsCommand(
        FlightRouteContext route,
        IReadOnlyList<EmployeeSnapshot> crew)
    {
        if (SelectedDays.Count == 0)
            throw new InvalidOperationException("No schedule days selected.");
        if (ArrivalTimeClock is null || DepartureTimeClock is null)
            throw new InvalidOperationException("Arrival/departure clock times are required.");

        var arrival = ArrivalTimeClock.Value.TimeOfDay;
        var departure = DepartureTimeClock.Value.TimeOfDay;
        var orderedDays = SelectedDays.OrderBy(d => d).ToList();
        var fn = NormalizedFlightNumber();

        var items = new List<BatchCreateFlightItem>(orderedDays.Count);
        foreach (var day in orderedDays)
        {
            var sta = UtcCombine(day, arrival);
            var std = UtcCombine(day, departure);
            if (std <= sta) std = std.AddDays(1);

            items.Add(new BatchCreateFlightItem(
                route.Customer,
                route.Station,
                route.OperationType,
                route.AircraftType,
                fn,
                sta,
                std,
                crew));
        }

        return new BatchCreateFlightsCommand(items);
    }

    /// <summary>
    /// Trim + upper-case applied to the entered flight number. The domain
    /// <c>FlightNumber</c> value object also normalizes inside <c>Create</c>;
    /// doing it here keeps every command leaving the form already canonical.
    /// </summary>
    private string NormalizedFlightNumber() =>
        (FlightNumber ?? string.Empty).Trim().ToUpperInvariant();

    private (DateTimeOffset Sta, DateTimeOffset Std) NormalizeSingleSchedule()
    {
        var sta = new DateTimeOffset(StaLocal!.Value, TimeSpan.Zero);
        var std = new DateTimeOffset(StdLocal!.Value, TimeSpan.Zero);
        if (std <= sta) std = std.AddDays(1);
        return (sta, std);
    }

    private static DateTimeOffset UtcCombine(DateOnly date, TimeSpan timeOfDay)
    {
        var dayStart = new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, TimeSpan.Zero);
        return dayStart.Add(timeOfDay);
    }
}

/// <summary>Resolved route data (snapshots built from lookup lists) for command construction.</summary>
public sealed record FlightRouteContext(
    CustomerSnapshot Customer,
    StationSnapshot Station,
    OperationTypeSnapshot OperationType,
    AircraftTypeSnapshot? AircraftType);
