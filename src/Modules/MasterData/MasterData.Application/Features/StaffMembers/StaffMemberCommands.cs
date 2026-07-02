using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Persistence;
using BuildingBlocks.Contracts.Authorization;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using MasterData.Application.Abstractions;
using MasterData.Application.Authorization;
using MasterData.Contracts;
using MasterData.Domain.Authorization;
using MasterData.Domain.StaffMembers;
using Microsoft.EntityFrameworkCore;

namespace MasterData.Application.Features.StaffMembers;

// --- Shared input shapes --------------------------------------------------

public sealed record EmploymentContractInput(DateOnly StartDate, DateOnly? EndDate);

public sealed record StaffLicenseInput(Guid? Id, Guid LicenseId, string? LicenseNumber);

// --- Create ---------------------------------------------------------------

public sealed record CreateStaffMemberCommand(
    string FullName,
    string EmployeeId,
    string Email,
    Guid StationId,
    Guid ManpowerTypeId,
    EmploymentContractInput? EmploymentContract,
    IReadOnlyList<DayOfWeek>? WorkingDays,
    IReadOnlyList<StaffLicenseInput> Licenses,
    Guid? PortalAccessRoleId) : ICommand<Guid>;

public sealed class CreateStaffMemberCommandValidator : AbstractValidator<CreateStaffMemberCommand>
{
    public CreateStaffMemberCommandValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.EmployeeId).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Email).NotEmpty();
        RuleFor(x => x.StationId).NotEmpty();
        RuleFor(x => x.ManpowerTypeId).NotEmpty();
        RuleFor(x => x.PortalAccessRoleId).NotEqual(Guid.Empty).When(x => x.PortalAccessRoleId.HasValue);
    }
}

public sealed class CreateStaffMemberCommandHandler(
    IMasterDataDbContext db,
    IMasterDataScope scope,
    IUserContext userContext,
    TimeProvider timeProvider)
    : ICommandHandler<CreateStaffMemberCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateStaffMemberCommand request, CancellationToken cancellationToken)
    {
        var scopeCheck = await scope.CheckStationAsync(request.StationId, cancellationToken);
        if (scopeCheck.IsFailure)
            return scopeCheck.Error;

        if (request.PortalAccessRoleId is { } && !userContext.HasPermission(MasterDataPermissions.StaffMembers.GrantAccess))
            return Error.Forbidden("Granting portal access requires the grant-access permission.", "MasterData.PortalAccess.Forbidden");

        var refsCheck = await StaffMemberGuards.EnsureActiveReferencesAsync(db, request.StationId, request.ManpowerTypeId, cancellationToken);
        if (refsCheck.IsFailure)
            return refsCheck.Error;

        var contract = StaffMemberGuards.BuildContract(request.EmploymentContract);
        if (contract.IsFailure)
            return contract.Error;

        var schedule = StaffMemberGuards.BuildSchedule(request.WorkingDays);
        if (schedule.IsFailure)
            return schedule.Error;

        var now = timeProvider.GetUtcNow();
        var result = StaffMember.Create(
            request.FullName, request.EmployeeId, request.Email, request.StationId, request.ManpowerTypeId,
            contract.Value, schedule.Value, now);
        if (result.IsFailure)
            return result.Error;

        var staff = result.Value;

        var employeeIdCheck = await StaffMemberGuards.EnsureEmployeeIdAvailableAsync(db, staff.EmployeeId, null, cancellationToken);
        if (employeeIdCheck.IsFailure)
            return employeeIdCheck.Error;

        var emailCheck = await StaffMemberGuards.EnsureEmailAvailableAsync(db, staff.Email, null, cancellationToken);
        if (emailCheck.IsFailure)
            return emailCheck.Error;

        var licensesCheck = await StaffMemberGuards.EnsureLicensesExistAsync(db, request.Licenses, cancellationToken);
        if (licensesCheck.IsFailure)
            return licensesCheck.Error;

        var reconcile = staff.ReconcileLicenses(StaffMemberGuards.MapLicenses(request.Licenses), now);
        if (reconcile.IsFailure)
            return reconcile.Error;

        if (request.PortalAccessRoleId is { } roleId)
        {
            var correlationId = Guid.NewGuid();
            staff.RequestPortalAccess(correlationId, now);

            db.Enqueue(new PortalAccessRequested
            {
                ExternalReferenceId = staff.Id,
                UserType = UserType.StationStaff,
                RoleId = roleId,
                Email = staff.Email,
                DisplayName = staff.FullName,
                CorrelationId = correlationId
            });
        }

        db.StaffMembers.Add(staff);
        await db.SaveChangesAsync(cancellationToken);
        return staff.Id;
    }
}

// --- Update ---------------------------------------------------------------

public sealed record UpdateStaffMemberCommand(
    Guid Id,
    string FullName,
    string EmployeeId,
    string Email,
    Guid StationId,
    Guid ManpowerTypeId,
    EmploymentContractInput? EmploymentContract,
    IReadOnlyList<DayOfWeek>? WorkingDays,
    IReadOnlyList<StaffLicenseInput> Licenses,
    byte[] RowVersion) : ICommand;

public sealed class UpdateStaffMemberCommandValidator : AbstractValidator<UpdateStaffMemberCommand>
{
    public UpdateStaffMemberCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.EmployeeId).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Email).NotEmpty();
        RuleFor(x => x.StationId).NotEmpty();
        RuleFor(x => x.ManpowerTypeId).NotEmpty();
        RuleFor(x => x.RowVersion).NotEmpty();
    }
}

public sealed class UpdateStaffMemberCommandHandler(IMasterDataDbContext db, IMasterDataScope scope, TimeProvider timeProvider)
    : ICommandHandler<UpdateStaffMemberCommand>
{
    public async Task<Result> Handle(UpdateStaffMemberCommand request, CancellationToken cancellationToken)
    {
        var resolved = await scope.ResolveAsync(cancellationToken);
        if (resolved.IsFailure)
            return resolved.Error;

        if (!resolved.Value.IsAdministrator && resolved.Value.StationId is null)
            return StaffMemberGuards.ScopeForbidden();

        var staff = await StaffMemberGuards.ApplyScope(db.StaffMembers
            .Include(s => s.Licenses)
            .AsQueryable(), resolved.Value)
            .FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken);
        if (staff is null)
            return StaffMemberGuards.NotFoundOrForbidden(resolved.Value);

        // A scoped caller may not move staff out of their station.
        var targetScopeCheck = StaffMemberGuards.EnsureStationInScope(resolved.Value, request.StationId);
        if (targetScopeCheck.IsFailure)
            return targetScopeCheck.Error;

        var refsCheck = await StaffMemberGuards.EnsureActiveReferencesAsync(db, request.StationId, request.ManpowerTypeId, cancellationToken);
        if (refsCheck.IsFailure)
            return refsCheck.Error;

        var contract = StaffMemberGuards.BuildContract(request.EmploymentContract);
        if (contract.IsFailure)
            return contract.Error;

        var schedule = StaffMemberGuards.BuildSchedule(request.WorkingDays);
        if (schedule.IsFailure)
            return schedule.Error;

        var now = timeProvider.GetUtcNow();
        var previousEmail = staff.Email;
        var result = staff.Update(
            request.FullName, request.EmployeeId, request.Email, request.StationId, request.ManpowerTypeId,
            contract.Value, schedule.Value, now);
        if (result.IsFailure)
            return result.Error;

        var employeeIdCheck = await StaffMemberGuards.EnsureEmployeeIdAvailableAsync(db, staff.EmployeeId, staff.Id, cancellationToken);
        if (employeeIdCheck.IsFailure)
            return employeeIdCheck.Error;

        var emailCheck = await StaffMemberGuards.EnsureEmailAvailableAsync(db, staff.Email, staff.Id, cancellationToken);
        if (emailCheck.IsFailure)
            return emailCheck.Error;

        // Changing the email of a linked staff member starts an Identity-owned reverification workflow;
        // the Identity login email only changes after the new address is verified.
        if (staff.LinkedUserId is { } linkedUserId && !string.Equals(previousEmail, staff.Email, StringComparison.Ordinal))
            PortalAccess.PortalLifecycle.EnqueueEmailChange(db, staff.Id, linkedUserId, staff.Email);

        var licensesCheck = await StaffMemberGuards.EnsureLicensesExistAsync(db, request.Licenses, cancellationToken);
        if (licensesCheck.IsFailure)
            return licensesCheck.Error;

        var reconcile = staff.ReconcileLicenses(StaffMemberGuards.MapLicenses(request.Licenses), now);
        if (reconcile.IsFailure)
            return reconcile.Error;

        db.SetOriginalRowVersion(staff, request.RowVersion);

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

public sealed record ActivateStaffMemberCommand(Guid Id, byte[] RowVersion) : ICommand;

public sealed record DeactivateStaffMemberCommand(Guid Id, byte[] RowVersion) : ICommand;

public sealed class ActivateStaffMemberCommandHandler(IMasterDataDbContext db, IMasterDataScope scope, TimeProvider timeProvider)
    : ICommandHandler<ActivateStaffMemberCommand>
{
    public async Task<Result> Handle(ActivateStaffMemberCommand request, CancellationToken cancellationToken)
    {
        var resolved = await scope.ResolveAsync(cancellationToken);
        if (resolved.IsFailure)
            return resolved.Error;

        if (!resolved.Value.IsAdministrator && resolved.Value.StationId is null)
            return StaffMemberGuards.ScopeForbidden();

        var staff = await StaffMemberGuards.ApplyScope(db.StaffMembers.AsQueryable(), resolved.Value)
            .FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken);
        if (staff is null)
            return StaffMemberGuards.NotFoundOrForbidden(resolved.Value);

        staff.Activate(timeProvider.GetUtcNow());
        db.SetOriginalRowVersion(staff, request.RowVersion);

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

public sealed class DeactivateStaffMemberCommandHandler(IMasterDataDbContext db, IMasterDataScope scope, TimeProvider timeProvider)
    : ICommandHandler<DeactivateStaffMemberCommand>
{
    public async Task<Result> Handle(DeactivateStaffMemberCommand request, CancellationToken cancellationToken)
    {
        var resolved = await scope.ResolveAsync(cancellationToken);
        if (resolved.IsFailure)
            return resolved.Error;

        if (!resolved.Value.IsAdministrator && resolved.Value.StationId is null)
            return StaffMemberGuards.ScopeForbidden();

        var staff = await StaffMemberGuards.ApplyScope(db.StaffMembers.AsQueryable(), resolved.Value)
            .FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken);
        if (staff is null)
            return StaffMemberGuards.NotFoundOrForbidden(resolved.Value);

        var now = timeProvider.GetUtcNow();

        // Deactivating a staff member deactivates the linked portal user and revokes its sessions.
        // Scope resolution fails immediately (record inactive); the linked account is suspended async.
        if (staff.IsActive && staff.LinkedUserId is { } linkedUserId)
        {
            staff.SuspendPortal(now);
            PortalAccess.PortalLifecycle.EnqueueDeactivation(db, staff.Id, linkedUserId);
        }

        staff.Deactivate(now);
        db.SetOriginalRowVersion(staff, request.RowVersion);

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

internal static class StaffMemberGuards
{
    public static IQueryable<StaffMember> ApplyScope(IQueryable<StaffMember> query, MasterDataScopeContext scope) =>
        scope.IsAdministrator
            ? query
            : query.Where(s => s.StationId == scope.StationId);

    public static Result EnsureStationInScope(MasterDataScopeContext scope, Guid stationId) =>
        scope.IsAdministrator || scope.StationId == stationId
            ? Result.Success()
            : ScopeForbidden();

    public static Error NotFoundOrForbidden(MasterDataScopeContext scope) =>
        scope.IsAdministrator
            ? Error.NotFound("Staff member not found.", "MasterData.StaffMember.NotFound")
            : ScopeForbidden();

    public static Error ScopeForbidden() =>
        Error.Forbidden("This record is outside your data scope.", "MasterData.Scope.Forbidden");

    public static async Task<Result> EnsureActiveReferencesAsync(
        IMasterDataDbContext db, Guid stationId, Guid manpowerTypeId, CancellationToken cancellationToken)
    {
        var station = await db.Stations.FirstOrDefaultAsync(s => s.Id == stationId, cancellationToken);
        if (station is null)
            return Error.NotFound("The selected station was not found.", "MasterData.StaffMember.StationNotFound");
        if (!station.IsActive)
            return Error.Validation("The selected station is inactive.", "MasterData.StaffMember.StationInactive");

        var manpowerType = await db.ManpowerTypes.FirstOrDefaultAsync(m => m.Id == manpowerTypeId, cancellationToken);
        if (manpowerType is null)
            return Error.NotFound("The selected manpower type was not found.", "MasterData.StaffMember.ManpowerTypeNotFound");
        if (!manpowerType.IsActive)
            return Error.Validation("The selected manpower type is inactive.", "MasterData.StaffMember.ManpowerTypeInactive");

        return Result.Success();
    }

    public static async Task<Result> EnsureEmailAvailableAsync(
        IMasterDataDbContext db, string email, Guid? excludeId, CancellationToken cancellationToken)
    {
        var taken = await db.StaffMembers.AnyAsync(s => s.Email == email && (excludeId == null || s.Id != excludeId), cancellationToken);
        if (taken)
            return Error.Conflict("A staff member with this email already exists.", "MasterData.StaffMember.DuplicateEmail");

        return Result.Success();
    }

    public static async Task<Result> EnsureEmployeeIdAvailableAsync(
        IMasterDataDbContext db, string employeeId, Guid? excludeId, CancellationToken cancellationToken)
    {
        var taken = await db.StaffMembers.AnyAsync(
            s => s.EmployeeId == employeeId && (excludeId == null || s.Id != excludeId), cancellationToken);
        if (taken)
            return Error.Conflict("A staff member with this employee ID already exists.", "MasterData.StaffMember.DuplicateEmployeeId");

        return Result.Success();
    }

    public static async Task<Result> EnsureLicensesExistAsync(
        IMasterDataDbContext db, IReadOnlyList<StaffLicenseInput>? licenses, CancellationToken cancellationToken)
    {
        if (licenses is null || licenses.Count == 0)
            return Result.Success();

        var ids = licenses.Select(l => l.LicenseId).Distinct().ToList();
        var found = await db.Licenses.Where(l => ids.Contains(l.Id)).Select(l => new { l.Id, l.IsActive }).ToListAsync(cancellationToken);

        foreach (var id in ids)
        {
            var match = found.FirstOrDefault(f => f.Id == id);
            if (match is null)
                return Error.NotFound("A referenced license was not found.", "MasterData.StaffMember.LicenseNotFound");
            if (!match.IsActive)
                return Error.Validation("A referenced license is inactive.", "MasterData.StaffMember.LicenseInactive");
        }

        return Result.Success();
    }

    public static Result<EmploymentContract?> BuildContract(EmploymentContractInput? input)
    {
        if (input is null)
            return Result.Success<EmploymentContract?>(null);

        var result = EmploymentContract.Create(input.StartDate, input.EndDate);
        if (result.IsFailure)
            return result.Error;

        return Result.Success<EmploymentContract?>(result.Value);
    }

    public static Result<WorkingSchedule?> BuildSchedule(IReadOnlyList<DayOfWeek>? days)
    {
        if (days is null || days.Count == 0)
            return Result.Success<WorkingSchedule?>(null);

        var result = WorkingSchedule.Create(days);
        if (result.IsFailure)
            return result.Error;

        return Result.Success<WorkingSchedule?>(result.Value);
    }

    public static IReadOnlyList<LicenseAssignmentItem> MapLicenses(IReadOnlyList<StaffLicenseInput>? licenses) =>
        licenses is null
            ? []
            : licenses.Select(l => new LicenseAssignmentItem(l.Id, l.LicenseId, l.LicenseNumber)).ToList();
}
