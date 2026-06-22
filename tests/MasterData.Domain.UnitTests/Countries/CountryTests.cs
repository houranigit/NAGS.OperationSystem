using MasterData.Domain.Countries;
using Shouldly;

namespace MasterData.Domain.UnitTests.Countries;

public sealed class CountryTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_normalizes_code_and_trims_name()
    {
        var result = Country.Create("  Jordan  ", "jo", Now);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Name.ShouldBe("Jordan");
        result.Value.IsoCode.ShouldBe("JO");
        result.Value.IsActive.ShouldBeTrue();
        result.Value.CreatedAtUtc.ShouldBe(Now);
    }

    [Fact]
    public void Create_uses_supplied_id_for_deterministic_seeding()
    {
        var id = Guid.NewGuid();

        var result = Country.Create("Egypt", "EG", Now, id);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Id.ShouldBe(id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_with_blank_name_fails(string? name)
    {
        var result = Country.Create(name, "JO", Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("MasterData.Country.NameRequired");
    }

    [Theory]
    [InlineData("J")]
    [InlineData("JOR")]
    [InlineData("1A")]
    [InlineData("")]
    public void Create_with_invalid_code_fails(string code)
    {
        var result = Country.Create("Jordan", code, Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBeOneOf("MasterData.Country.CodeInvalid", "MasterData.Country.CodeRequired");
    }

    [Fact]
    public void Update_changes_fields_and_stamps_updated()
    {
        var country = Country.Create("Jordan", "JO", Now).Value;
        var later = Now.AddDays(1);

        var result = country.Update("Hashemite Kingdom", "jo", later);

        result.IsSuccess.ShouldBeTrue();
        country.Name.ShouldBe("Hashemite Kingdom");
        country.UpdatedAtUtc.ShouldBe(later);
    }

    [Fact]
    public void Deactivate_then_activate_toggles_state()
    {
        var country = Country.Create("Jordan", "JO", Now).Value;

        country.Deactivate(Now.AddDays(1)).IsSuccess.ShouldBeTrue();
        country.IsActive.ShouldBeFalse();

        country.Activate(Now.AddDays(2)).IsSuccess.ShouldBeTrue();
        country.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void Deactivate_is_idempotent_when_already_inactive()
    {
        var country = Country.Create("Jordan", "JO", Now).Value;
        country.Deactivate(Now).IsSuccess.ShouldBeTrue();

        var result = country.Deactivate(Now.AddDays(1));

        result.IsSuccess.ShouldBeTrue();
        country.IsActive.ShouldBeFalse();
    }
}
