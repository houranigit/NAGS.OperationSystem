using BuildingBlocks.Domain.Results;
using MasterData.Contracts.Seeding;
using Operations.Application.Contracts;
using Operations.Domain.Flights;

namespace Operations.Application.Features.Flights;

/// <summary>New edits must satisfy actual prerequisites; exact acknowledgments run before these guards.</summary>
internal static class FlightActualRules
{
    internal static Result ArrivalRemarks(Flight flight, string? remarks) =>
        flight.OperationType.OperationTypeId == WellKnownMasterDataIds.AdHocOperationType &&
        flight.Customer.CustomerId == WellKnownMasterDataIds.UnknownCustomer && string.IsNullOrWhiteSpace(remarks)
            ? Error.Validation("Arrival remarks are required for an unknown customer.", "Operations.ArrivalDetails.RemarksRequiredForUnknownCustomer")
            : Result.Success();

    internal static Result RequireArrival(Flight flight, Guid staffId)
    {
        var arrival = Arrival(flight, staffId);
        return arrival is not null && arrival.Details.Ata != default &&
            arrival.Details.AircraftType.AircraftTypeId != Guid.Empty && !string.IsNullOrWhiteSpace(arrival.Details.AircraftTailNumber)
            ? Result.Success()
            : Error.Validation("Save valid arrival details before services, tasks or departure.", "Operations.Flight.ArrivalRequired");
    }

    internal static Result Chronology(Flight flight, Guid staffId, DateTimeOffset? arrivalOverride = null, DateTimeOffset? departureOverride = null)
    {
        var ata = arrivalOverride ?? Arrival(flight, staffId)?.Details.Ata;
        var departures = flight.DepartureSubmissions.OrderBy(item => item.CreatedAtUtc).ThenBy(item => item.Id).ToArray();
        var departure = departures.FirstOrDefault(item => item.SubmittedByEmployee.StaffMemberId == staffId)
            ?? departures.FirstOrDefault(item => item.IsAccepted) ?? departures.FirstOrDefault();
        var atd = departureOverride ?? departure?.Details.Atd;
        return ata.HasValue && atd.HasValue && atd < ata
            ? Error.Validation("Actual departure cannot be before actual arrival.", "Operations.Flight.DepartureBeforeArrival")
            : Result.Success();
    }

    internal static Result ServicePeriod(Flight flight, Guid recordId, SaveFlightServiceRequest input)
    {
        if (input.ServiceId == WellKnownMasterDataIds.UnknownService) return Result.Success();
        return flight.Services.Any(other => other.Id != recordId && other.Service.ServiceId == input.ServiceId &&
            Overlaps(input.StartUtc, input.EndUtc, other.Window.FromUtc, other.Window.ToUtc)) ? Overlap() : Result.Success();
    }

    internal static Result ServicePeriods(Flight flight, IReadOnlySet<Guid> changedIds)
    {
        // Only a pair involving changed desired work is relevant. Untouched historical violations stay readable.
        foreach (var service in flight.Services.Where(item => changedIds.Contains(item.Id)))
        {
            var result = ServicePeriod(flight, service.Id, new(service.Service.ServiceId, service.Window.FromUtc,
                service.Window.ToUtc, service.CustomerReferenceNumber, service.Notes, []));
            if (result.IsFailure) return result;
        }
        return Result.Success();
    }

    private static bool Overlaps(DateTimeOffset firstStart, DateTimeOffset? firstEnd, DateTimeOffset secondStart, DateTimeOffset? secondEnd) =>
        firstEnd != firstStart && secondEnd != secondStart &&
        (!secondEnd.HasValue || firstStart < secondEnd.Value) && (!firstEnd.HasValue || secondStart < firstEnd.Value);

    private static Error Overlap() => Error.Validation(new Dictionary<string, string[]>
        { ["StartUtc"] = ["Occurrences of the same service cannot overlap."], ["EndUtc"] = ["Close or adjust the other occurrence first."] },
        "Occurrences of the same service cannot overlap.", "Operations.FlightService.OverlappingPeriods");

    private static FlightArrivalSubmission? Arrival(Flight flight, Guid staffId)
    {
        var arrivals = flight.ArrivalSubmissions.OrderBy(item => item.CreatedAtUtc).ThenBy(item => item.Id).ToArray();
        return arrivals.FirstOrDefault(item => item.SubmittedByEmployee.StaffMemberId == staffId)
            ?? arrivals.FirstOrDefault(item => item.IsAccepted) ?? arrivals.FirstOrDefault();
    }
}
