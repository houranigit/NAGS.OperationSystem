using BuildingBlocks.Domain.Auditing;
using BuildingBlocks.Domain.Entities;
using BuildingBlocks.Domain.Results;

namespace MasterData.Domain.Customers;

/// <summary>
/// A business contact for a <see cref="Customer"/>. Exists regardless of portal access; a linked
/// portal <c>User</c> is optional and assigned later via integration events. Email is normalized and
/// unique within the owning Customer. Reconciled by stable <see cref="Entity{TId}.Id"/>.
/// </summary>
public sealed class CustomerContact : Entity<Guid>, IAuditable
{
    private CustomerContact() { }

    string IAuditable.AuditEntityType => "CustomerContact";
    Guid IAuditable.AuditEntityId => Id;
    string IAuditable.AuditRootType => "Customer";
    Guid IAuditable.AuditRootId => CustomerId;

    public Guid CustomerId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? JobTitle { get; private set; }
    public string Email { get; private set; } = null!;
    public string? Phone { get; private set; }

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

    internal static Result<CustomerContact> Create(
        Guid customerId,
        string? name,
        string? jobTitle,
        string? email,
        string? phone,
        DateTimeOffset now,
        Guid? id = null)
    {
        var nameCheck = ValidateName(name);
        if (nameCheck.IsFailure)
            return nameCheck.Error;

        var emailCheck = NormalizeEmail(email);
        if (emailCheck.IsFailure)
            return emailCheck.Error;

        var titleCheck = ValidateJobTitle(jobTitle);
        if (titleCheck.IsFailure)
            return titleCheck.Error;

        var phoneCheck = ValidatePhone(phone);
        if (phoneCheck.IsFailure)
            return phoneCheck.Error;

        return new CustomerContact
        {
            Id = id ?? Guid.NewGuid(),
            CustomerId = customerId,
            Name = nameCheck.Value,
            JobTitle = titleCheck.Value,
            Email = emailCheck.Value,
            Phone = phoneCheck.Value,
            IsActive = true,
            CreatedAtUtc = now
        };
    }

    internal Result Update(string? name, string? jobTitle, string? email, string? phone, DateTimeOffset now)
    {
        var nameCheck = ValidateName(name);
        if (nameCheck.IsFailure)
            return nameCheck.Error;

        var emailCheck = NormalizeEmail(email);
        if (emailCheck.IsFailure)
            return emailCheck.Error;

        var titleCheck = ValidateJobTitle(jobTitle);
        if (titleCheck.IsFailure)
            return titleCheck.Error;

        var phoneCheck = ValidatePhone(phone);
        if (phoneCheck.IsFailure)
            return phoneCheck.Error;

        Name = nameCheck.Value;
        JobTitle = titleCheck.Value;
        Email = emailCheck.Value;
        Phone = phoneCheck.Value;
        UpdatedAtUtc = now;
        return Result.Success();
    }

    internal void Deactivate(DateTimeOffset now)
    {
        if (!IsActive)
            return;

        IsActive = false;
        UpdatedAtUtc = now;
    }

    /// <summary>Begins a portal-access request, recording the correlation id of this attempt.</summary>
    public void RequestPortalAccess(Guid correlationId, DateTimeOffset now)
    {
        PortalCorrelationId = correlationId;
        PortalState = PortalAccess.PortalAccessState.Provisioning;
        PortalFailureReason = null;
        UpdatedAtUtc = now;
    }

    /// <summary>Links the provisioned portal user, ignoring stale replies from a superseded request.</summary>
    public void LinkUser(Guid userId, Guid? correlationId, DateTimeOffset now)
    {
        if (correlationId is { } id && PortalCorrelationId is { } current && id != current)
            return;

        LinkedUserId = userId;
        PortalState = PortalAccess.PortalAccessState.Invited;
        PortalFailureReason = null;
        UpdatedAtUtc = now;
    }

    /// <summary>Marks the linked portal account active after the invited user completes activation.</summary>
    public void MarkPortalActive(Guid userId, DateTimeOffset now)
    {
        if (!IsActive || LinkedUserId != userId)
            return;

        PortalState = PortalAccess.PortalAccessState.Active;
        PortalFailureReason = null;
        UpdatedAtUtc = now;
    }

    /// <summary>Marks a restored linked portal account invited again when it has not activated yet.</summary>
    public void MarkPortalInvited(Guid userId, DateTimeOffset now)
    {
        if (!IsActive || LinkedUserId != userId)
            return;

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

    private static Result<string> ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Error.Validation("Contact name is required.", "MasterData.CustomerContact.NameRequired");

        var trimmed = name.Trim();
        if (trimmed.Length > 150)
            return Error.Validation("Contact name must be at most 150 characters.", "MasterData.CustomerContact.NameTooLong");

        return trimmed;
    }

    private static Result<string?> ValidateJobTitle(string? jobTitle)
    {
        if (string.IsNullOrWhiteSpace(jobTitle))
            return Result.Success<string?>(null);

        var trimmed = jobTitle.Trim();
        if (trimmed.Length > 100)
            return Error.Validation("Job title must be at most 100 characters.", "MasterData.CustomerContact.JobTitleTooLong");

        return Result.Success<string?>(trimmed);
    }

    private static Result<string> NormalizeEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return Error.Validation("Contact email is required.", "MasterData.CustomerContact.EmailRequired");

        var normalized = email.Trim().ToLowerInvariant();
        if (normalized.Length > 256 || !EmailValidation.IsValid(normalized))
            return Error.Validation("Contact email is invalid.", "MasterData.CustomerContact.EmailInvalid");

        return normalized;
    }

    private static Result<string?> ValidatePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return Result.Success<string?>(null);

        var trimmed = phone.Trim();
        if (trimmed.Length > 30)
            return Error.Validation("Phone must be at most 30 characters.", "MasterData.CustomerContact.PhoneTooLong");

        return Result.Success<string?>(trimmed);
    }
}
