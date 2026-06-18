using Store.Application.Features.Material.Commands.CreateMaterial;
using Store.Application.Features.Material.Commands.UpdateMaterial;
using Store.Contracts.Features.Material;

namespace Host.Web.Components.Pages.Settings.MasterData.Materials.Dialog;

/// <summary>
/// UI state for Material Add/Update dialogs. Maps to <see cref="CreateMaterialCommand"/> /
/// <see cref="UpdateMaterialCommand"/> — mirrors the <c>ServiceFormModel</c> pattern.
/// </summary>
public sealed class MaterialFormModel
{
    public Guid? Id { get; set; }

    public string Name { get; set; } = "";
    public Guid? UnitId { get; set; }
    public bool IsActive { get; set; } = true;

    public static MaterialFormModel FromDto(MaterialDto dto) =>
        new()
        {
            Id = dto.Id,
            Name = dto.Name,
            UnitId = dto.Unit.UnitId,
            IsActive = dto.IsActive
        };

    public MaterialFormModel Clone() =>
        new()
        {
            Id = Id,
            Name = Name,
            UnitId = UnitId,
            IsActive = IsActive
        };

    public CreateMaterialCommand ToCreateCommand() =>
        new(Name.Trim(), UnitId!.Value, IsActive);

    public UpdateMaterialCommand ToUpdateCommand(Guid id) =>
        new(id, Name.Trim(), UnitId!.Value, IsActive);
}
