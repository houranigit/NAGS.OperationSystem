using BuildingBlocks.Domain.Aggregates;
using BuildingBlocks.Domain.Results;
using Core.Domain.Aggregates.License;
using Core.Domain.Aggregates.ManpowerType;
using Core.Domain.Aggregates.Station;
using Core.Domain.Events;
using Core.Domain.ValueObjects;

namespace Core.Domain.Aggregates.Employee;

public sealed class Employee : AggregateRoot<EmployeeId>
{
    private readonly List<EmployeeLicense> _licenses = [];

    public string FullName { get; private set; } = null!;
    public string Email { get; private set; } = null!;
    public byte[]? Logo { get; private set; }
    public ManpowerTypeId ManpowerTypeId { get; private set; } = null!;
    public StationId StationId { get; private set; } = null!;
    public EmploymentContract Contract { get; private set; } = null!;
    public WorkingSchedule WorkingSchedule { get; private set; } = null!;
    public IReadOnlyList<EmployeeLicense> Licenses => _licenses.AsReadOnly();
    public Guid? LinkedUserId { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private Employee() { }

    public static Result<Employee> Create(
        string fullName,
        string email,
        ManpowerTypeId manpowerTypeId,
        StationId stationId,
        EmploymentContract contract,
        WorkingSchedule workingSchedule,
        byte[]? logo = null,
        bool createUser = false)
    {
        var nameError = ValidateFullName(fullName);
        if (nameError is not null) return nameError;

        var emailError = ValidateEmail(email);
        if (emailError is not null) return emailError;

        var employee = new Employee
        {
            Id = EmployeeId.New(),
            FullName = fullName.Trim(),
            Email = email.Trim().ToLowerInvariant(),
            Logo = logo,
            ManpowerTypeId = manpowerTypeId,
            StationId = stationId,
            Contract = contract,
            WorkingSchedule = workingSchedule,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        employee.RaiseDomainEvent(new EmployeeCreatedEvent(
            employee.Id,
            employee.FullName,
            employee.Email,
            createUser));

        return employee;
    }

    public Result Activate()
    {
        if (IsActive)
            return Error.Conflict("Employee is already active.");

        IsActive = true;
        RaiseDomainEvent(new EmployeeActivatedEvent(Id));
        return Result.Success();
    }

    public Result Deactivate()
    {
        if (!IsActive)
            return Error.Conflict("Employee is already inactive.");

        IsActive = false;
        RaiseDomainEvent(new EmployeeDeactivatedEvent(Id, LinkedUserId));
        return Result.Success();
    }

    public Result UpdateDetails(
        string fullName,
        string email,
        ManpowerTypeId manpowerTypeId,
        StationId stationId)
    {
        var nameError = ValidateFullName(fullName);
        if (nameError is not null) return nameError;

        var emailError = ValidateEmail(email);
        if (emailError is not null) return emailError;

        FullName = fullName.Trim();
        Email = email.Trim().ToLowerInvariant();
        ManpowerTypeId = manpowerTypeId;
        StationId = stationId;

        return Result.Success();
    }

    public Result UpdateLogo(byte[]? logo)
    {
        Logo = logo;
        return Result.Success();
    }

    public Result UpdateContract(EmploymentContract contract)
    {
        Contract = contract;
        return Result.Success();
    }

    public Result UpdateWorkingSchedule(WorkingSchedule workingSchedule)
    {
        WorkingSchedule = workingSchedule;
        return Result.Success();
    }

    public Result<EmployeeLicense> AddLicense(LicenseId licenseId, string licenseNumber)
    {
        if (_licenses.Any(l => l.LicenseId == licenseId))
            return Error.Conflict("Employee already holds a license of this type.");

        var result = EmployeeLicense.Create(Id, licenseId, licenseNumber);
        if (result.IsFailure) return result.Error;

        var license = result.Value;
        _licenses.Add(license);
        RaiseDomainEvent(new EmployeeLicenseAddedEvent(Id, license.Id, licenseId));
        return license;
    }

    public Result RemoveLicense(EmployeeLicenseId employeeLicenseId)
    {
        var license = _licenses.FirstOrDefault(l => l.Id == employeeLicenseId);
        if (license is null)
            return Error.NotFound("License not found.");

        _licenses.Remove(license);
        RaiseDomainEvent(new EmployeeLicenseRemovedEvent(Id, employeeLicenseId));
        return Result.Success();
    }

    public Result UpdateLicense(EmployeeLicenseId employeeLicenseId, LicenseId licenseId, string licenseNumber)
    {
        var license = _licenses.FirstOrDefault(l => l.Id == employeeLicenseId);
        if (license is null)
            return Error.NotFound("License not found.");

        if (_licenses.Any(l => l.Id != employeeLicenseId && l.LicenseId == licenseId))
            return Error.Conflict("Employee already holds a license of this type.");

        var result = license.Update(licenseId, licenseNumber);
        if (result.IsFailure) return result;

        RaiseDomainEvent(new EmployeeLicenseUpdatedEvent(Id, employeeLicenseId, licenseId));
        return Result.Success();
    }

    /// <summary>
    /// Full licenses snapshot: ids update rows; null ids insert; others are removed.
    /// </summary>
    public Result SyncLicenses(
        IReadOnlyList<(Guid? EmployeeLicenseId, LicenseId LicenseId, string LicenseNumber)> incoming)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        var rows = incoming.ToList();
        var licenseIds = rows.Select(r => r.LicenseId).ToList();
        if (licenseIds.GroupBy(id => id).Any(g => g.Count() > 1))
            return Error.Validation("Duplicate license type in the license list.");

        foreach (var row in rows.Where(x => x.EmployeeLicenseId is not null))
        {
            var lid = EmployeeLicenseId.From(row.EmployeeLicenseId!.Value);
            var update = UpdateLicense(lid, row.LicenseId, row.LicenseNumber);
            if (update.IsFailure) return update.Error;
        }

        foreach (var lic in _licenses.ToList())
        {
            var keep = rows.Any(r => r.EmployeeLicenseId.HasValue && r.EmployeeLicenseId.Value == lic.Id.Value);
            if (!keep)
            {
                var rem = RemoveLicense(lic.Id);
                if (rem.IsFailure) return rem.Error;
            }
        }

        foreach (var row in rows.Where(x => x.EmployeeLicenseId is null))
        {
            var add = AddLicense(row.LicenseId, row.LicenseNumber);
            if (add.IsFailure) return add.Error;
        }

        return Result.Success();
    }

    public Result LinkToUser(Guid userId)
    {
        if (LinkedUserId.HasValue)
            return Error.Conflict("Employee is already linked to a user.");

        if (userId == Guid.Empty)
            return Error.Validation("UserId is required.");

        LinkedUserId = userId;
        RaiseDomainEvent(new EmployeeLinkedToUserEvent(Id, userId));
        return Result.Success();
    }

    private static Error? ValidateFullName(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            return Error.Validation("Full name is required.");
        if (fullName.Length > 200)
            return Error.Validation("Full name must not exceed 200 characters.");
        return null;
    }

    private static Error? ValidateEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return Error.Validation("Email is required.");
        if (!email.Contains('@') || !email.Contains('.'))
            return Error.Validation("Email format is invalid.");
        if (email.Length > 254)
            return Error.Validation("Email must not exceed 254 characters.");
        return null;
    }
}
