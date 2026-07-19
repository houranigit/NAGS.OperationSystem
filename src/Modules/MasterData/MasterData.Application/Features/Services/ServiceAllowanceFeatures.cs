using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Mobile;
using BuildingBlocks.Application.Persistence;
using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using MasterData.Application.Abstractions;
using MasterData.Application.Contracts;
using MasterData.Contracts.Seeding;
using MasterData.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace MasterData.Application.Features.Services;

public sealed record GetPerformedServiceOptionsQuery : IQuery<IReadOnlyList<ServiceOptionDto>>;

public sealed class GetPerformedServiceOptionsQueryHandler(IMasterDataDbContext db, IUserContext user)
    : IQueryHandler<GetPerformedServiceOptionsQuery, IReadOnlyList<ServiceOptionDto>>
{
    public async Task<Result<IReadOnlyList<ServiceOptionDto>>> Handle(
        GetPerformedServiceOptionsQuery request,
        CancellationToken cancellationToken)
    {
        var query = db.Services.AsNoTracking()
            .Where(service => service.IsActive && service.Id != WellKnownMasterDataIds.AircraftPerLandingService);

        if (user.UserType == UserType.StationStaff && user.ExternalReferenceId is { } staffMemberId)
        {
            var manpowerTypeId = await db.StaffMembers.AsNoTracking()
                .Where(staff => staff.Id == staffMemberId && staff.IsActive)
                .Select(staff => (Guid?)staff.ManpowerTypeId)
                .FirstOrDefaultAsync(cancellationToken);

            if (manpowerTypeId is null)
                return ScopeDenied();

            var manpowerTypeActive = await db.ManpowerTypes.AsNoTracking()
                .AnyAsync(type => type.Id == manpowerTypeId.Value && type.IsActive, cancellationToken);
            if (!manpowerTypeActive)
                return ScopeDenied();

            query = query.Where(service => db.ManpowerTypeAllowedServices.Any(allowance =>
                allowance.ManpowerTypeId == manpowerTypeId.Value && allowance.ServiceId == service.Id));
        }
        else if (user.UserType != UserType.SystemAdministrator)
        {
            return ScopeDenied();
        }

        IReadOnlyList<ServiceOptionDto> options = await query
            .OrderBy(service => service.Name)
            .ThenBy(service => service.Id)
            .Select(service => new ServiceOptionDto(service.Id, service.Name, false))
            .ToListAsync(cancellationToken);

        return Result.Success(options);
    }

    private static Error ScopeDenied() =>
        Error.Forbidden(
            "Your linked staff member or manpower type is missing or inactive.",
            "MasterData.ServiceAllowance.ScopeDenied");
}

public sealed record GetServiceAllowancesForManpowerTypeQuery(Guid ManpowerTypeId)
    : IQuery<IReadOnlyList<ServiceAllowanceDto>>;

public sealed class GetServiceAllowancesForManpowerTypeQueryHandler(IMasterDataDbContext db)
    : IQueryHandler<GetServiceAllowancesForManpowerTypeQuery, IReadOnlyList<ServiceAllowanceDto>>
{
    public async Task<Result<IReadOnlyList<ServiceAllowanceDto>>> Handle(
        GetServiceAllowancesForManpowerTypeQuery request,
        CancellationToken cancellationToken)
    {
        if (!await db.ManpowerTypes.AsNoTracking().AnyAsync(type => type.Id == request.ManpowerTypeId, cancellationToken))
            return Error.NotFound("Manpower type not found.", "MasterData.ManpowerType.NotFound");

        IReadOnlyList<ServiceAllowanceDto> items = await db.Services.AsNoTracking()
            .Where(service => service.Id != WellKnownMasterDataIds.AircraftPerLandingService)
            .OrderBy(service => service.Name)
            .ThenBy(service => service.Id)
            .Select(service => new ServiceAllowanceDto(
                service.Id,
                service.Name,
                service.IsActive,
                db.ManpowerTypeAllowedServices.Any(allowance =>
                    allowance.ManpowerTypeId == request.ManpowerTypeId && allowance.ServiceId == service.Id)))
            .ToListAsync(cancellationToken);

        return Result.Success(items);
    }
}

public sealed record GetManpowerTypeAllowancesForServiceQuery(Guid ServiceId)
    : IQuery<IReadOnlyList<ManpowerTypeAllowanceDto>>;

public sealed class GetManpowerTypeAllowancesForServiceQueryHandler(IMasterDataDbContext db)
    : IQueryHandler<GetManpowerTypeAllowancesForServiceQuery, IReadOnlyList<ManpowerTypeAllowanceDto>>
{
    public async Task<Result<IReadOnlyList<ManpowerTypeAllowanceDto>>> Handle(
        GetManpowerTypeAllowancesForServiceQuery request,
        CancellationToken cancellationToken)
    {
        var service = await db.Services.AsNoTracking()
            .Where(item => item.Id == request.ServiceId)
            .Select(item => new { item.Id })
            .FirstOrDefaultAsync(cancellationToken);
        if (service is null)
            return Error.NotFound("Service not found.", "MasterData.Service.NotFound");
        if (request.ServiceId == WellKnownMasterDataIds.AircraftPerLandingService)
            return Error.Conflict("System services cannot be used as performed-service allowances.", "MasterData.ServiceAllowance.SystemProtected");

        IReadOnlyList<ManpowerTypeAllowanceDto> items = await db.ManpowerTypes.AsNoTracking()
            .OrderBy(type => type.Name)
            .ThenBy(type => type.Id)
            .Select(type => new ManpowerTypeAllowanceDto(
                type.Id,
                type.Name,
                type.IsActive,
                db.ManpowerTypeAllowedServices.Any(allowance =>
                    allowance.ServiceId == request.ServiceId && allowance.ManpowerTypeId == type.Id)))
            .ToListAsync(cancellationToken);

        return Result.Success(items);
    }
}

public sealed record UpdateServiceAllowancesForManpowerTypeCommand(
    Guid ManpowerTypeId,
    IReadOnlyList<Guid> ServiceIds,
    byte[] RowVersion) : ICommand;

public sealed class UpdateServiceAllowancesForManpowerTypeCommandValidator
    : AbstractValidator<UpdateServiceAllowancesForManpowerTypeCommand>
{
    public UpdateServiceAllowancesForManpowerTypeCommandValidator()
    {
        RuleFor(command => command.ManpowerTypeId).NotEmpty();
        RuleFor(command => command.ServiceIds).NotNull();
        RuleFor(command => command.RowVersion).NotEmpty();
    }
}

public sealed class UpdateServiceAllowancesForManpowerTypeCommandHandler(
    IMasterDataDbContext db,
    TimeProvider timeProvider,
    IMobileSyncBroadcaster mobileSync)
    : ICommandHandler<UpdateServiceAllowancesForManpowerTypeCommand>
{
    public async Task<Result> Handle(
        UpdateServiceAllowancesForManpowerTypeCommand request,
        CancellationToken cancellationToken)
    {
        var requestedIds = request.ServiceIds.Distinct().ToHashSet();
        if (requestedIds.Contains(WellKnownMasterDataIds.AircraftPerLandingService))
            return SystemServiceError();

        await using var transaction = await db.BeginSerializableTransactionAsync(cancellationToken);
        var manpowerType = await db.ManpowerTypes.FirstOrDefaultAsync(
            type => type.Id == request.ManpowerTypeId,
            cancellationToken);
        if (manpowerType is null)
            return Error.NotFound("Manpower type not found.", "MasterData.ManpowerType.NotFound");

        var services = await db.Services
            .Where(service => requestedIds.Contains(service.Id))
            .ToListAsync(cancellationToken);
        if (services.Count != requestedIds.Count)
            return Error.Validation("One or more selected services do not exist.", "MasterData.ServiceAllowance.ServiceNotFound");

        var existing = await db.ManpowerTypeAllowedServices
            .Where(allowance => allowance.ManpowerTypeId == request.ManpowerTypeId)
            .ToListAsync(cancellationToken);
        var existingIds = existing.Select(allowance => allowance.ServiceId).ToHashSet();
        var removed = existing.Where(allowance => !requestedIds.Contains(allowance.ServiceId)).ToList();
        var addedIds = requestedIds.Except(existingIds).ToList();
        var changedServiceIds = removed.Select(allowance => allowance.ServiceId).Concat(addedIds).ToHashSet();

        db.ManpowerTypeAllowedServices.RemoveRange(removed);
        db.ManpowerTypeAllowedServices.AddRange(addedIds.Select(serviceId =>
            ManpowerTypeAllowedService.Create(request.ManpowerTypeId, serviceId)));

        var changedServices = await db.Services
            .Where(service => changedServiceIds.Contains(service.Id))
            .ToListAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        manpowerType.Touch(now);
        foreach (var service in changedServices)
            service.Touch(now);

        db.SetOriginalRowVersion(manpowerType, request.RowVersion);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyErrors.Stale;
        }
        catch (DbUpdateException)
        {
            return Error.Conflict("Service allowances changed concurrently. Reload and try again.", "MasterData.ServiceAllowance.Conflict");
        }

        if (changedServiceIds.Count > 0)
        {
            mobileSync.Enqueue(new MobileSyncChange(
                MobileSyncTables.Services,
                MobileSyncOps.Refresh,
                null,
                MobileSyncAudience.AllStations,
                now));
        }

        return Result.Success();
    }

    private static Error SystemServiceError() =>
        Error.Validation(
            "Aircraft Per Landing cannot be selected as a performed-service allowance.",
            "MasterData.ServiceAllowance.SystemService");
}

public sealed record UpdateManpowerTypeAllowancesForServiceCommand(
    Guid ServiceId,
    IReadOnlyList<Guid> ManpowerTypeIds,
    byte[] RowVersion) : ICommand;

public sealed class UpdateManpowerTypeAllowancesForServiceCommandValidator
    : AbstractValidator<UpdateManpowerTypeAllowancesForServiceCommand>
{
    public UpdateManpowerTypeAllowancesForServiceCommandValidator()
    {
        RuleFor(command => command.ServiceId).NotEmpty();
        RuleFor(command => command.ManpowerTypeIds).NotNull();
        RuleFor(command => command.RowVersion).NotEmpty();
    }
}

public sealed class UpdateManpowerTypeAllowancesForServiceCommandHandler(
    IMasterDataDbContext db,
    TimeProvider timeProvider,
    IMobileSyncBroadcaster mobileSync)
    : ICommandHandler<UpdateManpowerTypeAllowancesForServiceCommand>
{
    public async Task<Result> Handle(
        UpdateManpowerTypeAllowancesForServiceCommand request,
        CancellationToken cancellationToken)
    {
        if (request.ServiceId == WellKnownMasterDataIds.AircraftPerLandingService)
            return Error.Conflict("System services cannot be used as performed-service allowances.", "MasterData.ServiceAllowance.SystemProtected");

        var requestedIds = request.ManpowerTypeIds.Distinct().ToHashSet();
        await using var transaction = await db.BeginSerializableTransactionAsync(cancellationToken);
        var service = await db.Services.FirstOrDefaultAsync(item => item.Id == request.ServiceId, cancellationToken);
        if (service is null)
            return Error.NotFound("Service not found.", "MasterData.Service.NotFound");

        var manpowerTypes = await db.ManpowerTypes
            .Where(type => requestedIds.Contains(type.Id))
            .ToListAsync(cancellationToken);
        if (manpowerTypes.Count != requestedIds.Count)
            return Error.Validation("One or more selected manpower types do not exist.", "MasterData.ServiceAllowance.ManpowerTypeNotFound");

        var existing = await db.ManpowerTypeAllowedServices
            .Where(allowance => allowance.ServiceId == request.ServiceId)
            .ToListAsync(cancellationToken);
        var existingIds = existing.Select(allowance => allowance.ManpowerTypeId).ToHashSet();
        var removed = existing.Where(allowance => !requestedIds.Contains(allowance.ManpowerTypeId)).ToList();
        var addedIds = requestedIds.Except(existingIds).ToList();
        var changedManpowerTypeIds = removed.Select(allowance => allowance.ManpowerTypeId).Concat(addedIds).ToHashSet();

        db.ManpowerTypeAllowedServices.RemoveRange(removed);
        db.ManpowerTypeAllowedServices.AddRange(addedIds.Select(manpowerTypeId =>
            ManpowerTypeAllowedService.Create(manpowerTypeId, request.ServiceId)));

        var changedManpowerTypes = await db.ManpowerTypes
            .Where(type => changedManpowerTypeIds.Contains(type.Id))
            .ToListAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        service.Touch(now);
        foreach (var manpowerType in changedManpowerTypes)
            manpowerType.Touch(now);

        db.SetOriginalRowVersion(service, request.RowVersion);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyErrors.Stale;
        }
        catch (DbUpdateException)
        {
            return Error.Conflict("Manpower type allowances changed concurrently. Reload and try again.", "MasterData.ServiceAllowance.Conflict");
        }

        if (changedManpowerTypeIds.Count > 0)
        {
            mobileSync.Enqueue(new MobileSyncChange(
                MobileSyncTables.Services,
                MobileSyncOps.Refresh,
                null,
                MobileSyncAudience.AllStations,
                now));
        }

        return Result.Success();
    }
}
