using BuildingBlocks.Domain.Results;
using MasterData.Contracts.Seeding;
using Operations.Domain.ValueObjects;

namespace Operations.Domain.Flights;

/// <summary>
/// Enforces the Per-Landing service rules while service selection is manual (later these move to the
/// Contracts module): a flight's planned services may contain "Aircraft Per Landing" only as the sole
/// planned service, and "Aircraft Per Landing" is never a performable work-order service line.
/// </summary>
public static class PerLandingPolicy
{
    public static bool IsAircraftPerLanding(Guid serviceId) =>
        serviceId == WellKnownMasterDataIds.AircraftPerLandingService;

    /// <summary>
    /// Validates the planned-service set: it cannot be empty (unless <paramref name="allowEmpty"/> is
    /// set for the ad-hoc cancellation exception) and Aircraft Per Landing cannot be mixed with any
    /// other service.
    /// </summary>
    public static Result ValidatePlannedServices(IReadOnlyList<ServiceSnapshot> plannedServices, bool allowEmpty = false)
    {
        if (plannedServices.Count == 0 && !allowEmpty)
        {
            return Error.Validation(
                "At least one planned service is required.",
                "Operations.PlannedServices.Required");
        }

        var distinct = plannedServices
            .GroupBy(s => s.ServiceId)
            .Select(g => g.First())
            .ToList();

        var hasPerLanding = distinct.Any(s => IsAircraftPerLanding(s.ServiceId));
        if (hasPerLanding && distinct.Count > 1)
        {
            return Error.Validation(
                "A Per Landing flight cannot have any other service. Aircraft Per Landing must be the only planned service.",
                "Operations.PerLanding.NoMix");
        }

        return Result.Success();
    }

    /// <summary>Rejects "Aircraft Per Landing" being recorded as a performed work-order service line.</summary>
    public static Result ValidatePerformedService(Guid serviceId)
    {
        if (IsAircraftPerLanding(serviceId))
        {
            return Error.Validation(
                "Aircraft Per Landing cannot be recorded as a performed service. Use On Call for requested service.",
                "Operations.PerLanding.NotPerformable");
        }

        return Result.Success();
    }
}
