using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Persistence;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using MasterData.Application.Abstractions;
using MasterData.Domain.Materials;
using Microsoft.EntityFrameworkCore;

namespace MasterData.Application.Features.Materials;

public sealed record CreateMaterialCommand(string Name, string? Description) : ICommand<Guid>;

public sealed class CreateMaterialCommandValidator : AbstractValidator<CreateMaterialCommand>
{
    public CreateMaterialCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(500);
    }
}

public sealed class CreateMaterialCommandHandler(IMasterDataDbContext db, TimeProvider timeProvider)
    : ICommandHandler<CreateMaterialCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateMaterialCommand request, CancellationToken cancellationToken)
    {
        var result = Material.Create(request.Name, request.Description, timeProvider.GetUtcNow());
        if (result.IsFailure)
            return result.Error;

        var material = result.Value;
        if (await db.Materials.AnyAsync(m => m.Name == material.Name, cancellationToken))
            return Error.Conflict("A material with this name already exists.", "MasterData.Material.DuplicateName");

        db.Materials.Add(material);
        await db.SaveChangesAsync(cancellationToken);
        return material.Id;
    }
}

public sealed record UpdateMaterialCommand(Guid Id, string Name, string? Description, byte[] RowVersion) : ICommand;

public sealed class UpdateMaterialCommandValidator : AbstractValidator<UpdateMaterialCommand>
{
    public UpdateMaterialCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.RowVersion).NotEmpty();
    }
}

public sealed class UpdateMaterialCommandHandler(IMasterDataDbContext db, TimeProvider timeProvider)
    : ICommandHandler<UpdateMaterialCommand>
{
    public async Task<Result> Handle(UpdateMaterialCommand request, CancellationToken cancellationToken)
    {
        var material = await db.Materials.FirstOrDefaultAsync(m => m.Id == request.Id, cancellationToken);
        if (material is null)
            return Error.NotFound("Material not found.", "MasterData.Material.NotFound");

        var trimmedName = request.Name.Trim();
        if (await db.Materials.AnyAsync(m => m.Name == trimmedName && m.Id != request.Id, cancellationToken))
            return Error.Conflict("A material with this name already exists.", "MasterData.Material.DuplicateName");

        var result = material.Update(request.Name, request.Description, timeProvider.GetUtcNow());
        if (result.IsFailure)
            return result.Error;

        db.SetOriginalRowVersion(material, request.RowVersion);

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

public sealed record ActivateMaterialCommand(Guid Id, byte[] RowVersion) : ICommand;
public sealed record DeactivateMaterialCommand(Guid Id, byte[] RowVersion) : ICommand;

public sealed class ActivateMaterialCommandHandler(IMasterDataDbContext db, TimeProvider timeProvider)
    : ICommandHandler<ActivateMaterialCommand>
{
    public async Task<Result> Handle(ActivateMaterialCommand request, CancellationToken cancellationToken)
    {
        var material = await db.Materials.FirstOrDefaultAsync(m => m.Id == request.Id, cancellationToken);
        if (material is null)
            return Error.NotFound("Material not found.", "MasterData.Material.NotFound");

        material.Activate(timeProvider.GetUtcNow());
        db.SetOriginalRowVersion(material, request.RowVersion);

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

public sealed class DeactivateMaterialCommandHandler(IMasterDataDbContext db, TimeProvider timeProvider)
    : ICommandHandler<DeactivateMaterialCommand>
{
    public async Task<Result> Handle(DeactivateMaterialCommand request, CancellationToken cancellationToken)
    {
        var material = await db.Materials.FirstOrDefaultAsync(m => m.Id == request.Id, cancellationToken);
        if (material is null)
            return Error.NotFound("Material not found.", "MasterData.Material.NotFound");

        material.Deactivate(timeProvider.GetUtcNow());
        db.SetOriginalRowVersion(material, request.RowVersion);

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
