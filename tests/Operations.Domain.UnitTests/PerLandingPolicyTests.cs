using MasterData.Contracts.Seeding;
using Operations.Domain.Flights;
using Operations.Domain.ValueObjects;
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
    public void PerformedPerLandingService_IsRejected()
    {
        var result = PerLandingPolicy.ValidatePerformedService(WellKnownMasterDataIds.AircraftPerLandingService);
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Operations.PerLanding.NotPerformable");
    }

    [Fact]
    public void PerformedRegularService_IsAllowed()
    {
        var result = PerLandingPolicy.ValidatePerformedService(Guid.NewGuid());
        result.IsSuccess.ShouldBeTrue();
    }
}
