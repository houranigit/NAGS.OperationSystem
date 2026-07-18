using OperationsSystem.Blazor.Client.Api;

namespace OperationsSystem.Blazor.Client.Features.Operations;

internal readonly record struct FlightServiceBadge(string Label, string Tone);

internal static class FlightServicePresentation
{
    internal static FlightServiceBadge? Badge(bool isPerLanding, bool isOnCall) =>
        !isPerLanding
            ? null
            : isOnCall
                ? new FlightServiceBadge("On Call", "info")
                : new FlightServiceBadge("Per Landing", "warning");

    internal static IReadOnlyList<PlannedServiceModel> ServicesToPrefill(
        bool isPerLanding,
        IReadOnlyList<PlannedServiceModel> plannedServices) =>
        isPerLanding
            ? []
            : plannedServices.Where(service => !service.IsAircraftPerLanding).ToList();
}
