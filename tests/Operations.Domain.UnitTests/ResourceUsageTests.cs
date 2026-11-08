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
        ResourceUsage.Create(ResourceCalculationType.Quantity, 9999999999999999.99m, null, null)
            .IsSuccess.ShouldBeTrue();
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
        var from = TestData.Now;

        ResourceUsage.Create(ResourceCalculationType.Quantity, 1, from, null).IsFailure.ShouldBeTrue();
        ResourceUsage.Create(ResourceCalculationType.Duration, 1, null, null).IsFailure.ShouldBeTrue();
        ResourceUsage.Create(ResourceCalculationType.Duration, null, from, from.AddMinutes(-1)).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Create_RejectsUnknownCalculationTypeMissingQuantityAndInvalidDurationTimes()
    {
        ResourceUsage.Create((ResourceCalculationType)99, 1, null, null).Error.Code
            .ShouldBe("Operations.ResourceUsage.CalculationTypeInvalid");
        ResourceUsage.Create(ResourceCalculationType.Quantity, null, null, null).Error.Code
            .ShouldBe("Operations.ResourceUsage.QuantityInvalid");
        ResourceUsage.Create(ResourceCalculationType.Duration, null, null, null).Error.Code
            .ShouldBe("Operations.ResourceUsage.FromRequired");
        ResourceUsage.Create(ResourceCalculationType.Duration, null, default(DateTimeOffset), null).Error.Code
            .ShouldBe("Operations.ResourceUsage.FromRequired");
        ResourceUsage.Create(ResourceCalculationType.Duration, null, TestData.Now, default(DateTimeOffset)).Error.Code
            .ShouldBe("Operations.ResourceUsage.ToInvalid");

        var closed = ResourceUsage.Create(ResourceCalculationType.Duration, null,
            TestData.Now.ToOffset(TimeSpan.FromHours(3)), TestData.Now.AddHours(1).ToOffset(TimeSpan.FromHours(3))).Value;
        closed.FromUtc.ShouldBe(TestData.Now);
        closed.ToUtc.ShouldBe(TestData.Now.AddHours(1));
        closed.FromUtc!.Value.Offset.ShouldBe(TimeSpan.Zero);
        closed.ToUtc!.Value.Offset.ShouldBe(TimeSpan.Zero);
        ResourceUsage.Create(ResourceCalculationType.Duration, null, TestData.Now, TestData.Now).IsSuccess.ShouldBeTrue();
    }
}
