using BuildingBlocks.Contracts.IntegrationEvents;
using Core.Application.Abstractions;
using Core.Domain.Aggregates.Employee;
using Identity.Contracts.IntegrationEvents;
using Microsoft.Extensions.Logging;

namespace Core.Application.IntegrationEvents.Handlers;

/// <summary>
/// Closes the create-employee-with-account saga: when Identity replies that it has provisioned a
/// user for a given employee, this handler sets <c>Employee.LinkedUserId</c> so the rest of the
/// system can resolve the employee's portal user (notifications, mobile session, audit, etc).
/// </summary>
/// <remarks>
/// Idempotent via the standard inbox-dedup keyed by <c>EventId</c> — the outbox processor may
/// re-deliver the message after a transient failure. Soft-fails (employee missing or already
/// linked) still mark the event processed so we don't poison the queue.
/// </remarks>
public sealed class UserCreatedForEmployeeIntegrationEventHandler(
    ICoreDbContext db,
    IEmployeeRepository employees,
    ILogger<UserCreatedForEmployeeIntegrationEventHandler> logger)
    : IIntegrationEventHandler<UserCreatedForEmployeeIntegrationEvent>
{
    public async Task Handle(
        UserCreatedForEmployeeIntegrationEvent notification,
        CancellationToken cancellationToken)
    {
        if (await db.IsAlreadyProcessedAsync(notification.EventId, cancellationToken))
            return;

        var employee = await employees.GetByIdAsync(
            EmployeeId.From(notification.EmployeeId), cancellationToken);

        if (employee is null)
        {
            logger.LogWarning(
                "UserCreatedForEmployee: employee {EmployeeId} not found — dropping link to user {UserId}.",
                notification.EmployeeId, notification.UserId);
            db.MarkProcessed(notification.EventId, nameof(UserCreatedForEmployeeIntegrationEvent));
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        if (employee.LinkedUserId.HasValue)
        {
            // Already linked — re-delivery or operator linked manually. Nothing to do.
            db.MarkProcessed(notification.EventId, nameof(UserCreatedForEmployeeIntegrationEvent));
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var link = employee.LinkToUser(notification.UserId);
        if (link.IsFailure)
        {
            logger.LogWarning(
                "UserCreatedForEmployee: failed to link employee {EmployeeId} to user {UserId}: {Error}",
                notification.EmployeeId, notification.UserId, link.Error.Description);
            db.MarkProcessed(notification.EventId, nameof(UserCreatedForEmployeeIntegrationEvent));
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        db.MarkProcessed(notification.EventId, nameof(UserCreatedForEmployeeIntegrationEvent));
        await db.SaveChangesAsync(cancellationToken);
    }
}
