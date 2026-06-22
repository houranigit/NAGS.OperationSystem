using MasterData.Domain.ManpowerTypes;
using Shouldly;

namespace MasterData.Domain.UnitTests.ManpowerTypes;

public sealed class ManpowerTypeTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_trims_name_and_description()
    {
        var result = ManpowerType.Create("  Mechanic  ", "  Aircraft mechanic  ", Now);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Name.ShouldBe("Mechanic");
        result.Value.Description.ShouldBe("Aircraft mechanic");
        result.Value.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void Create_allows_null_description()
    {
        var result = ManpowerType.Create("Loadmaster", null, Now);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Description.ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_with_blank_name_fails(string? name)
    {
        var result = ManpowerType.Create(name, null, Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("MasterData.ManpowerType.NameRequired");
    }

    [Fact]
    public void Create_with_overlong_description_fails()
    {
        var result = ManpowerType.Create("Mechanic", new string('x', 501), Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("MasterData.ManpowerType.DescriptionTooLong");
    }

    [Fact]
    public void Update_changes_fields_and_stamps_updated()
    {
        var type = ManpowerType.Create("Mechanic", null, Now).Value;

        var result = type.Update("Senior Mechanic", "Lead", Now.AddDays(1));

        result.IsSuccess.ShouldBeTrue();
        type.Name.ShouldBe("Senior Mechanic");
        type.Description.ShouldBe("Lead");
        type.UpdatedAtUtc.ShouldBe(Now.AddDays(1));
    }

    [Fact]
    public void Deactivate_then_activate_toggles_state()
    {
        var type = ManpowerType.Create("Mechanic", null, Now).Value;

        type.Deactivate(Now.AddDays(1)).IsSuccess.ShouldBeTrue();
        type.IsActive.ShouldBeFalse();

        type.Activate(Now.AddDays(2)).IsSuccess.ShouldBeTrue();
        type.IsActive.ShouldBeTrue();
    }
}
