using MasterData.Contracts.Resources;
using Operations.Domain.ValueObjects;
using Shouldly;

namespace Operations.Domain.UnitTests;

public sealed class ResourceUsageTests
{
    [Fact]
    public void Create_Quantity_RequiresDatabaseSafePositiveDecimal()
    {
        ResourceUsage.Create(ResourceCalculationType.Quantity, 0, null, null).IsFailure.ShouldBeTrue();
        ResourceUsage.Create(ResourceCalculationType.Quantity, 1.001m, null, null).Error.Code
            .ShouldBe("Operations.ResourceUsage.QuantityPrecisionInvalid");
        ResourceUsage.Create(ResourceCalculationType.Quantity, 10000000000000000m, null, null).Error.Code
            .ShouldBe("Operations.ResourceUsage.QuantityPrecisionInvalid");

        var valid = ResourceUsage.Create(ResourceCalculationType.Quantity, 12.25m, null, null);
        valid.IsSuccess.ShouldBeTrue();
        valid.Value.Quantity.ShouldBe(12.25m);
        valid.Value.FromUtc.ShouldBeNull();
        valid.Value.ToUtc.ShouldBeNull();
    }

    [Fact]
    public void Create_Duration_AllowsOpenEndAndNormalizesUtc()
    {
        var from = new DateTimeOffset(2026, 8, 8, 10, 0, 0, TimeSpan.FromHours(3));

        var result = ResourceUsage.Create(ResourceCalculationType.Duration, null, from, null);

        result.IsSuccess.ShouldBeTrue();
        result.Value.CalculationType.ShouldBe(ResourceCalculationType.Duration);
        result.Value.FromUtc.ShouldBe(from.ToUniversalTime());
        result.Value.ToUtc.ShouldBeNull();
        result.Value.Quantity.ShouldBeNull();
    }

    [Fact]
    public void Create_RejectsMixedOrReversedBranches()
    {
        var from = DateTimeOffset.UtcNow;

        ResourceUsage.Create(ResourceCalculationType.Quantity, 1, from, null).IsFailure.ShouldBeTrue();
        ResourceUsage.Create(ResourceCalculationType.Duration, 1, null, null).IsFailure.ShouldBeTrue();
        ResourceUsage.Create(ResourceCalculationType.Duration, null, from, from.AddMinutes(-1)).IsFailure.ShouldBeTrue();
    }
}
