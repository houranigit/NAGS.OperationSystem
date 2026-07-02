using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Persistence;
using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using MasterData.Application.Abstractions;
using MasterData.Application.Authorization;
using MasterData.Contracts;
using MasterData.Domain.PortalAccess;
using Microsoft.EntityFrameworkCore;

namespace MasterData.Application.Features.PortalAccess;

// --- Grant portal access to a staff member -------------------------------

public sealed record GrantStaffPortalAccessCommand(Guid StaffMemberId, Guid RoleId, byte[] RowVersion) : ICommand;

public sealed class GrantStaffPortalAccessCommandValidator : AbstractValidator<GrantStaffPortalAccessCommand>
{
    public GrantStaffPortalAccessCommandValidator()
    {
        RuleFor(x => x.StaffMemberId).NotEmpty();
        RuleFor(x => x.RoleId).NotEmpty();
        RuleFor(x => x.RowVersion).NotEmpty();
    }
}

public sealed class GrantStaffPortalAccessCommandHandler(IMasterDataDbContext db, IMasterDataScope scope, TimeProvider timeProvider)
    : ICommandHandler<GrantStaffPortalAccessCommand>
{
    public async Task<Result> Handle(GrantStaffPortalAccessCommand request, CancellationToken cancellationToken)
    {
        var resolved = await scope.ResolveAsync(cancellationToken);
        if (resolved.IsFailure)
            return resolved.Error;

        if (!resolved.Value.IsAdministrator && resolved.Value.StationId is null)
            return ScopeForbidden();

        var query = db.StaffMembers.AsQueryable();
        if (!resolved.Value.IsAdministrator)
            query = query.Where(s => s.StationId == resolved.Value.StationId);

        var staff = await query.FirstOrDefaultAsync(s => s.Id == request.StaffMemberId, cancellationToken);
        if (staff is null)
            return resolved.Value.IsAdministrator
                ? Error.NotFound("Staff member not found.", "MasterData.StaffMember.NotFound")
                : ScopeForbidden();

        if (!staff.IsActive)
            return Error.Validation("Portal access cannot be granted to an inactive staff member.", "MasterData.StaffMember.Inactive");

        if (staff.LinkedUserId is not null)
            return Error.Conflict("This staff member already has a linked portal account.", "MasterData.StaffMember.AlreadyLinked");

        if (staff.PortalState == PortalAccessState.Provisioning)
            return Error.Conflict("Portal access is already being provisioned for this staff member.", "MasterData.StaffMember.PortalAccessProvisioning");

        // A correlation id identifies this attempt; a stale reply from a superseded request is ignored.
        var correlationId = Guid.NewGuid();
        staff.RequestPortalAccess(correlationId, timeProvider.GetUtcNow());

        db.Enqueue(new PortalAccessRequested
        {
            ExternalReferenceId = staff.Id,
            UserType = UserType.StationStaff,
            RoleId = request.RoleId,
            Email = staff.Email,
            DisplayName = staff.FullName,
            CorrelationId = correlationId
        });

        db.SetOriginalRowVersion(staff, request.RowVersion);

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

    private static Error ScopeForbidden() =>
        Error.Forbidden("This record is outside your data scope.", "MasterData.Scope.Forbidden");
}

// --- Grant portal access to a customer contact ----------------------------

public sealed record GrantContactPortalAccessCommand(Guid CustomerId, Guid ContactId, Guid RoleId, byte[] RowVersion) : ICommand;

public sealed class GrantContactPortalAccessCommandValidator : AbstractValidator<GrantContactPortalAccessCommand>
{
    public GrantContactPortalAccessCommandValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.ContactId).NotEmpty();
        RuleFor(x => x.RoleId).NotEmpty();
        RuleFor(x => x.RowVersion).NotEmpty();
    }
}

public sealed class GrantContactPortalAccessCommandHandler(IMasterDataDbContext db, IMasterDataScope scope, TimeProvider timeProvider)
    : ICommandHandler<GrantContactPortalAccessCommand>
{
    public async Task<Result> Handle(GrantContactPortalAccessCommand request, CancellationToken cancellationToken)
    {
        var resolved = await scope.ResolveAsync(cancellationToken);
        if (resolved.IsFailure)
            return resolved.Error;

        if (!resolved.Value.IsAdministrator && resolved.Value.CustomerId is null)
            return ScopeForbidden();

        var query = db.Customers.AsQueryable();
        if (!resolved.Value.IsAdministrator)
            query = query.Where(c => c.Id == resolved.Value.CustomerId);

        var customer = await query
            .Include(c => c.Contacts)
            .FirstOrDefaultAsync(c => c.Id == request.CustomerId, cancellationToken);
        if (customer is null)
            return resolved.Value.IsAdministrator
                ? Error.NotFound("Customer not found.", "MasterData.Customer.NotFound")
                : ScopeForbidden();

        if (!customer.IsActive)
            return Error.Validation("Portal access cannot be granted while the customer is inactive.", "MasterData.Customer.Inactive");

        var correlationId = Guid.NewGuid();
        var requested = customer.RequestContactPortalAccess(request.ContactId, correlationId, timeProvider.GetUtcNow());
        if (requested.IsFailure)
            return requested.Error;

        db.Enqueue(new PortalAccessRequested
        {
            ExternalReferenceId = requested.Value.Id,
            UserType = UserType.CustomerContact,
            RoleId = request.RoleId,
            Email = requested.Value.Email,
            DisplayName = requested.Value.Name,
            CorrelationId = correlationId
        });

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

    private static Error ScopeForbidden() =>
        Error.Forbidden("This record is outside your data scope.", "MasterData.Scope.Forbidden");
}
