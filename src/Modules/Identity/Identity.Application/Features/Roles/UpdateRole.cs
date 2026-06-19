using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using Identity.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Identity.Application.Features.Roles;

public sealed record UpdateRoleCommand(Guid Id, string Name, string? Description) : ICommand;

public sealed class UpdateRoleCommandValidator : AbstractValidator<UpdateRoleCommand>
{
    public UpdateRoleCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
    }
}

public sealed class UpdateRoleCommandHandler(IIdentityDbContext db, TimeProvider timeProvider)
    : ICommandHandler<UpdateRoleCommand>
{
    public async Task<Result> Handle(UpdateRoleCommand request, CancellationToken cancellationToken)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken);
        if (role is null)
            return Error.NotFound("Role not found.", "Identity.Role.NotFound");

        if (role.IsSystem)
            return Error.Conflict("System roles cannot be modified.", "Identity.Role.SystemProtected");

        var normalized = request.Name.Trim().ToUpperInvariant();
        var duplicate = await db.Roles.AnyAsync(r => r.NormalizedName == normalized && r.Id != role.Id, cancellationToken);
        if (duplicate)
            return Error.Conflict("A role with this name already exists.", "Identity.Role.DuplicateName");

        var result = role.Update(request.Name, request.Description, timeProvider.GetUtcNow());
        if (result.IsFailure)
            return result.Error;

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
