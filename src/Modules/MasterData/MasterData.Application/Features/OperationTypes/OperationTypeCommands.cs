using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Persistence;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using MasterData.Application.Abstractions;
using MasterData.Domain.OperationTypes;
using Microsoft.EntityFrameworkCore;

namespace MasterData.Application.Features.OperationTypes;

public sealed record CreateOperationTypeCommand(string Name, string? Description) : ICommand<Guid>;

public sealed class CreateOperationTypeCommandValidator : AbstractValidator<CreateOperationTypeCommand>
{
    public CreateOperationTypeCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
    }
}

public sealed class CreateOperationTypeCommandHandler(IMasterDataDbContext db, TimeProvider timeProvider)
    : ICommandHandler<CreateOperationTypeCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateOperationTypeCommand request, CancellationToken cancellationToken)
    {
        var result = OperationType.Create(request.Name, request.Description, timeProvider.GetUtcNow());
        if (result.IsFailure)
            return result.Error;

        var operationType = result.Value;
        if (await db.OperationTypes.AnyAsync(o => o.Name == operationType.Name, cancellationToken))
            return Error.Conflict("An operation type with this name already exists.", "MasterData.OperationType.DuplicateName");

        db.OperationTypes.Add(operationType);
        await db.SaveChangesAsync(cancellationToken);
        return operationType.Id;
    }
}

public sealed record UpdateOperationTypeCommand(Guid Id, string Name, string? Description, byte[] RowVersion) : ICommand;

public sealed class UpdateOperationTypeCommandValidator : AbstractValidator<UpdateOperationTypeCommand>
{
    public UpdateOperationTypeCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.RowVersion).NotEmpty();
    }
}

public sealed class UpdateOperationTypeCommandHandler(IMasterDataDbContext db, TimeProvider timeProvider)
    : ICommandHandler<UpdateOperationTypeCommand>
{
    public async Task<Result> Handle(UpdateOperationTypeCommand request, CancellationToken cancellationToken)
    {
        var operationType = await db.OperationTypes.FirstOrDefaultAsync(o => o.Id == request.Id, cancellationToken);
        if (operationType is null)
            return Error.NotFound("Operation type not found.", "MasterData.OperationType.NotFound");
        if (OperationTypeSystemRecords.IsSystem(operationType.Id))
            return Error.Conflict("System operation types cannot be modified.", "MasterData.OperationType.SystemProtected");

        var trimmedName = request.Name.Trim();
        if (await db.OperationTypes.AnyAsync(o => o.Name == trimmedName && o.Id != request.Id, cancellationToken))
            return Error.Conflict("An operation type with this name already exists.", "MasterData.OperationType.DuplicateName");

        var result = operationType.Update(request.Name, request.Description, timeProvider.GetUtcNow());
        if (result.IsFailure)
            return result.Error;

        db.SetOriginalRowVersion(operationType, request.RowVersion);

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

public sealed record ActivateOperationTypeCommand(Guid Id, byte[] RowVersion) : ICommand;
public sealed record DeactivateOperationTypeCommand(Guid Id, byte[] RowVersion) : ICommand;

public sealed class ActivateOperationTypeCommandHandler(IMasterDataDbContext db, TimeProvider timeProvider)
    : ICommandHandler<ActivateOperationTypeCommand>
{
    public async Task<Result> Handle(ActivateOperationTypeCommand request, CancellationToken cancellationToken)
    {
        var operationType = await db.OperationTypes.FirstOrDefaultAsync(o => o.Id == request.Id, cancellationToken);
        if (operationType is null)
            return Error.NotFound("Operation type not found.", "MasterData.OperationType.NotFound");
        if (OperationTypeSystemRecords.IsSystem(operationType.Id))
            return Error.Conflict("System operation types cannot be activated or deactivated.", "MasterData.OperationType.SystemProtected");

        operationType.Activate(timeProvider.GetUtcNow());
        db.SetOriginalRowVersion(operationType, request.RowVersion);

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

public sealed class DeactivateOperationTypeCommandHandler(IMasterDataDbContext db, TimeProvider timeProvider)
    : ICommandHandler<DeactivateOperationTypeCommand>
{
    public async Task<Result> Handle(DeactivateOperationTypeCommand request, CancellationToken cancellationToken)
    {
        var operationType = await db.OperationTypes.FirstOrDefaultAsync(o => o.Id == request.Id, cancellationToken);
        if (operationType is null)
            return Error.NotFound("Operation type not found.", "MasterData.OperationType.NotFound");
        if (OperationTypeSystemRecords.IsSystem(operationType.Id))
            return Error.Conflict("System operation types cannot be activated or deactivated.", "MasterData.OperationType.SystemProtected");

        operationType.Deactivate(timeProvider.GetUtcNow());
        db.SetOriginalRowVersion(operationType, request.RowVersion);

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
