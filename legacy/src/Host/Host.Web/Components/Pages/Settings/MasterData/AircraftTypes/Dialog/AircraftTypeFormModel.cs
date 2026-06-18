using Core.Contracts.Features.AircraftType;
using Core.Domain.Enumerations;

namespace Host.Web.Components.Pages.Settings.MasterData.AircraftTypes.Dialog;

public sealed class AircraftTypeFormModel
{
    public Guid? Id { get; set; }
    public Manufacturer Manufacturer { get; set; }

    private string _model = "";

    public string Model
    {
        get => _model;
        set => _model = (value ?? "").Trim().ToUpperInvariant();
    }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;

    public static AircraftTypeFormModel FromDto(AircraftTypeDto dto) =>
        new()
        {
            Id = dto.Id,
            Manufacturer = dto.Manufacturer,
            Model = dto.Model,
            Notes = dto.Notes,
            IsActive = dto.IsActive
        };

    /// <summary>Deep copy of editable fields for dirty-check baseline.</summary>
    public AircraftTypeFormModel Clone() =>
        new()
        {
            Id = Id,
            Manufacturer = Manufacturer,
            Model = Model,
            Notes = Notes,
            IsActive = IsActive
        };
}
