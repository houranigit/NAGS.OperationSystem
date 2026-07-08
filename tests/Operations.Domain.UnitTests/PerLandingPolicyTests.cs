using Operations.Domain.Flights;
using Shouldly;

namespace Operations.Domain.UnitTests;

public sealed class PerLandingPolicyTests
{
    [Fact]
    public void PerLandingAlone_IsAllowed()
    {
        var result = PerLandingPolicy.ValidatePlannedServices([TestData.PerLandingService()]);
        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void PerLandingMixedWithOtherService_IsRejected()
    {
        var result = PerLandingPolicy.ValidatePlannedServices([TestData.PerLandingService(), TestData.Service()]);
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Operations.PerLanding.NoMix");
    }

    [Fact]
    public void EmptyPlannedServices_IsRejected_UnlessExplicitlyAllowed()
    {
        var rejected = PerLandingPolicy.ValidatePlannedServices([]);
        rejected.IsFailure.ShouldBeTrue();
        rejected.Error.Code.ShouldBe("Operations.PlannedServices.Required");

        var allowed = PerLandingPolicy.ValidatePlannedServices([], allowEmpty: true);
        allowed.IsSuccess.ShouldBeTrue();
    }
}
