using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Persistence;
using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using MasterData.Application.Abstractions;
using MasterData.Application.Authorization;
using MasterData.Application.Features.StaffMembers;
using MasterData.Contracts;
using MasterData.Domain.Authorization;
using MasterData.Domain.StaffMembers;
using MasterData.Domain.Stations;
using Microsoft.EntityFrameworkCore;

namespace MasterData.Application.Features.Stations;

// --- Create ---------------------------------------------------------------

/// <summary>
/// A staff member to create together with a new station. <see cref="PortalAccessRoleId"/>, when set,
/// requests portal access in the same transaction and requires the administrator-only grant-access
/// permission.
/// </summary>
public sealed record NewStationStaffInput(
    string FullName,
    string EmployeeId,
    string Email,
    Guid ManpowerTypeId,
    EmploymentContractInput? EmploymentContract,
    IReadOnlyList<DayOfWeek>? WorkingDays,
    IReadOnlyList<StaffLicenseInput> Licenses,
    Guid? PortalAccessRoleId);

public sealed record CreateStationCommand(
    string IataCode,
    string? IcaoCode,
    string Name,
    string? City,
    Guid CountryId,
    IReadOnlyList<NewStationStaffInput> Staff) : ICommand<Guid>;

public sealed class CreateStationCommandValidator : AbstractValidator<CreateStationCommand>
{
    public CreateStationCommandValidator()
    {
        RuleFor(x => x.IataCode).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.City).MaximumLength(100);
        RuleFor(x => x.CountryId).NotEmpty();
    }
}

public sealed class CreateStationCommandHandler(IMasterDataDbContext db, IUserContext userContext, TimeProvider timeProvider)
    : ICommandHandler<CreateStationCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateStationCommand request, CancellationToken cancellationToken)
    {
        if (request.Staff is { Count: > 0 } &&
            !userContext.HasPermission(MasterDataPermissions.StaffMembers.Create))
        {
            return Error.Forbidden(
                "Creating staff members with a station requires staff-member create permission.",
                "MasterData.StaffMember.CreateForbidden");
        }

        var now = timeProvider.GetUtcNow();

        var countryCheck = await StationGuards.EnsureActiveCountryAsync(db, request.CountryId, cancellationToken);
        if (countryCheck.IsFailure)
            return countryCheck.Error;

        var result = Station.Create(request.IataCode, request.IcaoCode, request.Name, request.City, request.CountryId, now);
        if (result.IsFailure)
            return result.Error;

        var station = result.Value;

        var conflict = await StationGuards.EnsureCodesAvailableAsync(db, station.IataCode, station.IcaoCode, null, cancellationToken);
        if (conflict.IsFailure)
            return conflict.Error;

        db.Stations.Add(station);

        // Create zero-or-more staff atomically with the station. Any invalid child fails the whole
        // create (single SaveChanges below). Supplying portal access requires the grant-access
        // permission, enforced here as well as at the endpoint.
        var pendingEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pendingEmployeeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var input in request.Staff ?? [])
        {
            var staffResult = await BuildStaffAsync(station.Id, input, pendingEmails, pendingEmployeeIds, now, cancellationToken);
            if (staffResult.IsFailure)
                return staffResult.Error;

            db.StaffMembers.Add(staffResult.Value);

            if (input.PortalAccessRoleId is { } roleId)
            {
                if (!PortalAccessAuthorization.CanGrantStaffAccess(userContext))
                    return PortalAccessAuthorization.GrantForbidden();

                var initiatingUser = PortalAccessAuthorization.ResolveInitiatingUserId(userContext);
                if (initiatingUser.IsFailure)
                    return initiatingUser.Error;

                var correlationId = Guid.NewGuid();
                staffResult.Value.RequestPortalAccess(correlationId, now);

                db.Enqueue(new PortalAccessRequested
                {
                    InitiatedByUserId = initiatingUser.Value,
                    ExternalReferenceId = staffResult.Value.Id,
                    UserType = UserType.StationStaff,
                    RoleId = roleId,
                    Email = staffResult.Value.Email,
                    DisplayName = staffResult.Value.FullName,
                    CorrelationId = correlationId
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return station.Id;
    }

    private async Task<Result<StaffMember>> BuildStaffAsync(
        Guid stationId, NewStationStaffInput input, HashSet<string> pendingEmails, HashSet<string> pendingEmployeeIds,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        // The station is the freshly-created active one (not yet persisted), so only the manpower
        // type needs to be validated against the database here.
        var manpowerType = await db.ManpowerTypes.FirstOrDefaultAsync(m => m.Id == input.ManpowerTypeId, cancellationToken);
        if (manpowerType is null)
            return Error.NotFound("The selected manpower type was not found.", "MasterData.StaffMember.ManpowerTypeNotFound");
        if (!manpowerType.IsActive)
            return Error.Validation("The selected manpower type is inactive.", "MasterData.StaffMember.ManpowerTypeInactive");

        var contract = StaffMemberGuards.BuildContract(input.EmploymentContract);
        if (contract.IsFailure)
            return contract.Error;

        var schedule = StaffMemberGuards.BuildSchedule(input.WorkingDays);
        if (schedule.IsFailure)
            return schedule.Error;

        var created = StaffMember.Create(input.FullName, input.EmployeeId, input.Email, stationId, input.ManpowerTypeId, contract.Value, schedule.Value, now);
        if (created.IsFailure)
            return created.Error;

        var staff = created.Value;

        var employeeIdCheck = await StaffMemberGuards.EnsureEmployeeIdAvailableAsync(db, staff.EmployeeId, null, cancellationToken);
        if (employeeIdCheck.IsFailure)
            return employeeIdCheck.Error;

        if (!pendingEmployeeIds.Add(staff.EmployeeId))
            return Error.Conflict("Two staff members in the request share an employee ID.", "MasterData.StaffMember.DuplicateEmployeeId");

        if (!pendingEmails.Add(staff.Email))
            return Error.Conflict("Two staff members in the request share an email.", "MasterData.StaffMember.DuplicateEmail");

        var emailCheck = await StaffMemberGuards.EnsureEmailAvailableAsync(db, staff.Email, null, cancellationToken);
        if (emailCheck.IsFailure)
            return emailCheck.Error;

        var licensesCheck = await StaffMemberGuards.EnsureLicensesExistAsync(db, input.Licenses, cancellationToken);
        if (licensesCheck.IsFailure)
            return licensesCheck.Error;

        var reconcile = staff.ReconcileLicenses(StaffMemberGuards.MapLicenses(input.Licenses), now);
        if (reconcile.IsFailure)
            return reconcile.Error;

        return staff;
    }
}

// --- Update ---------------------------------------------------------------

public sealed record UpdateStationCommand(Guid Id, string IataCode, string? IcaoCode, string Name, string? City, Guid CountryId, byte[] RowVersion) : ICommand;

public sealed class UpdateStationCommandValidator : AbstractValidator<UpdateStationCommand>
{
    public UpdateStationCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.IataCode).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.City).MaximumLength(100);
        RuleFor(x => x.CountryId).NotEmpty();
        RuleFor(x => x.RowVersion).NotEmpty();
    }
}

public sealed class UpdateStationCommandHandler(IMasterDataDbContext db, IMasterDataScope scope, TimeProvider timeProvider)
    : ICommandHandler<UpdateStationCommand>
{
    public async Task<Result> Handle(UpdateStationCommand request, CancellationToken cancellationToken)
    {
        var scopeCheck = await scope.CheckStationForWriteAsync(request.Id, cancellationToken);
        if (scopeCheck.IsFailure)
            return scopeCheck.Error;

        var station = await db.Stations.FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken);
        if (station is null)
            return Error.NotFound("Station not found.", "MasterData.Station.NotFound");

        var countryCheck = await StationGuards.EnsureActiveCountryAsync(db, request.CountryId, cancellationToken);
        if (countryCheck.IsFailure)
            return countryCheck.Error;

        var result = station.Update(request.IataCode, request.IcaoCode, request.Name, request.City, request.CountryId, timeProvider.GetUtcNow());
        if (result.IsFailure)
            return result.Error;

        var conflict = await StationGuards.EnsureCodesAvailableAsync(db, station.IataCode, station.IcaoCode, station.Id, cancellationToken);
        if (conflict.IsFailure)
            return conflict.Error;

        db.SetOriginalRowVersion(station, request.RowVersion);

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

public sealed record ActivateStationCommand(Guid Id, byte[] RowVersion) : ICommand;

public sealed record DeactivateStationCommand(Guid Id, byte[] RowVersion) : ICommand;

public sealed class ActivateStationCommandHandler(IMasterDataDbContext db, IMasterDataScope scope, TimeProvider timeProvider)
    : ICommandHandler<ActivateStationCommand>
{
    public async Task<Result> Handle(ActivateStationCommand request, CancellationToken cancellationToken)
    {
        var scopeCheck = await scope.CheckStationForWriteAsync(request.Id, cancellationToken);
        if (scopeCheck.IsFailure)
            return scopeCheck.Error;

        var station = await db.Stations.FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken);
        if (station is null)
            return Error.NotFound("Station not found.", "MasterData.Station.NotFound");

        station.Activate(timeProvider.GetUtcNow());
        db.SetOriginalRowVersion(station, request.RowVersion);

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

public sealed class DeactivateStationCommandHandler(IMasterDataDbContext db, IMasterDataScope scope, TimeProvider timeProvider)
    : ICommandHandler<DeactivateStationCommand>
{
    public async Task<Result> Handle(DeactivateStationCommand request, CancellationToken cancellationToken)
    {
        var scopeCheck = await scope.CheckStationForWriteAsync(request.Id, cancellationToken);
        if (scopeCheck.IsFailure)
            return scopeCheck.Error;

        var station = await db.Stations.FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken);
        if (station is null)
            return Error.NotFound("Station not found.", "MasterData.Station.NotFound");

        var now = timeProvider.GetUtcNow();

        if (station.IsActive)
        {
            // Deactivating a station blocks access for all of its linked staff members.
            var linkedStaff = await db.StaffMembers
                .Where(s => s.StationId == station.Id && s.IsActive && s.LinkedUserId != null)
                .ToListAsync(cancellationToken);

            foreach (var staff in linkedStaff)
            {
                staff.SuspendPortal(now);
                PortalAccess.PortalLifecycle.EnqueueDeactivation(db, staff.Id, staff.LinkedUserId!.Value);
            }
        }

        station.Deactivate(now);
        db.SetOriginalRowVersion(station, request.RowVersion);

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

internal static class StationGuards
{
    public static async Task<Result> EnsureActiveCountryAsync(IMasterDataDbContext db, Guid countryId, CancellationToken cancellationToken)
    {
        var country = await db.Countries.FirstOrDefaultAsync(c => c.Id == countryId, cancellationToken);
        if (country is null)
            return Error.NotFound("The selected country was not found.", "MasterData.Station.CountryNotFound");

        if (!country.IsActive)
            return Error.Validation("The selected country is inactive.", "MasterData.Station.CountryInactive");

        return Result.Success();
    }

    public static async Task<Result> EnsureCodesAvailableAsync(
        IMasterDataDbContext db, string iataCode, string? icaoCode, Guid? excludeId, CancellationToken cancellationToken)
    {
        var iataTaken = await db.Stations.AnyAsync(s => s.IataCode == iataCode && (excludeId == null || s.Id != excludeId), cancellationToken);
        if (iataTaken)
            return Error.Conflict("A station with this IATA code already exists.", "MasterData.Station.DuplicateIata");

        if (icaoCode is not null)
        {
            var icaoTaken = await db.Stations.AnyAsync(s => s.IcaoCode == icaoCode && (excludeId == null || s.Id != excludeId), cancellationToken);
            if (icaoTaken)
                return Error.Conflict("A station with this ICAO code already exists.", "MasterData.Station.DuplicateIcao");
        }

        return Result.Success();
    }
}
