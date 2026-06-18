using Core.Application.Features.ManpowerType.Commands.CreateManpowerType;
using Core.Application.Features.ManpowerType.Commands.UpdateManpowerType;
using Core.Contracts.Features.ManpowerType;

namespace Host.Web.Components.Pages.Settings.MasterData.ManpowerTypes.Dialog;

/// <summary>
/// UI form state for Manpower Type Add/Update dialogs. Maps to Create/Update commands — keep validation on Radzen validators or helpers here.
/// </summary>
public sealed class ManpowerTypeFormModel
{
    public Guid? Id { get; set; }

    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;

    public static ManpowerTypeFormModel FromDto(ManpowerTypeDto dto) =>
        new()
        {
            Id = dto.Id,
            Name = dto.Name,
            Description = dto.Description,
            IsActive = dto.IsActive
        };

    public ManpowerTypeFormModel Clone() =>
        new()
        {
            Id = Id,
            Name = Name,
            Description = Description,
            IsActive = IsActive
        };

    public CreateManpowerTypeCommand ToCreateManpowerTypeCommand() =>
        new(
            Name.Trim(),
            string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
            IsActive);

    public UpdateManpowerTypeCommand ToUpdateManpowerTypeCommand(Guid id) =>
        new(
            id,
            Name.Trim(),
            string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
            IsActive);
}
