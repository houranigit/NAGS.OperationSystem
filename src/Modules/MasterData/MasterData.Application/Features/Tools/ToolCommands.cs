using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Persistence;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using MasterData.Application.Abstractions;
using MasterData.Domain.Tools;
using Microsoft.EntityFrameworkCore;

namespace MasterData.Application.Features.Tools;

public sealed record ToolEquipmentInput(Guid? Id, string FactoryId, string SerialId, DateOnly? CalibrationDate);

public sealed record CreateToolCommand(string Name, string? Description, IReadOnlyList<ToolEquipmentInput>? Equipments) : ICommand<Guid>;

public sealed class CreateToolCommandValidator : AbstractValidator<CreateToolCommand>
{
    public CreateToolCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
        RuleForEach(x => x.Equipments).SetValidator(new ToolEquipmentInputValidator());
    }
}

public sealed class ToolEquipmentInputValidator : AbstractValidator<ToolEquipmentInput>
{
    public ToolEquipmentInputValidator()
    {
        RuleFor(x => x.FactoryId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SerialId).NotEmpty().MaximumLength(100);
    }
}

public sealed class CreateToolCommandHandler(IMasterDataDbContext db, TimeProvider timeProvider)
    : ICommandHandler<CreateToolCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateToolCommand request, CancellationToken cancellationToken)
    {
        var result = Tool.Create(request.Name, request.Description, timeProvider.GetUtcNow());
        if (result.IsFailure)
            return result.Error;

        var tool = result.Value;
        if (await db.Tools.AnyAsync(t => t.Name == tool.Name, cancellationToken))
            return Error.Conflict("A tool with this name already exists.", "MasterData.Tool.DuplicateName");

        var now = timeProvider.GetUtcNow();
        foreach (var equipment in request.Equipments ?? [])
        {
            var add = tool.AddEquipment(equipment.FactoryId, equipment.SerialId, equipment.CalibrationDate, now);
            if (add.IsFailure)
                return add.Error;
        }

        db.Tools.Add(tool);
        await db.SaveChangesAsync(cancellationToken);
        return tool.Id;
    }
}

public sealed record UpdateToolCommand(Guid Id, string Name, string? Description, IReadOnlyList<ToolEquipmentInput>? Equipments, byte[] RowVersion) : ICommand;

public sealed class UpdateToolCommandValidator : AbstractValidator<UpdateToolCommand>
{
    public UpdateToolCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.RowVersion).NotEmpty();
        RuleForEach(x => x.Equipments).SetValidator(new ToolEquipmentInputValidator());
    }
}

public sealed class UpdateToolCommandHandler(IMasterDataDbContext db, TimeProvider timeProvider)
    : ICommandHandler<UpdateToolCommand>
{
    public async Task<Result> Handle(UpdateToolCommand request, CancellationToken cancellationToken)
    {
        var tool = await db.Tools
            .Include(t => t.Equipments)
            .FirstOrDefaultAsync(t => t.Id == request.Id, cancellationToken);
        if (tool is null)
            return Error.NotFound("Tool not found.", "MasterData.Tool.NotFound");

        var trimmedName = request.Name.Trim();
        if (await db.Tools.AnyAsync(t => t.Name == trimmedName && t.Id != request.Id, cancellationToken))
            return Error.Conflict("A tool with this name already exists.", "MasterData.Tool.DuplicateName");

        var now = timeProvider.GetUtcNow();
        var result = tool.Update(request.Name, request.Description, now);
        if (result.IsFailure)
            return result.Error;

        var requested = request.Equipments ?? [];
        var requestedExistingIds = requested.Where(e => e.Id.HasValue).Select(e => e.Id!.Value).ToHashSet();

        foreach (var equipment in requested)
        {
            if (equipment.Id is { } equipmentId)
            {
                var update = tool.UpdateEquipment(equipmentId, equipment.FactoryId, equipment.SerialId, equipment.CalibrationDate, now);
                if (update.IsFailure)
                    return update;
            }
            else
            {
                var add = tool.AddEquipment(equipment.FactoryId, equipment.SerialId, equipment.CalibrationDate, now);
                if (add.IsFailure)
                    return add.Error;
            }
        }

        foreach (var equipment in tool.Equipments.Where(e => !requestedExistingIds.Contains(e.Id)).ToList())
        {
            var remove = tool.RemoveEquipment(equipment.Id, now);
            if (remove.IsFailure)
                return remove;
        }

        db.SetOriginalRowVersion(tool, request.RowVersion);

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

public sealed record ActivateToolCommand(Guid Id, byte[] RowVersion) : ICommand;
public sealed record DeactivateToolCommand(Guid Id, byte[] RowVersion) : ICommand;

public sealed class ActivateToolCommandHandler(IMasterDataDbContext db, TimeProvider timeProvider)
    : ICommandHandler<ActivateToolCommand>
{
    public async Task<Result> Handle(ActivateToolCommand request, CancellationToken cancellationToken)
    {
        var tool = await db.Tools.FirstOrDefaultAsync(t => t.Id == request.Id, cancellationToken);
        if (tool is null)
            return Error.NotFound("Tool not found.", "MasterData.Tool.NotFound");

        tool.Activate(timeProvider.GetUtcNow());
        db.SetOriginalRowVersion(tool, request.RowVersion);

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

public sealed class DeactivateToolCommandHandler(IMasterDataDbContext db, TimeProvider timeProvider)
    : ICommandHandler<DeactivateToolCommand>
{
    public async Task<Result> Handle(DeactivateToolCommand request, CancellationToken cancellationToken)
    {
        var tool = await db.Tools.FirstOrDefaultAsync(t => t.Id == request.Id, cancellationToken);
        if (tool is null)
            return Error.NotFound("Tool not found.", "MasterData.Tool.NotFound");

        tool.Deactivate(timeProvider.GetUtcNow());
        db.SetOriginalRowVersion(tool, request.RowVersion);

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
