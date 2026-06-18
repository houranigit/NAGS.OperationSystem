using BuildingBlocks.Domain.Entities;
using BuildingBlocks.Domain.Results;
using Core.Domain.Aggregates.License;

namespace Core.Domain.Aggregates.Employee;

public sealed class EmployeeLicense : Entity<EmployeeLicenseId>
{
    public EmployeeId EmployeeId { get; private set; } = null!;
    public LicenseId LicenseId { get; private set; } = null!;
    public string LicenseNumber { get; private set; } = null!;

    private EmployeeLicense() { }

    internal static Result<EmployeeLicense> Create(
        EmployeeId employeeId,
        LicenseId licenseId,
        string licenseNumber)
    {
        var error = ValidateLicenseNumber(licenseNumber);
        if (error is not null) return error;

        return new EmployeeLicense
        {
            Id = EmployeeLicenseId.New(),
            EmployeeId = employeeId,
            LicenseId = licenseId,
            LicenseNumber = licenseNumber.Trim().ToUpperInvariant()
        };
    }

    internal Result Update(LicenseId licenseId, string licenseNumber)
    {
        var error = ValidateLicenseNumber(licenseNumber);
        if (error is not null) return error;

        LicenseId = licenseId;
        LicenseNumber = licenseNumber.Trim().ToUpperInvariant();
        return Result.Success();
    }

    private static Error? ValidateLicenseNumber(string licenseNumber)
    {
        if (string.IsNullOrWhiteSpace(licenseNumber))
            return Error.Validation("License number is required.");
        if (licenseNumber.Length > 100)
            return Error.Validation("License number must not exceed 100 characters.");
        return null;
    }
}
