using Core.Contracts.Features.Employee;
using Core.Contracts.Features.Station;

namespace Host.Web.Components.Pages.Stations.Dialog;

public sealed class StationFormModel
{
    public Guid? Id { get; set; }
    public string IataCode { get; set; } = "";
    public string? IcaoCode { get; set; }
    public string Name { get; set; } = "";
    public string? City { get; set; }
    public bool IsActive { get; set; } = true;
    public List<EmployeeDto> AssignedEmployees { get; set; } = [];

    public static StationFormModel FromDto(StationDto dto) =>
        new()
        {
            Id = dto.Id,
            IataCode = dto.IataCode,
            IcaoCode = dto.IcaoCode,
            Name = dto.Name,
            City = dto.City,
            IsActive = dto.IsActive,
            AssignedEmployees = [.. dto.AssignedEmployees]
        };

    public StationFormModel Clone() => new()
    {
        Id = Id,
        IataCode = IataCode,
        IcaoCode = IcaoCode,
        Name = Name,
        City = City,
        IsActive = IsActive,
        AssignedEmployees = [.. AssignedEmployees]
    };
}
