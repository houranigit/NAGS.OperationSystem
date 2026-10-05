using MasterData.Domain.AircraftTypes;
using MasterData.Domain.Customers;
using MasterData.Domain.GeneralSupports;
using MasterData.Domain.LegacySystem;
using MasterData.Domain.ManpowerTypes;
using MasterData.Domain.Materials;
using MasterData.Domain.OperationTypes;
using MasterData.Domain.Services;
using MasterData.Domain.StaffMembers;
using MasterData.Domain.Stations;
using MasterData.Domain.Tools;
using Shouldly;

namespace MasterData.Domain.UnitTests;

public sealed class LegacySystemIdTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 0, 0, 0, TimeSpan.Zero);

    public static TheoryData<string> EntityKinds => new()
    {
        "Service", "Tool", "Material", "GeneralSupport", "Customer", "Station", "ManpowerType",
        "OperationType", "AircraftType", "StaffMember"
    };

    [Theory]
    [MemberData(nameof(EntityKinds))]
    public void Mapping_is_optional_trims_without_changing_case_and_can_be_cleared(string kind)
    {
        var entity = Create(kind);
        entity.LegacySystemId.ShouldBeNull();

        entity.SetLegacySystemId("  XyZ00123  ", Now.AddMinutes(1)).IsSuccess.ShouldBeTrue();
        entity.LegacySystemId.ShouldBe("XyZ00123");

        entity.SetLegacySystemId(" \t ", Now.AddMinutes(2)).IsSuccess.ShouldBeTrue();
        entity.LegacySystemId.ShouldBeNull();
    }

    [Theory]
    [MemberData(nameof(EntityKinds))]
    public void Mapping_rejects_oversized_values_without_destroying_saved_mapping(string kind)
    {
        var entity = Create(kind);
        entity.SetLegacySystemId(new string('0', 200), Now).IsSuccess.ShouldBeTrue();

        var result = entity.SetLegacySystemId(new string('0', 201), Now.AddMinutes(1));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("MasterData.LegacySystemId.TooLong");
        entity.LegacySystemId.ShouldBe(new string('0', 200));
    }

    [Fact]
    public void Ordinary_domain_updates_preserve_existing_mapping()
    {
        var tool = Tool.Create("Tool", null, Now).Value;
        tool.SetLegacySystemId("00123", Now).IsSuccess.ShouldBeTrue();

        tool.Update("Renamed tool", "Updated", Now.AddMinutes(1)).IsSuccess.ShouldBeTrue();

        tool.LegacySystemId.ShouldBe("00123");
    }

    private static ILegacySystemIdentified Create(string kind) => kind switch
    {
        "Service" => Service.Create("Service", null, Now).Value,
        "Tool" => Tool.Create("Tool", null, Now).Value,
        "Material" => Material.Create("Material", null, Now).Value,
        "GeneralSupport" => GeneralSupport.Create("Support", null, Now).Value,
        "Customer" => Customer.Create(null, null, "Customer", Guid.NewGuid(), null, null, null,
            Address.Create(null, null, null, null, null).Value, Now).Value,
        "Station" => Station.Create("ORD", null, "Station", null, Guid.NewGuid(), Now).Value,
        "ManpowerType" => ManpowerType.Create("Engineer", null, Now).Value,
        "OperationType" => OperationType.Create("Adhoc", null, Now).Value,
        "AircraftType" => AircraftType.Create(AircraftManufacturer.Airbus, "A320", null, Now).Value,
        "StaffMember" => StaffMember.Create("Staff", "EMP1", "staff@example.com", Guid.NewGuid(),
            Guid.NewGuid(), null, null, Now).Value,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
