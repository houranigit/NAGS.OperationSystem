using Core.Application.Features.Service.Commands.CreateService;
using Core.Application.Features.Service.Commands.UpdateService;
using Core.Contracts.Features.Service;

namespace Host.Web.Components.Pages.Settings.MasterData.Services.Dialog;

/// <summary>
/// UI state for Service Add/Update dialogs. Maps to <see cref="CreateServiceCommand"/> / <see cref="UpdateServiceCommand"/> — parity with Customers <c>CustomerFormModel</c>.
/// </summary>
public sealed class ServiceFormModel
{
    public Guid? Id { get; set; }

    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;

    public static ServiceFormModel FromDto(ServiceDto dto) =>
        new()
        {
            Id = dto.Id,
            Name = dto.Name,
            Description = dto.Description,
            IsActive = dto.IsActive
        };

    public ServiceFormModel Clone() =>
        new()
        {
            Id = Id,
            Name = Name,
            Description = Description,
            IsActive = IsActive
        };

    public CreateServiceCommand ToCreateServiceCommand() =>
        new(
            Name.Trim(),
            string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
            IsActive);

    public UpdateServiceCommand ToUpdateServiceCommand(Guid id) =>
        new(
            id,
            Name.Trim(),
            string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
            IsActive);
}
