using MasterData.Domain.AircraftTypes;
using MasterData.Domain.GeneralSupports;
using MasterData.Domain.Materials;
using MasterData.Domain.OperationTypes;
using MasterData.Domain.Services;
using MasterData.Domain.Tools;
using Shouldly;

namespace MasterData.Domain.UnitTests.Catalogs;

public sealed class CatalogAggregateTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Service_create_trims_name_and_description()
    {
        var result = Service.Create("  Aircraft Per Landing  ", "  Per landing  ", Now);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Name.ShouldBe("Aircraft Per Landing");
        result.Value.Description.ShouldBe("Per landing");
        result.Value.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void Operation_type_rejects_blank_name()
    {
        var result = OperationType.Create(" ", null, Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("MasterData.OperationType.NameRequired");
    }

    [Fact]
    public void Aircraft_type_normalizes_model_to_uppercase()
    {
        var result = AircraftType.Create(AircraftManufacturer.Airbus, " a320 ", "  narrow body  ", Now);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Model.ShouldBe("A320");
        result.Value.Notes.ShouldBe("narrow body");
    }

    [Fact]
    public void Material_and_general_support_start_without_units()
    {
        var material = Material.Create("  Hydraulic Fluid  ", "  Consumable  ", Now);
        var support = GeneralSupport.Create("  Customs Clearance  ", "  One-off support  ", Now);

        material.IsSuccess.ShouldBeTrue();
        material.Value.Name.ShouldBe("Hydraulic Fluid");
        material.Value.Description.ShouldBe("Consumable");

        support.IsSuccess.ShouldBeTrue();
        support.Value.Name.ShouldBe("Customs Clearance");
        support.Value.Description.ShouldBe("One-off support");
    }

    [Fact]
    public void Tool_rejects_duplicate_equipment_factory_and_serial_pair()
    {
        var tool = Tool.Create("Jack", null, Now).Value;

        tool.AddEquipment("F-1", "S-1", null, Now).IsSuccess.ShouldBeTrue();
        var duplicate = tool.AddEquipment(" f-1 ", " s-1 ", null, Now);

        duplicate.IsFailure.ShouldBeTrue();
        duplicate.Error.Code.ShouldBe("MasterData.ToolEquipment.Duplicate");
    }

    [Fact]
    public void Tool_update_and_remove_equipment_changes_collection()
    {
        var tool = Tool.Create("Jack", null, Now).Value;
        var equipmentId = tool.AddEquipment("F-1", "S-1", null, Now).Value.Id;

        tool.UpdateEquipment(equipmentId, "F-2", "S-2", new DateOnly(2026, 6, 1), Now.AddDays(1)).IsSuccess.ShouldBeTrue();
        tool.Equipments[0].FactoryId.ShouldBe("F-2");
        tool.Equipments[0].CalibrationDate.ShouldBe(new DateOnly(2026, 6, 1));

        tool.RemoveEquipment(equipmentId, Now.AddDays(2)).IsSuccess.ShouldBeTrue();
        tool.Equipments.ShouldBeEmpty();
    }
}
