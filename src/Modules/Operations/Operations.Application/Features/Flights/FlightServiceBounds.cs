using BuildingBlocks.Domain.Results;
using Operations.Application.Contracts;
using Operations.Domain.Flights;

namespace Operations.Application.Features.Flights;

/// <summary>Actual-time bounds use the same own/accepted/earliest selection as mobile details.</summary>
internal static class FlightServiceBounds
{
    internal static Result Validate(Flight flight, SaveFlightServiceRequest input, Guid staffMemberId)
    {
        var actuals = VisibleActuals(flight, staffMemberId);
        var failures = new Dictionary<string, string[]>();
        ValidatePeriods(input, actuals.Arrival, actuals.Departure, failures);
        return ResultFor(failures);
    }

    internal static Result ValidateExistingServices(Flight flight, Guid staffMemberId,
        DateTimeOffset? arrivalOverride = null, DateTimeOffset? departureOverride = null)
    {
        var actuals = VisibleActuals(flight, staffMemberId);
        var failures = new Dictionary<string, string[]>();
        foreach (var service in flight.Services)
        {
            var input = new SaveFlightServiceRequest(service.Service.ServiceId, service.Window.FromUtc, service.Window.ToUtc,
                service.CustomerReferenceNumber, service.Notes, service.Performers.Select(p =>
                    new SaveFlightServicePerformerRequest(p.Id, p.Employee.StaffMemberId, p.Window.FromUtc, p.Window.ToUtc)).ToList());
            ValidatePeriods(input, arrivalOverride ?? actuals.Arrival, departureOverride ?? actuals.Departure,
                failures, $"Services[{service.Id}].");
        }
        return ResultFor(failures);
    }

    internal static Result RequireCompletedIfDeparted(Flight flight, SaveFlightServiceRequest input, Guid staffMemberId)
    {
        if (VisibleActuals(flight, staffMemberId).Departure is null) return Result.Success();
        var failures = new Dictionary<string, string[]>();
        if (input.EndUtc is null) failures["EndUtc"] = ["Enter the service end because departure is already recorded."];
        for (var i = 0; i < input.Performers.Count; i++)
            if (input.Performers[i].EndUtc is null)
                failures[$"Performers[{i}].EndUtc"] = ["Enter the employee end because departure is already recorded."];
        return failures.Count == 0 ? Result.Success() : Error.Validation(failures,
            "Service and employee end times are required when departure is already recorded.",
            "Operations.FlightService.EndRequiredAfterDeparture");
    }

    internal static Result RequireCompletedForDeparture(Flight flight)
    {
        var failures = new Dictionary<string, string[]>();
        foreach (var service in flight.Services)
        {
            var prefix = $"Services[{service.Id}].";
            if (service.Window.ToUtc is null)
                failures[prefix + "EndUtc"] = ["Enter the service end before saving departure."];
            if (service.Performers.Count == 0)
                failures[prefix + "Performers"] = ["Every started service needs at least one employee before departure."];
            for (var i = 0; i < service.Performers.Count; i++)
                if (service.Performers[i].Window.ToUtc is null)
                    failures[$"{prefix}Performers[{i}].EndUtc"] = ["Enter every employee's end before saving departure."];
        }
        return failures.Count == 0 ? Result.Success() : Error.Validation(failures,
            "Complete the end times for started services and their employees before saving departure.",
            "Operations.Departure.ServiceWorkIncomplete");
    }

    private static (DateTimeOffset? Arrival, DateTimeOffset? Departure) VisibleActuals(Flight flight, Guid staffMemberId)
    {
        var arrivals = flight.ArrivalSubmissions.OrderBy(a => a.CreatedAtUtc).ThenBy(a => a.Id).ToArray();
        var arrival = arrivals.FirstOrDefault(a => a.SubmittedByEmployee.StaffMemberId == staffMemberId)
            ?? arrivals.FirstOrDefault(a => a.IsAccepted) ?? arrivals.FirstOrDefault();
        var departures = flight.DepartureSubmissions.OrderBy(d => d.CreatedAtUtc).ThenBy(d => d.Id).ToArray();
        var departure = departures.FirstOrDefault(d => d.SubmittedByEmployee.StaffMemberId == staffMemberId)
            ?? departures.FirstOrDefault(d => d.IsAccepted) ?? departures.FirstOrDefault();
        return (arrival?.Details.Ata, departure?.Details.Atd);
    }

    private static void ValidatePeriods(SaveFlightServiceRequest input, DateTimeOffset? ata, DateTimeOffset? atd,
        Dictionary<string, string[]> failures, string prefix = "")
    {
        void Check(DateTimeOffset start, DateTimeOffset? end, string fieldPrefix, string label)
        {
            if (ata.HasValue && start < ata.Value)
                failures[$"{fieldPrefix}StartUtc"] = [$"{label} start cannot be before ATA."];
            if (atd.HasValue && start > atd.Value)
                failures[$"{fieldPrefix}StartUtc"] = [$"{label} start cannot be after ATD."];
            if (atd.HasValue && end > atd.Value)
                failures[$"{fieldPrefix}EndUtc"] = [$"{label} end cannot be after ATD."];
        }
        Check(input.StartUtc, input.EndUtc, prefix, "Service");
        for (var i = 0; i < input.Performers.Count; i++)
            Check(input.Performers[i].StartUtc, input.Performers[i].EndUtc, $"{prefix}Performers[{i}].", "Employee");
    }

    private static Result ResultFor(Dictionary<string, string[]> failures) => failures.Count == 0 ? Result.Success() :
        Error.Validation(failures, "Service and employee times must stay within the recorded actual flight times.", "Operations.FlightService.Validation");
}
