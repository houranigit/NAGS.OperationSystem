using Core.Application.Features.License.Commands.CreateLicense;
using Core.Application.Features.License.Commands.UpdateLicense;
using Core.Contracts.Features.License;

namespace Host.Web.Components.Pages.Settings.MasterData.Licenses.Dialog;

/// <summary>
/// UI state for license Add / Update dialogs. Maps to commands — mirrors <see cref="Host.Web.Components.Pages.Customers.Dialog.CustomerFormModel"/> shape (smaller aggregate).
/// </summary>
public sealed class LicenseFormModel
{
    public Guid? Id { get; set; }

    public string Code { get; set; } = "";

    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;

    public static LicenseFormModel FromDto(LicenseDto dto) =>
        new()
        {
            Id = dto.Id,
            Code = dto.Code,
            Name = dto.Name,
            Description = dto.Description,
            IsActive = dto.IsActive
        };

    public LicenseFormModel Clone() =>
        new()
        {
            Id = Id,
            Code = Code,
            Name = Name,
            Description = Description,
            IsActive = IsActive
        };

    public CreateLicenseCommand ToCreateLicenseCommand() =>
        new(
            Code.Trim(),
            Name.Trim(),
            string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
            IsActive);

    public UpdateLicenseCommand ToUpdateLicenseCommand(Guid id) =>
        new(
            id,
            Code.Trim(),
            Name.Trim(),
            string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
            IsActive);
}
