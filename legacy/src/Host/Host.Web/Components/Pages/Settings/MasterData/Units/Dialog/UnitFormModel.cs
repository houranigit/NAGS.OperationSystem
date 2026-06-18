using Store.Application.Features.Unit.Commands.CreateUnit;
using Store.Application.Features.Unit.Commands.UpdateUnit;
using Store.Contracts.Features.Unit;

namespace Host.Web.Components.Pages.Settings.MasterData.Units.Dialog;

/// <summary>
/// UI state for Unit Add/Update dialogs. Maps to <see cref="CreateUnitCommand"/> /
/// <see cref="UpdateUnitCommand"/> — mirrors the <c>ServiceFormModel</c> pattern.
/// </summary>
public sealed class UnitFormModel
{
    public Guid? Id { get; set; }

    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;

    public static UnitFormModel FromDto(UnitDto dto) =>
        new()
        {
            Id = dto.Id,
            Code = dto.Code,
            Name = dto.Name,
            IsActive = dto.IsActive
        };

    public UnitFormModel Clone() =>
        new()
        {
            Id = Id,
            Code = Code,
            Name = Name,
            IsActive = IsActive
        };

    public CreateUnitCommand ToCreateCommand() =>
        new(Code.Trim().ToUpperInvariant(), Name.Trim(), IsActive);

    public UpdateUnitCommand ToUpdateCommand(Guid id) =>
        new(id, Code.Trim().ToUpperInvariant(), Name.Trim(), IsActive);
}
