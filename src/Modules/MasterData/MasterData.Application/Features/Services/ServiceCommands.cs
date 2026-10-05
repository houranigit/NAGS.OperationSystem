using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Persistence;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using MasterData.Application.Abstractions;
using MasterData.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace MasterData.Application.Features.Services;

public sealed record CreateServiceCommand(string Name, string? Description, string? LegacySystemId = null) : ICommand<Guid>;

public sealed class CreateServiceCommandValidator : AbstractValidator<CreateServiceCommand>
{
    public CreateServiceCommandValidator()
    {
        RuleFor(x => x.LegacySystemId).Must(value => value is null || value.Trim().Length <= 200)
            .WithMessage("Legacy system ID must be at most 200 characters.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
    }
}

public sealed class CreateServiceCommandHandler(IMasterDataDbContext db, TimeProvider timeProvider)
    : ICommandHandler<CreateServiceCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateServiceCommand request, CancellationToken cancellationToken)
    {
        var result = Service.Create(request.Name, request.Description, timeProvider.GetUtcNow());
        if (result.IsFailure)
            return result.Error;

        var service = result.Value;
        var legacyIdResult = service.SetLegacySystemId(request.LegacySystemId, timeProvider.GetUtcNow());
        if (legacyIdResult.IsFailure)
            return legacyIdResult.Error;
        if (await db.Services.AnyAsync(s => s.Name == service.Name, cancellationToken))
            return Error.Conflict("A service with this name already exists.", "MasterData.Service.DuplicateName");

        db.Services.Add(service);
        await db.SaveChangesAsync(cancellationToken);
        return service.Id;
    }
}

public sealed record UpdateServiceCommand(Guid Id, string Name, string? Description, byte[] RowVersion, string? LegacySystemId = null) : ICommand;

public sealed class UpdateServiceCommandValidator : AbstractValidator<UpdateServiceCommand>
{
    public UpdateServiceCommandValidator()
    {
        RuleFor(x => x.LegacySystemId).Must(value => value is null || value.Trim().Length <= 200)
            .WithMessage("Legacy system ID must be at most 200 characters.");
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.RowVersion).NotEmpty();
    }
}

public sealed class UpdateServiceCommandHandler(IMasterDataDbContext db, TimeProvider timeProvider)
    : ICommandHandler<UpdateServiceCommand>
{
    public async Task<Result> Handle(UpdateServiceCommand request, CancellationToken cancellationToken)
    {
        var service = await db.Services.FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken);
        if (service is null)
            return Error.NotFound("Service not found.", "MasterData.Service.NotFound");
        if (ServiceSystemRecords.IsSystem(service.Id)
            && (request.LegacySystemId is null
                || request.Name.Trim() != service.Name
                || NormalizeOptional(request.Description) != service.Description))
            return Error.Conflict("Only the legacy system ID can be changed for system services.", "MasterData.Service.SystemProtected");

        var trimmedName = request.Name.Trim();
        if (await db.Services.AnyAsync(s => s.Name == trimmedName && s.Id != request.Id, cancellationToken))
            return Error.Conflict("A service with this name already exists.", "MasterData.Service.DuplicateName");

        var result = service.Update(request.Name, request.Description, timeProvider.GetUtcNow());
        if (result.IsFailure)
            return result.Error;

        // Preserve mappings for older callers that omit this optional field; an empty string clears it.
        if (request.LegacySystemId is not null)
        {
            var legacyIdResult = service.SetLegacySystemId(request.LegacySystemId, timeProvider.GetUtcNow());
            if (legacyIdResult.IsFailure)
                return legacyIdResult.Error;
        }

        db.SetOriginalRowVersion(service, request.RowVersion);

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

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record ActivateServiceCommand(Guid Id, byte[] RowVersion) : ICommand;
public sealed record DeactivateServiceCommand(Guid Id, byte[] RowVersion) : ICommand;

public sealed class ActivateServiceCommandHandler(IMasterDataDbContext db, TimeProvider timeProvider)
    : ICommandHandler<ActivateServiceCommand>
{
    public async Task<Result> Handle(ActivateServiceCommand request, CancellationToken cancellationToken)
    {
        var service = await db.Services.FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken);
        if (service is null)
            return Error.NotFound("Service not found.", "MasterData.Service.NotFound");
        if (ServiceSystemRecords.IsSystem(service.Id))
            return Error.Conflict("System services cannot be activated or deactivated.", "MasterData.Service.SystemProtected");

        service.Activate(timeProvider.GetUtcNow());
        db.SetOriginalRowVersion(service, request.RowVersion);

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

public sealed class DeactivateServiceCommandHandler(IMasterDataDbContext db, TimeProvider timeProvider)
    : ICommandHandler<DeactivateServiceCommand>
{
    public async Task<Result> Handle(DeactivateServiceCommand request, CancellationToken cancellationToken)
    {
        var service = await db.Services.FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken);
        if (service is null)
            return Error.NotFound("Service not found.", "MasterData.Service.NotFound");
        if (ServiceSystemRecords.IsSystem(service.Id))
            return Error.Conflict("System services cannot be activated or deactivated.", "MasterData.Service.SystemProtected");

        service.Deactivate(timeProvider.GetUtcNow());
        db.SetOriginalRowVersion(service, request.RowVersion);

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
