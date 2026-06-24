using BuildingBlocks.Domain.Aggregates;
using BuildingBlocks.Domain.Auditing;
using BuildingBlocks.Domain.Results;
using MasterData.Domain.Customers;

namespace MasterData.Domain.StaffMembers;

/// <summary>
/// An operational staff member assigned to a <see cref="Stations.Station"/> with a
/// <see cref="ManpowerTypes.ManpowerType"/>. Owns an optional employment contract, an optional working
/// schedule, and a reconciled-by-id collection of license assignments. Email is unique across staff.
/// A linked portal <c>User</c> is optional and assigned later via the portal-access workflow.
/// </summary>
public sealed class StaffMember : AggregateRoot<Guid>, IAuditable
{
    private readonly List<StaffMemberLicense> _licenses = [];

    private StaffMember() { }

    string IAuditable.AuditEntityType => "StaffMember";
    Guid IAuditable.AuditEntityId => Id;

    public string FullName { get; private set; } = null!;
    public string Email { get; private set; } = null!;
    public Guid StationId { get; private set; }
    public Guid ManpowerTypeId { get; private set; }

    public DateOnly? EmploymentStartDate { get; private set; }
    public DateOnly? EmploymentEndDate { get; private set; }
    public int? WorkingScheduleMask { get; private set; }

    /// <summary>The provisioned portal user, set later via the portal-access workflow. Null until linked.</summary>
    public Guid? LinkedUserId { get; private set; }

    /// <summary>Portal-access lifecycle state reflected from the Identity provisioning workflow.</summary>
    public PortalAccess.PortalAccessState PortalState { get; private set; } = PortalAccess.PortalAccessState.None;

    /// <summary>Correlates the latest provisioning request so stale replies cannot overwrite a newer one.</summary>
    public Guid? PortalCorrelationId { get; private set; }

    /// <summary>Safe, non-sensitive failure detail when <see cref="PortalState"/> is Failed.</summary>
    public string? PortalFailureReason { get; private set; }

    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    /// <summary>Optimistic-concurrency token surfaced to clients as an ETag.</summary>
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyList<StaffMemberLicense> Licenses => _licenses.AsReadOnly();

    public EmploymentContract? EmploymentContract =>
        EmploymentStartDate is { } start ? StaffMembers.EmploymentContract.Create(start, EmploymentEndDate).Value : null;

    public WorkingSchedule? WorkingSchedule =>
        WorkingScheduleMask is { } mask ? StaffMembers.WorkingSchedule.FromMask(mask) : null;

    public static Result<StaffMember> Create(
        string? fullName,
        string? email,
        Guid stationId,
        Guid manpowerTypeId,
        EmploymentContract? employmentContract,
        WorkingSchedule? workingSchedule,
        DateTimeOffset now,
        Guid? id = null)
    {
        var nameCheck = ValidateName(fullName);
        if (nameCheck.IsFailure)
            return nameCheck.Error;

        var emailCheck = NormalizeEmail(email);
        if (emailCheck.IsFailure)
            return emailCheck.Error;

        if (stationId == Guid.Empty)
            return Error.Validation("A station is required.", "MasterData.StaffMember.StationRequired");

        if (manpowerTypeId == Guid.Empty)
            return Error.Validation("A manpower type is required.", "MasterData.StaffMember.ManpowerTypeRequired");

        return new StaffMember
        {
            Id = id ?? Guid.NewGuid(),
            FullName = nameCheck.Value,
            Email = emailCheck.Value,
            StationId = stationId,
            ManpowerTypeId = manpowerTypeId,
            EmploymentStartDate = employmentContract?.StartDate,
            EmploymentEndDate = employmentContract?.EndDate,
            WorkingScheduleMask = workingSchedule?.ToMask(),
            IsActive = true,
            CreatedAtUtc = now
        };
    }

    public Result Update(
        string? fullName,
        string? email,
        Guid stationId,
        Guid manpowerTypeId,
        EmploymentContract? employmentContract,
        WorkingSchedule? workingSchedule,
        DateTimeOffset now)
    {
        var nameCheck = ValidateName(fullName);
        if (nameCheck.IsFailure)
            return nameCheck.Error;

        var emailCheck = NormalizeEmail(email);
        if (emailCheck.IsFailure)
            return emailCheck.Error;

        if (stationId == Guid.Empty)
            return Error.Validation("A station is required.", "MasterData.StaffMember.StationRequired");

        if (manpowerTypeId == Guid.Empty)
            return Error.Validation("A manpower type is required.", "MasterData.StaffMember.ManpowerTypeRequired");

        FullName = nameCheck.Value;
        Email = emailCheck.Value;
        StationId = stationId;
        ManpowerTypeId = manpowerTypeId;
        EmploymentStartDate = employmentContract?.StartDate;
        EmploymentEndDate = employmentContract?.EndDate;
        WorkingScheduleMask = workingSchedule?.ToMask();
        UpdatedAtUtc = now;
        return Result.Success();
    }

    /// <summary>
    /// Reconciles license assignments by stable id: existing assignments are updated, missing ones are
    /// removed, and id-less ones are created. A staff member cannot hold the same License type twice.
    /// </summary>
    public Result ReconcileLicenses(IReadOnlyCollection<LicenseAssignmentItem> incoming, DateTimeOffset now)
    {
        var duplicateType = incoming
            .GroupBy(x => x.LicenseId)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicateType is not null)
            return Error.Conflict("A staff member cannot hold the same license type more than once.", "MasterData.StaffMemberLicense.DuplicateLicense");

        var keepIds = new HashSet<Guid>();
        foreach (var item in incoming)
        {
            if (item.Id is { } existingId && existingId != Guid.Empty)
            {
                var existing = _licenses.FirstOrDefault(l => l.Id == existingId);
                if (existing is null)
                    return Error.NotFound("A referenced license assignment does not exist.", "MasterData.StaffMemberLicense.NotFound");

                var updateResult = existing.Update(item.LicenseId, item.LicenseNumber);
                if (updateResult.IsFailure)
                    return updateResult.Error;

                keepIds.Add(existingId);
            }
            else
            {
                var createResult = StaffMemberLicense.Create(Id, item.LicenseId, item.LicenseNumber);
                if (createResult.IsFailure)
                    return createResult.Error;

                _licenses.Add(createResult.Value);
                keepIds.Add(createResult.Value.Id);
            }
        }

        foreach (var orphan in _licenses.Where(l => !keepIds.Contains(l.Id)).ToList())
            _licenses.Remove(orphan);

        UpdatedAtUtc = now;
        return Result.Success();
    }

    public Result Activate(DateTimeOffset now)
    {
        if (IsActive)
            return Result.Success();

        IsActive = true;
        UpdatedAtUtc = now;
        return Result.Success();
    }

    public Result Deactivate(DateTimeOffset now)
    {
        if (!IsActive)
            return Result.Success();

        IsActive = false;
        UpdatedAtUtc = now;
        return Result.Success();
    }

    /// <summary>Begins a portal-access request, recording the correlation id of this attempt.</summary>
    public void RequestPortalAccess(Guid correlationId, DateTimeOffset now)
    {
        PortalCorrelationId = correlationId;
        PortalState = PortalAccess.PortalAccessState.Provisioning;
        PortalFailureReason = null;
        UpdatedAtUtc = now;
    }

    /// <summary>
    /// Links the provisioned portal user when consuming the Identity reply. Ignores stale replies
    /// whose correlation id does not match the latest request.
    /// </summary>
    public void LinkUser(Guid userId, Guid? correlationId, DateTimeOffset now)
    {
        if (correlationId is { } id && PortalCorrelationId is { } current && id != current)
            return;

        LinkedUserId = userId;
        PortalState = PortalAccess.PortalAccessState.Invited;
        PortalFailureReason = null;
        UpdatedAtUtc = now;
    }

    /// <summary>Records a provisioning failure (visible, retryable) for the matching request.</summary>
    public void MarkPortalFailed(Guid? correlationId, string reason, DateTimeOffset now)
    {
        if (correlationId is { } id && PortalCorrelationId is { } current && id != current)
            return;

        PortalState = PortalAccess.PortalAccessState.Failed;
        PortalFailureReason = reason;
        UpdatedAtUtc = now;
    }

    /// <summary>Marks portal access suspended (record or parent deactivated). Keeps the User link.</summary>
    public void SuspendPortal(DateTimeOffset now)
    {
        if (PortalState is PortalAccess.PortalAccessState.None)
            return;

        PortalState = PortalAccess.PortalAccessState.Suspended;
        UpdatedAtUtc = now;
    }

    /// <summary>Clears the linked portal user (e.g. after a permanent removal that releases the login email).</summary>
    public void UnlinkUser(DateTimeOffset now)
    {
        LinkedUserId = null;
        PortalState = PortalAccess.PortalAccessState.None;
        PortalCorrelationId = null;
        PortalFailureReason = null;
        UpdatedAtUtc = now;
    }

    private static Result<string> ValidateName(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            return Error.Validation("Full name is required.", "MasterData.StaffMember.NameRequired");

        var trimmed = fullName.Trim();
        if (trimmed.Length > 200)
            return Error.Validation("Full name must be at most 200 characters.", "MasterData.StaffMember.NameTooLong");

        return trimmed;
    }

    private static Result<string> NormalizeEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return Error.Validation("Email is required.", "MasterData.StaffMember.EmailRequired");

        var normalized = email.Trim().ToLowerInvariant();
        if (normalized.Length > 256 || !EmailValidation.IsValid(normalized))
            return Error.Validation("Email is invalid.", "MasterData.StaffMember.EmailInvalid");

        return normalized;
    }
}

/// <summary>An incoming license assignment for reconciliation. A null/empty <see cref="Id"/> creates a new assignment.</summary>
public sealed record LicenseAssignmentItem(Guid? Id, Guid LicenseId, string? LicenseNumber);
