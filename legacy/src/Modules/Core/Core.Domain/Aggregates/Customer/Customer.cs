using BuildingBlocks.Domain.Aggregates;
using BuildingBlocks.Domain.Results;
using Core.Domain.Events;
using Core.Domain.ValueObjects;

namespace Core.Domain.Aggregates.Customer;

/// <summary>
/// Customer aggregate. Child contacts use the <see cref="SyncContacts"/> snapshot pattern — see remarks there.
/// </summary>
public sealed class Customer : AggregateRoot<CustomerId>
{
    private readonly List<CustomerContact> _contacts = [];

    public IataAirlineCode IataCode { get; private set; } = null!;
    public string? IcaoCode { get; private set; }
    public string Name { get; private set; } = null!;
    public string? OfficialEmail { get; private set; }
    public string? OfficialPhone { get; private set; }
    public byte[]? Logo { get; private set; }
    public Address? Address { get; private set; }
    public IReadOnlyList<CustomerContact> Contacts => _contacts.AsReadOnly();
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private Customer() { }

    public static Result<Customer> Create(
        IataAirlineCode iataCode,
        string name,
        string? icaoCode = null,
        string? officialEmail = null,
        string? officialPhone = null,
        byte[]? logo = null,
        Address? address = null)
    {
        var nameError = ValidateName(name);
        if (nameError is not null) return nameError;

        if (icaoCode is not null)
        {
            var icaoError = ValidateIcaoCode(icaoCode);
            if (icaoError is not null) return icaoError;
        }

        if (officialEmail is not null)
        {
            var emailError = ValidateEmail(officialEmail);
            if (emailError is not null) return emailError;
        }

        if (officialPhone is not null && officialPhone.Length > 50)
            return Error.Validation("Official phone must not exceed 50 characters.");

        var customer = new Customer
        {
            Id = CustomerId.New(),
            IataCode = iataCode,
            IcaoCode = icaoCode?.Trim().ToUpperInvariant(),
            Name = name.Trim(),
            OfficialEmail = officialEmail?.Trim().ToLowerInvariant(),
            OfficialPhone = officialPhone?.Trim(),
            Logo = logo,
            Address = address,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        customer.RaiseDomainEvent(new CustomerCreatedEvent(customer.Id));
        return customer;
    }

    public Result Activate()
    {
        if (IsActive)
            return Error.Conflict("Customer is already active.");

        IsActive = true;
        RaiseDomainEvent(new CustomerActivatedEvent(Id));
        return Result.Success();
    }

    public Result Deactivate()
    {
        if (!IsActive)
            return Error.Conflict("Customer is already inactive.");

        IsActive = false;
        var linkedUserIds = _contacts
            .Where(c => c.LinkedUserId.HasValue)
            .Select(c => c.LinkedUserId!.Value)
            .ToList();
        RaiseDomainEvent(new CustomerDeactivatedEvent(Id, linkedUserIds));
        return Result.Success();
    }

    public Result UpdateDetails(
        string name,
        string? icaoCode,
        string? officialEmail,
        string? officialPhone)
    {
        var nameError = ValidateName(name);
        if (nameError is not null) return nameError;

        if (icaoCode is not null)
        {
            var icaoError = ValidateIcaoCode(icaoCode);
            if (icaoError is not null) return icaoError;
        }

        if (officialEmail is not null)
        {
            var emailError = ValidateEmail(officialEmail);
            if (emailError is not null) return emailError;
        }

        if (officialPhone is not null && officialPhone.Length > 50)
            return Error.Validation("Official phone must not exceed 50 characters.");

        Name = name.Trim();
        IcaoCode = icaoCode?.Trim().ToUpperInvariant();
        OfficialEmail = officialEmail?.Trim().ToLowerInvariant();
        OfficialPhone = officialPhone?.Trim();
        UpdatedAt = DateTime.UtcNow;

        return Result.Success();
    }

    public Result UpdateLogo(byte[]? logo)
    {
        Logo = logo;
        return Result.Success();
    }

    public Result UpdateAddress(Address? address)
    {
        Address = address;
        return Result.Success();
    }

    // --- Contacts (child collection) ---
    // Prefer SyncContacts from handlers when UI/API sends the full list: aggregate reconciles adds/removes/updates and emits granular domain events via AddContact / RemoveContact / UpdateContact semantics internally where applicable.

    public Result<CustomerContact> AddContact(
        string name,
        string? jobTitle,
        string email,
        string? phone,
        bool createUser = false)
    {
        var result = CustomerContact.Create(Id, name, jobTitle, email, phone);
        if (result.IsFailure) return result.Error;

        var normalizedEmail = email.Trim().ToLowerInvariant();
        if (_contacts.Any(c => c.Email == normalizedEmail))
            return Error.Conflict($"A contact with email '{normalizedEmail}' already exists.");

        var contact = result.Value;
        _contacts.Add(contact);

        RaiseDomainEvent(new CustomerContactAddedEvent(
            Id,
            contact.Id,
            contact.Name,
            contact.Email,
            createUser));

        return contact;
    }

    public Result RemoveContact(CustomerContactId contactId)
    {
        var contact = _contacts.FirstOrDefault(c => c.Id == contactId);
        if (contact is null)
            return Error.NotFound("Contact not found.");

        _contacts.Remove(contact);
        RaiseDomainEvent(new CustomerContactRemovedEvent(Id, contactId, contact.LinkedUserId));
        return Result.Success();
    }

    public Result UpdateContact(
        CustomerContactId contactId,
        string name,
        string? jobTitle,
        string email,
        string? phone)
    {
        var contact = _contacts.FirstOrDefault(c => c.Id == contactId);
        if (contact is null)
            return Error.NotFound("Contact not found.");

        var normalizedEmail = email.Trim().ToLowerInvariant();
        if (_contacts.Any(c => c.Id != contactId && c.Email == normalizedEmail))
            return Error.Conflict($"A contact with email '{normalizedEmail}' already exists.");

        return contact.Update(name, jobTitle, email, phone);
    }

    /// <summary>
    /// Reconciles the contact collection against an authoritative incoming snapshot from the application layer.
    /// </summary>
    /// <remarks>
    /// <para><b>Preferred pattern for aggregate children:</b> commands send <i>the full desired child list</i> each save (Contracts primitives/DTOs). The aggregate compares identity (<paramref name="incoming"/> rows with non-null contact ids vs existing <see cref="CustomerContact"/> ids):</para>
    /// <list type="number">
    /// <item><description><b>Update</b> — rows whose id matches an existing contact (same logical entity).</description></item>
    /// <item><description><b>Remove</b> — existing contacts whose ids do not appear in the incoming list.</description></item>
    /// <item><description><b>Add</b> — rows with null id (client‑assigned sentinel for “new” rows).</description></item>
    /// </list>
    /// <para>This avoids orphan rows, replaces ambiguous deltas (“patch-only payloads”), and keeps invariant enforcement (email uniqueness, VO validation) in one place. New aggregates with collections should mirror this approach — package commands as full snapshots and implement an aggregate method modeled after this handler pair (see <c>Core.Application.Features.Customer.Commands</c>).</para>
    /// </remarks>
    public Result SyncContacts(
        IReadOnlyList<(Guid? ContactId, string Name, string? JobTitle, string Email, string? Phone, bool CreateUserOnAdd)> incoming)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        var rows = incoming.ToList();
        var normalized = rows.Select(r => r.Email.Trim().ToLowerInvariant()).ToList();
        if (normalized.GroupBy(e => e).Any(g => g.Count() > 1))
            return Error.Validation("Duplicate contact email in the contact list.");

        // Phase order matters for stable edits + domain events: update persisted rows first, drop removals, then add new ids last (raises adds last).

        foreach (var row in rows.Where(x => x.ContactId is not null))
        {
            var cid = CustomerContactId.From(row.ContactId!.Value);
            var contact = _contacts.FirstOrDefault(c => c.Id == cid);
            if (contact is null)
                return Error.NotFound("Contact not found.");

            var update = contact.Update(row.Name, row.JobTitle, row.Email, row.Phone);
            if (update.IsFailure) return update.Error;
        }

        foreach (var contact in _contacts.ToList())
        {
            var keep = rows.Any(r => r.ContactId.HasValue && r.ContactId.Value == contact.Id.Value);
            if (!keep)
            {
                var rem = RemoveContact(contact.Id);
                if (rem.IsFailure) return rem.Error;
            }
        }

        foreach (var row in rows.Where(x => x.ContactId is null))
        {
            var add = AddContact(
                row.Name,
                row.JobTitle,
                row.Email,
                row.Phone,
                row.CreateUserOnAdd);
            if (add.IsFailure) return add.Error;
        }

        return Result.Success();
    }

    public Result LinkContactToUser(CustomerContactId contactId, Guid linkedUserId)
    {
        var contact = _contacts.FirstOrDefault(c => c.Id == contactId);
        if (contact is null)
            return Error.NotFound("Contact not found.");

        if (contact.LinkedUserId.HasValue)
            return Error.Conflict("Contact is already linked to a user.");

        contact.LinkUser(linkedUserId);
        RaiseDomainEvent(new CustomerContactLinkedToUserEvent(Id, contactId, linkedUserId));
        return Result.Success();
    }

    private static Error? ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Error.Validation("Customer name is required.");
        if (name.Length > 200)
            return Error.Validation("Customer name must not exceed 200 characters.");
        return null;
    }

    private static Error? ValidateIcaoCode(string icaoCode)
    {
        var code = icaoCode.Trim().ToUpperInvariant();
        if (code.Length != 3 || !code.All(char.IsLetter))
            return Error.Validation("ICAO airline code must be exactly 3 uppercase letters.");
        return null;
    }

    private static Error? ValidateEmail(string email)
    {
        if (!email.Contains('@') || !email.Contains('.'))
            return Error.Validation("Official email format is invalid.");
        if (email.Length > 254)
            return Error.Validation("Official email must not exceed 254 characters.");
        return null;
    }
}
