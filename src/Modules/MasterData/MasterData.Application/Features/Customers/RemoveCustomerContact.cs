using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Persistence;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using MasterData.Application.Abstractions;
using MasterData.Application.Authorization;
using MasterData.Application.Features.PortalAccess;
using Microsoft.EntityFrameworkCore;

namespace MasterData.Application.Features.Customers;

/// <summary>
/// Removes a contact from a customer (soft delete). When the contact has a linked portal user, the
/// removal propagates to Identity: a plain removal deactivates the user and revokes sessions while
/// retaining it for audit; a permanent removal (<see cref="ReleaseEmail"/>) additionally detaches the
/// user and releases its login email for reuse by a different identity.
/// </summary>
public sealed record RemoveCustomerContactCommand(Guid CustomerId, Guid ContactId, byte[] RowVersion, bool ReleaseEmail = false) : ICommand;

public sealed class RemoveCustomerContactCommandValidator : AbstractValidator<RemoveCustomerContactCommand>
{
    public RemoveCustomerContactCommandValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.ContactId).NotEmpty();
        RuleFor(x => x.RowVersion).NotEmpty();
    }
}

public sealed class RemoveCustomerContactCommandHandler(
    IMasterDataDbContext db,
    IMasterDataScope scope,
    IUserContext userContext,
    TimeProvider timeProvider)
    : ICommandHandler<RemoveCustomerContactCommand>
{
    public async Task<Result> Handle(RemoveCustomerContactCommand request, CancellationToken cancellationToken)
    {
        var scopeCheck = await scope.CheckCustomerAsync(request.CustomerId, cancellationToken);
        if (scopeCheck.IsFailure)
            return scopeCheck.Error;

        if (request.ReleaseEmail && !PortalAccessAuthorization.CanGrantCustomerContactAccess(userContext))
            return PortalAccessAuthorization.ReleaseEmailForbidden();

        var customer = await db.Customers
            .Include(c => c.Contacts)
            .FirstOrDefaultAsync(c => c.Id == request.CustomerId, cancellationToken);
        if (customer is null)
            return Error.NotFound("Customer not found.", "MasterData.Customer.NotFound");
        if (CustomerSystemRecords.IsSystem(customer.Id))
            return Error.Conflict("System customers cannot be modified.", "MasterData.Customer.SystemProtected");

        var contact = customer.Contacts.FirstOrDefault(c => c.Id == request.ContactId);
        var linkedUserId = contact?.LinkedUserId;

        var now = timeProvider.GetUtcNow();
        var remove = customer.RemoveContact(request.ContactId, releaseLink: request.ReleaseEmail, now);
        if (remove.IsFailure)
            return remove.Error;

        if (linkedUserId is { } userId)
        {
            if (!request.ReleaseEmail)
                remove.Value.SuspendPortal(now);

            PortalLifecycle.EnqueueDeactivation(db, request.ContactId, userId, request.ReleaseEmail);
        }

        db.SetOriginalRowVersion(customer, request.RowVersion);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyErrors.Stale;
        }

        return Result.Success();
    }
}
