using BuildingBlocks.Domain.Results;
using MasterData.Contracts.Seeding;
using Operations.Domain.ValueObjects;

namespace Operations.Domain.Flights;

/// <summary>
/// A flight's planned services may contain Aircraft Per Landing only as the sole service.
/// </summary>
public static class PerLandingPolicy
{
    public static bool IsAircraftPerLanding(Guid serviceId) =>
        serviceId == WellKnownMasterDataIds.AircraftPerLandingService;

    /// <summary>
    /// Validates that services are present and Aircraft Per Landing is not mixed with other services.
    /// </summary>
    public static Result ValidatePlannedServices(IReadOnlyList<ServiceSnapshot> plannedServices)
    {
        if (plannedServices.Count == 0)
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
}
