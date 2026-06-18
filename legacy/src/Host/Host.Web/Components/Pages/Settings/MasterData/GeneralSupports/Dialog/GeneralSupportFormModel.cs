using Store.Application.Features.GeneralSupport.Commands.CreateGeneralSupport;
using Store.Application.Features.GeneralSupport.Commands.UpdateGeneralSupport;
using Store.Contracts.Features.GeneralSupport;

namespace Host.Web.Components.Pages.Settings.MasterData.GeneralSupports.Dialog;

public sealed class GeneralSupportFormModel
{
    public Guid? Id { get; set; }

    public string Name { get; set; } = "";
    public Guid? UnitId { get; set; }
    public bool IsDuration { get; set; }
    public string? Note { get; set; }
    public bool IsActive { get; set; } = true;

    public static GeneralSupportFormModel FromDto(GeneralSupportDto dto) =>
        new()
        {
            Id = dto.Id,
            Name = dto.Name,
            UnitId = dto.Unit.UnitId,
            IsDuration = dto.IsDuration,
            Note = dto.Note,
            IsActive = dto.IsActive
        };

    public GeneralSupportFormModel Clone() =>
        new()
        {
            Id = Id,
            Name = Name,
            UnitId = UnitId,
            IsDuration = IsDuration,
            Note = Note,
            IsActive = IsActive
        };

    public CreateGeneralSupportCommand ToCreateCommand() =>
        new(
            Name.Trim(),
            UnitId!.Value,
            IsDuration,
            string.IsNullOrWhiteSpace(Note) ? null : Note.Trim(),
            IsActive);

    public UpdateGeneralSupportCommand ToUpdateCommand(Guid id) =>
        new(
            id,
            Name.Trim(),
            UnitId!.Value,
            IsDuration,
            string.IsNullOrWhiteSpace(Note) ? null : Note.Trim(),
            IsActive);
}
