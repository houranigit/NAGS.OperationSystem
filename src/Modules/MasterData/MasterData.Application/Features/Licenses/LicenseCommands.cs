using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Persistence;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using MasterData.Application.Abstractions;
using MasterData.Domain.Licenses;
using Microsoft.EntityFrameworkCore;

namespace MasterData.Application.Features.Licenses;

// --- Create ---------------------------------------------------------------

public sealed record CreateLicenseCommand(string Code, string Name, string? Description) : ICommand<Guid>;

public sealed class CreateLicenseCommandValidator : AbstractValidator<CreateLicenseCommand>
{
    public CreateLicenseCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
    }
}

public sealed class CreateLicenseCommandHandler(IMasterDataDbContext db, TimeProvider timeProvider)
    : ICommandHandler<CreateLicenseCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateLicenseCommand request, CancellationToken cancellationToken)
    {
        var result = License.Create(request.Code, request.Name, request.Description, timeProvider.GetUtcNow());
        if (result.IsFailure)
            return result.Error;

        var license = result.Value;
        var codeExists = await db.Licenses.AnyAsync(l => l.Code == license.Code, cancellationToken);
        if (codeExists)
            return Error.Conflict("A license with this code already exists.", "MasterData.License.DuplicateCode");

        db.Licenses.Add(license);
        await db.SaveChangesAsync(cancellationToken);
        return license.Id;
    }
}

// --- Update ---------------------------------------------------------------

public sealed record UpdateLicenseCommand(Guid Id, string Name, string? Description, byte[] RowVersion) : ICommand;

public sealed class UpdateLicenseCommandValidator : AbstractValidator<UpdateLicenseCommand>
{
    public UpdateLicenseCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.RowVersion).NotEmpty();
    }
}

public sealed class UpdateLicenseCommandHandler(IMasterDataDbContext db, TimeProvider timeProvider)
    : ICommandHandler<UpdateLicenseCommand>
{
    public async Task<Result> Handle(UpdateLicenseCommand request, CancellationToken cancellationToken)
    {
        var license = await db.Licenses.FirstOrDefaultAsync(l => l.Id == request.Id, cancellationToken);
        if (license is null)
            return Error.NotFound("License not found.", "MasterData.License.NotFound");

        var result = license.Update(request.Name, request.Description, timeProvider.GetUtcNow());
        if (result.IsFailure)
            return result.Error;

        db.SetOriginalRowVersion(license, request.RowVersion);

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

// --- Activate / Deactivate ------------------------------------------------

public sealed record ActivateLicenseCommand(Guid Id, byte[] RowVersion) : ICommand;

public sealed record DeactivateLicenseCommand(Guid Id, byte[] RowVersion) : ICommand;

public sealed class ActivateLicenseCommandHandler(IMasterDataDbContext db, TimeProvider timeProvider)
    : ICommandHandler<ActivateLicenseCommand>
{
    public async Task<Result> Handle(ActivateLicenseCommand request, CancellationToken cancellationToken)
    {
        var license = await db.Licenses.FirstOrDefaultAsync(l => l.Id == request.Id, cancellationToken);
        if (license is null)
            return Error.NotFound("License not found.", "MasterData.License.NotFound");

        license.Activate(timeProvider.GetUtcNow());
        db.SetOriginalRowVersion(license, request.RowVersion);

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

public sealed class DeactivateLicenseCommandHandler(IMasterDataDbContext db, TimeProvider timeProvider)
    : ICommandHandler<DeactivateLicenseCommand>
{
    public async Task<Result> Handle(DeactivateLicenseCommand request, CancellationToken cancellationToken)
    {
        var license = await db.Licenses.FirstOrDefaultAsync(l => l.Id == request.Id, cancellationToken);
        if (license is null)
            return Error.NotFound("License not found.", "MasterData.License.NotFound");

        license.Deactivate(timeProvider.GetUtcNow());
        db.SetOriginalRowVersion(license, request.RowVersion);

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
