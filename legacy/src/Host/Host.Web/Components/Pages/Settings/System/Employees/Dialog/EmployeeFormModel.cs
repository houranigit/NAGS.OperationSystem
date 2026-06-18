using Core.Application.Features.Employee.Commands.CreateEmployee;
using Core.Application.Features.Employee.Commands.UpdateEmployee;
using Core.Contracts.Features.Employee;

namespace Host.Web.Components.Pages.Settings.System.Employees.Dialog;

/// <summary>
/// UI state for Employee Add/Update dialogs — maps to create/update commands (see <c>CustomerFormModel</c>).
/// </summary>
public sealed class EmployeeFormModel
{
    public Guid? Id { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public Guid ManpowerTypeId { get; set; }
    public Guid StationId { get; set; }
    public DateOnly? ContractFrom { get; set; }
    public DateOnly? ContractTo { get; set; }
    public List<DayOfWeek> WorkingDays { get; set; } = [];
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// When true, creating/updating the employee triggers an Identity-side request to provision a
    /// user account and email an activation link to <see cref="Email"/>. Hidden in the UI once the
    /// employee already has a linked user (<see cref="HasLinkedUser"/>).
    /// </summary>
    public bool CreateLinkedUser { get; set; }

    /// <summary>True when the employee already has a linked Identity user.</summary>
    public bool HasLinkedUser { get; set; }

    /// <summary>Repeating rows for certifications (Licenses wizard section).</summary>
    public List<EmployeeLicenseEditorLine> LicenseLines { get; set; } = [new()];

    public string UserTypeDisplay { get; set; } = "";
    public string UserStatusDisplay { get; set; } = "";

    public static bool LicenseLineHasAnyField(EmployeeLicenseEditorLine line)
    {
        var num = (line.LicenseNumber ?? string.Empty).Trim();
        return line.LicenseTypeId.HasValue || num.Length > 0;
    }

    public bool IsLicenseTypeInputValidForRow(int index)
    {
        if (index < 0 || index >= LicenseLines.Count) return true;
        var line = LicenseLines[index];
        if (!LicenseLineHasAnyField(line)) return true;
        return line.LicenseTypeId.HasValue && line.LicenseTypeId.Value != Guid.Empty;
    }

    public bool IsLicenseNumberInputValidForRow(int index)
    {
        if (index < 0 || index >= LicenseLines.Count) return true;
        var line = LicenseLines[index];
        if (!LicenseLineHasAnyField(line)) return true;
        var num = (line.LicenseNumber ?? string.Empty).Trim();
        return num.Length > 0 && num.Length <= 100;
    }

    public bool AreFilledLicenseTypesUnique()
    {
        var ids = LicenseLines
            .Where(LicenseLineHasAnyField)
            .Select(l => l.LicenseTypeId)
            .Where(id => id.HasValue && id.Value != Guid.Empty)
            .Select(id => id!.Value)
            .ToList();

        return ids.Count == ids.Distinct().Count();
    }

    public static EmployeeFormModel FromDto(EmployeeDto dto)
    {
        var lines = dto.Licenses.Count == 0
            ? new List<EmployeeLicenseEditorLine> { new() }
            : dto.Licenses
                .Select(l => new EmployeeLicenseEditorLine
                {
                    Id = null,
                    LicenseTypeId = l.LicenseSnapshot.LicenseId,
                    LicenseNumber = l.LicenseNumber
                })
                .ToList();

        return new EmployeeFormModel
        {
            Id = dto.Id,
            FullName = dto.FullName,
            Email = dto.Email,
            ManpowerTypeId = dto.ManpowerTypeSnapshot.ManpowerTypeId,
            StationId = dto.StationSnapshot.StationId,
            ContractFrom = dto.ContractFrom,
            ContractTo = dto.ContractTo,
            WorkingDays = dto.WorkingDays.ToList(),
            IsActive = dto.IsActive,
            HasLinkedUser = dto.LinkedUserId is not null,
            LicenseLines = lines,
            UserTypeDisplay = dto.UserType.Name,
            UserStatusDisplay = dto.UserStatus.Name
        };
    }

    public EmployeeFormModel Clone() =>
        new()
        {
            Id = Id,
            FullName = FullName,
            Email = Email,
            ManpowerTypeId = ManpowerTypeId,
            StationId = StationId,
            ContractFrom = ContractFrom,
            ContractTo = ContractTo,
            WorkingDays = WorkingDays.ToList(),
            IsActive = IsActive,
            CreateLinkedUser = CreateLinkedUser,
            HasLinkedUser = HasLinkedUser,
            LicenseLines = LicenseLines.Select(l => l.Clone()).ToList(),
            UserTypeDisplay = UserTypeDisplay,
            UserStatusDisplay = UserStatusDisplay
        };

    public IReadOnlyList<EmployeeLicenseInput> BuildLicenseInputs() =>
        LicenseLines
            .Where(LicenseLineHasAnyField)
            .Select(l =>
                new EmployeeLicenseInput(l.Id, l.LicenseTypeId!.Value, (l.LicenseNumber ?? "").Trim()))
            .ToList();

    public CreateEmployeeCommand ToCreateEmployeeCommand() =>
        new(
            FullName.Trim(),
            Email.Trim(),
            ManpowerTypeId,
            StationId,
            ContractFrom,
            ContractTo,
            WorkingDays.ToHashSet(),
            CreateLinkedUser,
            BuildLicenseInputs());

    public UpdateEmployeeCommand ToUpdateEmployeeCommand(Guid id) =>
        new(
            id,
            FullName.Trim(),
            Email.Trim(),
            ManpowerTypeId,
            StationId,
            ContractFrom,
            ContractTo,
            WorkingDays.ToHashSet(),
            CreateLinkedUser && !HasLinkedUser,
            BuildLicenseInputs());
}

/// <summary>Editable row for employee certifications (add / wizard UI).</summary>
public sealed class EmployeeLicenseEditorLine
{
    /// <summary>Stable Blazor identity for bracket rows.</summary>
    public Guid UiKey { get; init; } = Guid.NewGuid();

    /// <summary>Persisted employee-license row id on update; null for new rows.</summary>
    public Guid? Id { get; set; }

    public Guid? LicenseTypeId { get; set; }

    public string LicenseNumber { get; set; } = "";

    public EmployeeLicenseEditorLine Clone() =>
        new()
        {
            UiKey = UiKey,
            Id = Id,
            LicenseTypeId = LicenseTypeId,
            LicenseNumber = LicenseNumber
        };
}
