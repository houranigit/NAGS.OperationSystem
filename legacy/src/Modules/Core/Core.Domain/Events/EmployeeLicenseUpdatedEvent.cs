using BuildingBlocks.Domain.Events;
using Core.Domain.Aggregates.Employee;
using Core.Domain.Aggregates.License;

namespace Core.Domain.Events;

public sealed class EmployeeLicenseUpdatedEvent(
    EmployeeId employeeId,
    EmployeeLicenseId licenseId,
    LicenseId licenseTypeId) : DomainEvent
{
    public EmployeeId EmployeeId { get; } = employeeId;
    public EmployeeLicenseId LicenseId { get; } = licenseId;
    public LicenseId LicenseTypeId { get; } = licenseTypeId;
}
