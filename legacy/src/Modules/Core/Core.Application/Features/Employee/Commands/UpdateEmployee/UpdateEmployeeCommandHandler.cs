using System.Text.Json;
using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Application.Abstractions.Mobile.Sync;
using BuildingBlocks.Domain.Results;
using Core.Contracts.IntegrationEvents;
using Core.Domain.Aggregates.Employee;
using Core.Domain.Aggregates.License;
using Core.Domain.Aggregates.ManpowerType;
using Core.Domain.Aggregates.Station;
using Core.Domain.ValueObjects;

namespace Core.Application.Features.Employee.Commands.UpdateEmployee;

/// <summary>
/// Updates employee details and replaces the license list using <see cref="Core.Domain.Aggregates.Employee.Employee.SyncLicenses"/> — mirror <see cref="Customer.Commands.UpdateCustomer.UpdateCustomerCommandHandler"/>.
/// When <see cref="UpdateEmployeeCommand.CreateLinkedUser"/> is set and the employee has no linked user yet,
/// a request integration event is written to the outbox so Identity can provision a user account.
/// </summary>
public sealed class UpdateEmployeeCommandHandler(
    IEmployeeRepository employees,
    IOutboxWriter outboxWriter,
    IMobileSyncBroadcaster mobileSync)
    : ICommandHandler<UpdateEmployeeCommand>
{
    public async Task<Result> Handle(UpdateEmployeeCommand request, CancellationToken cancellationToken)
    {
        var id = EmployeeId.From(request.Id);
        var entity = await employees.GetByIdWithLicensesAsync(id, cancellationToken);
        if (entity is null) return Error.NotFound("Employee was not found.");

        if (await employees.ExistsByEmailAsync(request.Email, id, cancellationToken))
            return Error.Conflict("An employee with this email already exists.");

        if (request.ContractFrom is null)
            return Error.Validation("Contract start date is required.");

        var contractResult = EmploymentContract.Create(request.ContractFrom.Value, request.ContractTo);
        if (contractResult.IsFailure) return contractResult;

        var scheduleResult = WorkingSchedule.Create(request.WorkingDays);
        if (scheduleResult.IsFailure) return scheduleResult;

        var detailsResult = entity.UpdateDetails(
            request.FullName,
            request.Email,
            ManpowerTypeId.From(request.ManpowerTypeId),
            StationId.From(request.StationId));
        if (detailsResult.IsFailure) return detailsResult;

        var contractUpdateResult = entity.UpdateContract(contractResult.Value);
        if (contractUpdateResult.IsFailure) return contractUpdateResult;

        var scheduleUpdateResult = entity.UpdateWorkingSchedule(scheduleResult.Value);
        if (scheduleUpdateResult.IsFailure) return scheduleUpdateResult;

        var licenseRows = request.Licenses
            .Select(l => ((Guid?)l.Id, LicenseId.From(l.LicenseId), l.LicenseNumber))
            .ToList<(Guid? EmployeeLicenseId, LicenseId LicenseId, string LicenseNumber)>();

        var syncResult = entity.SyncLicenses(licenseRows);
        if (syncResult.IsFailure) return syncResult;

        employees.Update(entity);

        // Only write the integration event when the admin explicitly opted in AND the employee
        // doesn't already have a linked user — re-asking for provisioning would create duplicates.
        if (request.CreateLinkedUser && entity.LinkedUserId is null)
        {
            outboxWriter.Write(
                nameof(EmployeeUserCreationRequestedIntegrationEvent),
                JsonSerializer.Serialize(new EmployeeUserCreationRequestedIntegrationEvent(
                    entity.Id.Value,
                    entity.FullName,
                    entity.Email)));
        }

        MobileSyncCatalogBroadcasts.EnqueueRefresh(mobileSync, MobileSyncTables.Employees);
        return Result.Success();
    }
}
