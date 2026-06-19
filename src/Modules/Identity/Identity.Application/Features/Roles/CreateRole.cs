using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using Identity.Application.Abstractions;
using Identity.Domain.Roles;
using Microsoft.EntityFrameworkCore;

namespace Identity.Application.Features.Roles;

public sealed record CreateRoleCommand(string Name, string? Description, IReadOnlyList<string> Permissions) : ICommand<Guid>;

public sealed class CreateRoleCommandValidator : AbstractValidator<CreateRoleCommand>
{
    public CreateRoleCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.Permissions).NotNull();
    }
}

public sealed class CreateRoleCommandHandler(IIdentityDbContext db, TimeProvider timeProvider)
    : ICommandHandler<CreateRoleCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateRoleCommand request, CancellationToken cancellationToken)
    {
        var normalized = request.Name.Trim().ToUpperInvariant();
        var exists = await db.Roles.AnyAsync(r => r.NormalizedName == normalized, cancellationToken);
        if (exists)
            return Error.Conflict("A role with this name already exists.", "Identity.Role.DuplicateName");

        var roleResult = Role.Create(request.Name, request.Description, request.Permissions, timeProvider.GetUtcNow());
        if (roleResult.IsFailure)
            return roleResult.Error;

        db.Roles.Add(roleResult.Value);
        await db.SaveChangesAsync(cancellationToken);
        return roleResult.Value.Id;
    }
}
