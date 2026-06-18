using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Application.Abstractions.Mobile.Sync;
using BuildingBlocks.Domain.Results;
using Core.Domain.Aggregates.Employee;
using Core.Domain.Aggregates.License;
using Core.Domain.Aggregates.ManpowerType;
using Core.Domain.Aggregates.Station;
using Core.Domain.ValueObjects;

namespace Core.Application.Features.Employee.Commands.CreateEmployee;

/// <summary>
/// Creates an employee and reconciles licenses with a full snapshot (<see cref="Core.Domain.Aggregates.Employee.Employee.SyncLicenses"/>), matching the customer contacts pattern.
/// </summary>
public sealed class CreateEmployeeCommandHandler(
    IEmployeeRepository employees,
    IMobileSyncBroadcaster mobileSync)
    : ICommandHandler<CreateEmployeeCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateEmployeeCommand request, CancellationToken cancellationToken)
    {
        if (request.ContractFrom is null)
            return Error.Validation("Contract start date is required.");

        var contractResult = EmploymentContract.Create(request.ContractFrom.Value, request.ContractTo);
        if (contractResult.IsFailure) return contractResult.Error;

        var scheduleResult = WorkingSchedule.Create(request.WorkingDays);
        if (scheduleResult.IsFailure) return scheduleResult.Error;

        if (await employees.ExistsByEmailAsync(request.Email, null, cancellationToken))
            return Error.Conflict("An employee with this email already exists.");

        var created = Core.Domain.Aggregates.Employee.Employee.Create(
            request.FullName,
            request.Email,
            ManpowerTypeId.From(request.ManpowerTypeId),
            StationId.From(request.StationId),
            contractResult.Value,
            scheduleResult.Value,
            createUser: request.CreateLinkedUser);
        if (created.IsFailure) return created.Error;

        var employee = created.Value;

        var licenseRows = request.Licenses
            .Select(l => ((Guid?)l.Id, LicenseId.From(l.LicenseId), l.LicenseNumber))
            .ToList<(Guid? EmployeeLicenseId, LicenseId LicenseId, string LicenseNumber)>();

        var syncResult = employee.SyncLicenses(licenseRows);
        if (syncResult.IsFailure) return syncResult.Error;

        employees.Add(employee);
        MobileSyncCatalogBroadcasts.EnqueueRefresh(mobileSync, MobileSyncTables.Employees);
        return employee.Id.Value;
    }
}
