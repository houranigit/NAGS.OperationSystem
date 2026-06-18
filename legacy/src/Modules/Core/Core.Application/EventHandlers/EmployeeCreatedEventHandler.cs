using System.Text.Json;
using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Abstractions.Events;
using Core.Contracts.IntegrationEvents;
using Core.Domain.Events;

namespace Core.Application.EventHandlers;

/// <summary>
/// Bridges the <see cref="EmployeeCreatedEvent"/> domain event to the outbox so the Identity module
/// can provision a user account for the new employee. Only writes the integration event when the
/// caller explicitly opted-in via <see cref="EmployeeCreatedEvent.CreateUser"/> — otherwise the
/// employee is created without a linked identity (admin can request the user later).
/// </summary>
/// <remarks>
/// The outbox row is added inside the same EF change tracker as the employee insert. The outer
/// <c>SaveChangesAsync</c> in <c>BaseDbContext</c> dispatches domain events AFTER the initial
/// <c>SaveChanges</c>, so the row written here lands on a follow-up save triggered by a recursive
/// <c>SaveChangesAsync</c> call below — same pattern as the Customer contact flow.
/// </remarks>
public sealed class EmployeeCreatedEventHandler(
    IOutboxWriter outboxWriter,
    IUnitOfWork unitOfWork)
    : IDomainEventHandler<EmployeeCreatedEvent>
{
    public async Task Handle(EmployeeCreatedEvent notification, CancellationToken cancellationToken)
    {
        if (!notification.CreateUser)
            return;

        outboxWriter.Write(
            nameof(EmployeeUserCreationRequestedIntegrationEvent),
            JsonSerializer.Serialize(new EmployeeUserCreationRequestedIntegrationEvent(
                notification.EmployeeId.Value,
                notification.FullName,
                notification.Email)));

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
