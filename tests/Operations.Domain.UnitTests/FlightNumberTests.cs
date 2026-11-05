using Operations.Domain.ValueObjects;
using Shouldly;

namespace Operations.Domain.UnitTests;

public sealed class FlightNumberTests
{
    [Theory]
    [InlineData(null, "123", "123")]
    [InlineData(null, "123/1", "123/1")]
    [InlineData(null, "123/01", "123/01")]
    [InlineData(null, "0/00", "0/00")]
    [InlineData("XY", "123", "XY123")]
    [InlineData("XY", "XY123", "XY123")]
    [InlineData("XY", "123/12", "XY123/12")]
    [InlineData("XY", "xy123/12", "XY123/12")]
    [InlineData(" xy ", "123/1", "XY123/1")]
    [InlineData("5Y", "5y123/9", "5Y123/9")]
    [InlineData("12", "123", "123")]
    [InlineData("12", "789", "12789")]
    [InlineData("12", "12", "1212")]
    [InlineData("12", "1212", "1212")]
    [InlineData("5Y", "5Y", "5Y5Y")]
    [InlineData("5Y", "5Y5Y", "5Y5Y")]
    [InlineData(null, "1234A", "1234A")]
    [InlineData("XY", "1234a", "XY1234A")]
    [InlineData("XY", "xy123A", "XY123A")]
    [InlineData(null, "1a", "1A")]
    public void Create_AcceptsOnlyTheCustomerCodeDigitsAndOptionalRoute(string? code, string input, string expected)
    {
        var result = FlightNumber.Create(input, code);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_RequiresAFlightNumber(string? input)
    {
        var result = FlightNumber.Create(input, "XY");

        result.Error.Code.ShouldBe("Operations.FlightNumber.Required");
    }

    [Theory]
    [InlineData("XY", "XY")]
    [InlineData("XY", "ZZ123")]
    [InlineData("XY", "XYXY123")]
    [InlineData("XY", "123/")]
    [InlineData("XY", "/1")]
    [InlineData("XY", "123/123")]
    [InlineData("XY", "123//1")]
    [InlineData("XY", "123/1/2")]
    [InlineData("XY", "123/-1")]
    [InlineData("XY", "123/A")]
    [InlineData("XY", "123 /1")]
    [InlineData("XY", "123/ 1")]
    [InlineData("XY", "XY 123")]
    [InlineData("XY", "XY-123")]
    [InlineData("XY", "123.4")]
    [InlineData("XY", "123_4")]
    [InlineData("XY", "+123")]
    [InlineData("XY", "123 ")]
    [InlineData("XY", " 123")]
    [InlineData("XY", "123\n")]
    [InlineData("XY", "123\t")]
    [InlineData("XY", "١٢٣")]
    [InlineData("XY", "123/١")]
    [InlineData("SV", "ſV123")]
    [InlineData(null, "XY123")]
    [InlineData(null, "ABC")]
    [InlineData("XY", "12345")]
    [InlineData("XY", "XY12345A/12")]
    [InlineData(null, "123AB")]
    [InlineData("XY", "123A/1")]
    [InlineData(null, "1a/01")]
    [InlineData("XY", "123aB/12")]
    [InlineData("XY", "123é/12")]
    public void Create_RejectsAnyOtherFormat(string? code, string input)
    {
        var result = FlightNumber.Create(input, code);

        result.Error.Code.ShouldBe("Operations.FlightNumber.InvalidFormat");
    }
}
