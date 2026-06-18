using Core.Contracts.Features.Country;

namespace Host.Web.Components.Pages.Settings.MasterData.Countries.Dialog;

public sealed class CountryFormModel
{
    public Guid? Id { get; set; }

    public string Code { get; set; } = "";

    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;

    public static CountryFormModel FromDto(CountryDto dto) =>
        new()
        {
            Id = dto.Id,
            Code = dto.Code,
            Name = dto.Name,
            IsActive = dto.IsActive
        };

    public CountryFormModel Clone() =>
        new()
        {
            Id = Id,
            Code = Code,
            Name = Name,
            IsActive = IsActive
        };
}
