using BuildingBlocks.Domain.Entities;
using BuildingBlocks.Domain.Results;

namespace MasterData.Domain.Customers;

/// <summary>
/// A business contact for a <see cref="Customer"/>. Exists regardless of portal access; a linked
/// portal <c>User</c> is optional and assigned later via integration events. Email is normalized and
/// unique within the owning Customer. Reconciled by stable <see cref="Entity{TId}.Id"/>.
/// </summary>
public sealed class CustomerContact : Entity<Guid>
{
    private CustomerContact() { }

    public Guid CustomerId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? JobTitle { get; private set; }
    public string Email { get; private set; } = null!;
    public string? Phone { get; private set; }

    /// <summary>The provisioned portal user, set later via the portal-access workflow. Null until linked.</summary>
    public Guid? LinkedUserId { get; private set; }
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

    /// <summary>Links the provisioned portal user. Called when consuming the Identity provisioning reply.</summary>
    public void LinkUser(Guid userId, DateTimeOffset now)
    {
        LinkedUserId = userId;
        UpdatedAtUtc = now;
    }

    /// <summary>Clears the linked portal user (e.g. after a permanent removal that releases the login email).</summary>
    public void UnlinkUser(DateTimeOffset now)
    {
        LinkedUserId = null;
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
