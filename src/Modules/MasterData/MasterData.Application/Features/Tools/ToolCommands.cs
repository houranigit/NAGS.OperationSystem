using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Persistence;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using MasterData.Application.Abstractions;
using MasterData.Contracts.Resources;
using MasterData.Contracts.Seeding;
using MasterData.Domain.Tools;
using Microsoft.EntityFrameworkCore;

namespace MasterData.Application.Features.Tools;

public sealed record ToolEquipmentInput(Guid? Id, string FactoryId, string SerialId, DateOnly? CalibrationDate);

public sealed record CreateToolCommand(
    string Name,
    string? Description,
    IReadOnlyList<ToolEquipmentInput>? Equipments,
    ResourceCalculationType? CalculationType = null,
    string? LegacySystemId = null) : ICommand<Guid>;

public sealed class CreateToolCommandValidator : AbstractValidator<CreateToolCommand>
{
    public CreateToolCommandValidator()
    {
        RuleFor(x => x.LegacySystemId).Must(value => value is null || value.Trim().Length <= 200)
            .WithMessage("Legacy system ID must be at most 200 characters.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.CalculationType)
            .Must(type => type is null or ResourceCalculationType.Quantity or ResourceCalculationType.Duration)
            .WithMessage("Calculation type must be Quantity or Duration.");
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
        var result = Tool.Create(
            request.Name,
            request.Description,
            timeProvider.GetUtcNow(),
            calculationType: request.CalculationType ?? ResourceCalculationType.Duration);
        if (result.IsFailure)
            return result.Error;

        var tool = result.Value;
        var legacyIdResult = tool.SetLegacySystemId(request.LegacySystemId, timeProvider.GetUtcNow());
        if (legacyIdResult.IsFailure)
            return legacyIdResult.Error;
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

public sealed record UpdateToolCommand(
    Guid Id,
    string Name,
    string? Description,
    IReadOnlyList<ToolEquipmentInput>? Equipments,
    byte[] RowVersion,
    ResourceCalculationType? CalculationType = null,
    string? LegacySystemId = null) : ICommand;

public sealed class UpdateToolCommandValidator : AbstractValidator<UpdateToolCommand>
{
    public UpdateToolCommandValidator()
    {
        RuleFor(x => x.LegacySystemId).Must(value => value is null || value.Trim().Length <= 200)
            .WithMessage("Legacy system ID must be at most 200 characters.");
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.CalculationType)
            .Must(type => type is null or ResourceCalculationType.Quantity or ResourceCalculationType.Duration)
            .WithMessage("Calculation type must be Quantity or Duration.");
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
        if (tool.Id == WellKnownMasterDataIds.UnknownTool
            && (request.LegacySystemId is null || !(request.Name.Trim() == tool.Name
                && NormalizeOptional(request.Description) == tool.Description
                && (request.CalculationType ?? tool.CalculationType) == tool.CalculationType
                && HasUnchangedEquipment(request.Equipments, tool))))
            return Error.Validation("Only the legacy system ID can be changed for system-seeded records.", "MasterData.Tool.SystemRecord");

        var trimmedName = request.Name.Trim();
        if (await db.Tools.AnyAsync(t => t.Name == trimmedName && t.Id != request.Id, cancellationToken))
            return Error.Conflict("A tool with this name already exists.", "MasterData.Tool.DuplicateName");

        var now = timeProvider.GetUtcNow();
        var result = tool.Update(request.Name, request.Description, now, request.CalculationType);
        if (result.IsFailure)
            return result.Error;

        var requested = request.Equipments ?? [];
        var keepEquipmentIds = requested.Where(e => e.Id.HasValue).Select(e => e.Id!.Value).ToHashSet();

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

                // Existing aggregates assign child IDs in the domain, so tell EF this child is new.
                db.ToolEquipments.Add(add.Value);
                keepEquipmentIds.Add(add.Value.Id);
            }
        }

        foreach (var equipment in tool.Equipments.Where(e => !keepEquipmentIds.Contains(e.Id)).ToList())
        {
            var remove = tool.RemoveEquipment(equipment.Id, now);
            if (remove.IsFailure)
                return remove;
        }

        // Preserve mappings for older callers that omit this optional field; an empty string clears it.
        if (request.LegacySystemId is not null)
        {
            var legacyIdResult = tool.SetLegacySystemId(request.LegacySystemId, timeProvider.GetUtcNow());
            if (legacyIdResult.IsFailure)
                return legacyIdResult.Error;
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

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool HasUnchangedEquipment(IReadOnlyList<ToolEquipmentInput>? requested, Tool tool) =>
        (requested?.Count ?? 0) == tool.Equipments.Count
        && (requested ?? []).Select(input => input.Id).Distinct().Count() == tool.Equipments.Count
        && (requested ?? []).All(input => input.Id is { } id
            && tool.Equipments.Any(equipment => equipment.Id == id
                && equipment.FactoryId == input.FactoryId.Trim()
                && equipment.SerialId == input.SerialId.Trim()
                && equipment.CalibrationDate == input.CalibrationDate));
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
        if (tool.Id == WellKnownMasterDataIds.UnknownTool)
            return Error.Validation("System-seeded records cannot be modified or deactivated.", "MasterData.Tool.SystemRecord");

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
