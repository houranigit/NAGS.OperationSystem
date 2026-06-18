using BuildingBlocks.Domain.Events;
using Core.Domain.Aggregates.Employee;

namespace Core.Domain.Events;

public sealed class EmployeeLicenseRemovedEvent(
    EmployeeId employeeId,
    EmployeeLicenseId licenseId) : DomainEvent
{
    public EmployeeId EmployeeId { get; } = employeeId;
    public EmployeeLicenseId LicenseId { get; } = licenseId;
}
