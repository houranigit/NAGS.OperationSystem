using Store.Application.Features.Tool.Commands.CreateTool;
using Store.Application.Features.Tool.Commands.UpdateTool;
using Store.Contracts.Features.Tool;

namespace Host.Web.Components.Pages.Settings.MasterData.Tools.Dialog;

/// <summary>
/// UI state for Tool Add/Update dialogs. Maps to <see cref="CreateToolCommand"/> /
/// <see cref="UpdateToolCommand"/>. Mutable equipment rows live in <see cref="Equipments"/>;
/// rows with <c>Id == null</c> are new, the rest map back via their existing id.
/// </summary>
public sealed class ToolFormModel
{
    public Guid? Id { get; set; }

    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;

    public List<EquipmentRow> Equipments { get; set; } = [];

    public sealed class EquipmentRow
    {
        public Guid? Id { get; set; }
        public string FactoryId { get; set; } = "";
        public string SerialId { get; set; } = "";
        public DateOnly? CalibrationDate { get; set; }

        public EquipmentRow Clone() =>
            new() { Id = Id, FactoryId = FactoryId, SerialId = SerialId, CalibrationDate = CalibrationDate };
    }

    public static ToolFormModel FromDetails(ToolDetailsDto dto) =>
        new()
        {
            Id = dto.Id,
            Name = dto.Name,
            Description = dto.Description,
            IsActive = dto.IsActive,
            Equipments = dto.Equipments
                .Select(e => new EquipmentRow
                {
                    Id = e.Id,
                    FactoryId = e.FactoryId,
                    SerialId = e.SerialId,
                    CalibrationDate = e.CalibrationDate
                })
                .ToList()
        };

    public ToolFormModel Clone() =>
        new()
        {
            Id = Id,
            Name = Name,
            Description = Description,
            IsActive = IsActive,
            Equipments = Equipments.Select(e => e.Clone()).ToList()
        };

    public CreateToolCommand ToCreateCommand() =>
        new(
            Name.Trim(),
            string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
            IsActive,
            ToCommandEquipments());

    public UpdateToolCommand ToUpdateCommand(Guid id) =>
        new(
            id,
            Name.Trim(),
            string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
            IsActive,
            ToCommandEquipments());

    private IReadOnlyList<ToolEquipmentInput> ToCommandEquipments() =>
        Equipments
            .Where(e => !string.IsNullOrWhiteSpace(e.FactoryId) || !string.IsNullOrWhiteSpace(e.SerialId))
            .Select(e => new ToolEquipmentInput(
                e.Id,
                (e.FactoryId ?? string.Empty).Trim(),
                (e.SerialId ?? string.Empty).Trim(),
                e.CalibrationDate))
            .ToList();
}
