using Operations.Application.Features.Mobile;
using Shouldly;

namespace Operations.Application.UnitTests;

public sealed class MobileFlightWindowTests
{
    private static readonly DateTimeOffset Sta =
        new(2026, 7, 18, 18, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(-12, true)]
    [InlineData(-11.999, true)]
    [InlineData(0, true)]
    [InlineData(11.999, true)]
    [InlineData(12, true)]
    [InlineData(-12.001, false)]
    [InlineData(12.001, false)]
    public void Evaluate_UsesInclusiveWindowAroundSta(double hoursFromSta, bool expected)
    {
        var result = MobileFlightWindow.Evaluate(Sta, Sta.AddHours(hoursFromSta));

        result.IsWithinWindow.ShouldBe(expected);
        result.StartsAtUtc.ShouldBe(Sta.AddHours(-MobileFlightWindow.DefaultHours));
        result.EndsAtUtc.ShouldBe(Sta.AddHours(MobileFlightWindow.DefaultHours));
    }

    [Theory]
    [InlineData(-50, MobileFlightWindow.MinHours)]
    [InlineData(0, MobileFlightWindow.MinHours)]
    [InlineData(12, 12)]
    [InlineData(500, MobileFlightWindow.MaxHours)]
    public void ClampHours_UsesSupportedRange(int requested, int expected) =>
        MobileFlightWindow.ClampHours(requested).ShouldBe(expected);
}
