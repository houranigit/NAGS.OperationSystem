using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.Features.Operations;
using Shouldly;

namespace OperationsSystem.Blazor.UnitTests.Operations;

public sealed class FlightServicePresentationTests
{
    [Fact]
    public void Badge_gives_on_call_precedence_for_per_landing_flights()
    {
        FlightServicePresentation.Badge(isPerLanding: true, isOnCall: true)
            .ShouldBe(new FlightServiceBadge("On Call", "info"));
        FlightServicePresentation.Badge(isPerLanding: true, isOnCall: false)
            .ShouldBe(new FlightServiceBadge("Per Landing", "warning"));
        FlightServicePresentation.Badge(isPerLanding: false, isOnCall: false)
            .ShouldBeNull();
        FlightServicePresentation.Badge(isPerLanding: false, isOnCall: true)
            .ShouldBeNull();
    }

    [Fact]
    public void Per_landing_work_orders_start_without_prefilled_services()
    {
        var plannedServices = new[]
        {
            new PlannedServiceModel(Guid.NewGuid(), "Aircraft Per Landing", true),
            new PlannedServiceModel(Guid.NewGuid(), "Baggage", false)
        };

        FlightServicePresentation.ServicesToPrefill(isPerLanding: true, plannedServices)
            .ShouldBeEmpty();
    }

    [Fact]
    public void Normal_work_orders_keep_real_planned_service_prefill()
    {
        var perLanding = new PlannedServiceModel(Guid.NewGuid(), "Aircraft Per Landing", true);
        var baggage = new PlannedServiceModel(Guid.NewGuid(), "Baggage", false);

        FlightServicePresentation.ServicesToPrefill(
                isPerLanding: false,
                new[] { perLanding, baggage })
            .ShouldBe(new[] { baggage });
    }
}
