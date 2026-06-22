using MasterData.Domain.Licenses;
using Shouldly;

namespace MasterData.Domain.UnitTests.Licenses;

public sealed class LicenseTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_uppercases_code_and_trims_name()
    {
        var result = License.Create("  a1  ", "  Airframe  ", null, Now);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Code.ShouldBe("A1");
        result.Value.Name.ShouldBe("Airframe");
        result.Value.IsActive.ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_with_blank_code_fails(string? code)
    {
        var result = License.Create(code, "Airframe", null, Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("MasterData.License.CodeRequired");
    }

    [Theory]
    [InlineData("A")]
    [InlineData("ABCDEFGHIJK")]
    public void Create_with_out_of_range_code_length_fails(string code)
    {
        var result = License.Create(code, "Airframe", null, Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("MasterData.License.CodeLength");
    }

    [Fact]
    public void Create_with_non_alphanumeric_code_fails()
    {
        var result = License.Create("A-1", "Airframe", null, Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("MasterData.License.CodeInvalid");
    }

    [Fact]
    public void Update_keeps_code_immutable()
    {
        var license = License.Create("A1", "Airframe", null, Now).Value;

        var result = license.Update("Powerplant", "Engines", Now.AddDays(1));

        result.IsSuccess.ShouldBeTrue();
        license.Code.ShouldBe("A1");
        license.Name.ShouldBe("Powerplant");
        license.Description.ShouldBe("Engines");
        license.UpdatedAtUtc.ShouldBe(Now.AddDays(1));
    }

    [Fact]
    public void Deactivate_then_activate_toggles_state()
    {
        var license = License.Create("A1", "Airframe", null, Now).Value;

        license.Deactivate(Now.AddDays(1)).IsSuccess.ShouldBeTrue();
        license.IsActive.ShouldBeFalse();

        license.Activate(Now.AddDays(2)).IsSuccess.ShouldBeTrue();
        license.IsActive.ShouldBeTrue();
    }
}
