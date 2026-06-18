using Core.Application.Features.OperationType.Commands.CreateOperationType;
using Core.Application.Features.OperationType.Commands.UpdateOperationType;
using Core.Contracts.Features.OperationType;

namespace Host.Web.Components.Pages.Settings.MasterData.OperationTypes.Dialog;

/// <summary>
/// UI form state for Operation Type Add/Update dialogs. Maps to Create/Update commands — validation on Radzen validators; sections bind via [Parameter] Model.
/// </summary>
public sealed class OperationTypeFormModel
{
    public Guid? Id { get; set; }

    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;

    public static OperationTypeFormModel FromDto(OperationTypeDto dto) =>
        new()
        {
            Id = dto.Id,
            Name = dto.Name,
            Description = dto.Description,
            IsActive = dto.IsActive
        };

    public OperationTypeFormModel Clone() =>
        new()
        {
            Id = Id,
            Name = Name,
            Description = Description,
            IsActive = IsActive
        };

    public CreateOperationTypeCommand ToCreateOperationTypeCommand() =>
        new(
            Name.Trim(),
            string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
            IsActive);

    public UpdateOperationTypeCommand ToUpdateOperationTypeCommand(Guid id) =>
        new(
            id,
            Name.Trim(),
            string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
            IsActive);
}
