using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Persistence;
using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using MasterData.Application.Abstractions;
using MasterData.Contracts;
using Microsoft.EntityFrameworkCore;

namespace MasterData.Application.Features.PortalAccess;

// --- Grant portal access to a staff member -------------------------------

public sealed record GrantStaffPortalAccessCommand(Guid StaffMemberId, Guid RoleId) : ICommand;

public sealed class GrantStaffPortalAccessCommandValidator : AbstractValidator<GrantStaffPortalAccessCommand>
{
    public GrantStaffPortalAccessCommandValidator()
    {
        RuleFor(x => x.StaffMemberId).NotEmpty();
        RuleFor(x => x.RoleId).NotEmpty();
    }
}

public sealed class GrantStaffPortalAccessCommandHandler(IMasterDataDbContext db)
    : ICommandHandler<GrantStaffPortalAccessCommand>
{
    public async Task<Result> Handle(GrantStaffPortalAccessCommand request, CancellationToken cancellationToken)
    {
        var staff = await db.StaffMembers.FirstOrDefaultAsync(s => s.Id == request.StaffMemberId, cancellationToken);
        if (staff is null)
            return Error.NotFound("Staff member not found.", "MasterData.StaffMember.NotFound");

        if (!staff.IsActive)
            return Error.Validation("Portal access cannot be granted to an inactive staff member.", "MasterData.StaffMember.Inactive");

        if (staff.LinkedUserId is not null)
            return Error.Conflict("This staff member already has a linked portal account.", "MasterData.StaffMember.AlreadyLinked");

        db.Enqueue(new PortalAccessRequested
        {
            ExternalReferenceId = staff.Id,
            UserType = UserType.StationStaff,
            RoleId = request.RoleId,
            Email = staff.Email,
            DisplayName = staff.FullName
        });

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

// --- Grant portal access to a customer contact ----------------------------

public sealed record GrantContactPortalAccessCommand(Guid CustomerId, Guid ContactId, Guid RoleId) : ICommand;

public sealed class GrantContactPortalAccessCommandValidator : AbstractValidator<GrantContactPortalAccessCommand>
{
    public GrantContactPortalAccessCommandValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.ContactId).NotEmpty();
        RuleFor(x => x.RoleId).NotEmpty();
    }
}

public sealed class GrantContactPortalAccessCommandHandler(IMasterDataDbContext db)
    : ICommandHandler<GrantContactPortalAccessCommand>
{
    public async Task<Result> Handle(GrantContactPortalAccessCommand request, CancellationToken cancellationToken)
    {
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == request.CustomerId, cancellationToken);
        if (customer is null)
            return Error.NotFound("Customer not found.", "MasterData.Customer.NotFound");

        var contact = await db.CustomerContacts
            .FirstOrDefaultAsync(c => c.Id == request.ContactId && c.CustomerId == request.CustomerId, cancellationToken);
        if (contact is null)
            return Error.NotFound("Customer contact not found.", "MasterData.CustomerContact.NotFound");

        if (!contact.IsActive)
            return Error.Validation("Portal access cannot be granted to a removed contact.", "MasterData.CustomerContact.Inactive");

        if (!customer.IsActive)
            return Error.Validation("Portal access cannot be granted while the customer is inactive.", "MasterData.Customer.Inactive");

        if (contact.LinkedUserId is not null)
            return Error.Conflict("This contact already has a linked portal account.", "MasterData.CustomerContact.AlreadyLinked");

        db.Enqueue(new PortalAccessRequested
        {
            ExternalReferenceId = contact.Id,
            UserType = UserType.CustomerContact,
            RoleId = request.RoleId,
            Email = contact.Email,
            DisplayName = contact.Name
        });

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
