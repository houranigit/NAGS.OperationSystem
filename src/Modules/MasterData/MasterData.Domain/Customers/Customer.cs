using BuildingBlocks.Domain.Aggregates;
using BuildingBlocks.Domain.Results;

namespace MasterData.Domain.Customers;

/// <summary>
/// An airline customer the operation serves. Identified by a globally-unique 2-character IATA airline
/// code with an optional globally-unique 3-letter ICAO airline code. References a required active
/// <see cref="Countries.Country"/>. Owns its <see cref="CustomerContact"/> collection, reconciled by
/// stable contact id. Long-lived master data with an active/inactive lifecycle; never hard-deleted.
/// </summary>
public sealed class Customer : AggregateRoot<Guid>
{
    private readonly List<CustomerContact> _contacts = [];

    private Customer() { }

    public string IataCode { get; private set; } = null!;
    public string? IcaoCode { get; private set; }
    public string Name { get; private set; } = null!;
    public Guid CountryId { get; private set; }
    public string? OfficialEmail { get; private set; }
    public string? OfficialPhone { get; private set; }
    public string? LogoFileReference { get; private set; }
    public Address Address { get; private set; } = null!;
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    /// <summary>Optimistic-concurrency token surfaced to clients as an ETag.</summary>
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyList<CustomerContact> Contacts => _contacts.AsReadOnly();

    public static Result<Customer> Create(
        string? iataCode,
        string? icaoCode,
        string? name,
        Guid countryId,
        string? officialEmail,
        string? officialPhone,
        string? logoFileReference,
        Address address,
        DateTimeOffset now,
        Guid? id = null)
    {
        var iataCheck = NormalizeIata(iataCode);
        if (iataCheck.IsFailure)
            return iataCheck.Error;

        var icaoCheck = NormalizeIcao(icaoCode);
        if (icaoCheck.IsFailure)
            return icaoCheck.Error;

        var nameCheck = ValidateName(name);
        if (nameCheck.IsFailure)
            return nameCheck.Error;

        if (countryId == Guid.Empty)
            return Error.Validation("A country is required.", "MasterData.Customer.CountryRequired");

        var emailCheck = NormalizeOfficialEmail(officialEmail);
        if (emailCheck.IsFailure)
            return emailCheck.Error;

        var phoneCheck = ValidateOfficialPhone(officialPhone);
        if (phoneCheck.IsFailure)
            return phoneCheck.Error;

        if (address is null)
            return Error.Validation("An official address is required.", "MasterData.Customer.AddressRequired");

        return new Customer
        {
            Id = id ?? Guid.NewGuid(),
            IataCode = iataCheck.Value,
            IcaoCode = icaoCheck.Value,
            Name = nameCheck.Value,
            CountryId = countryId,
            OfficialEmail = emailCheck.Value,
            OfficialPhone = phoneCheck.Value,
            LogoFileReference = string.IsNullOrWhiteSpace(logoFileReference) ? null : logoFileReference.Trim(),
            Address = address,
            IsActive = true,
            CreatedAtUtc = now
        };
    }

    public Result Update(
        string? iataCode,
        string? icaoCode,
        string? name,
        Guid countryId,
        string? officialEmail,
        string? officialPhone,
        Address address,
        DateTimeOffset now)
    {
        var iataCheck = NormalizeIata(iataCode);
        if (iataCheck.IsFailure)
            return iataCheck.Error;

        var icaoCheck = NormalizeIcao(icaoCode);
        if (icaoCheck.IsFailure)
            return icaoCheck.Error;

        var nameCheck = ValidateName(name);
        if (nameCheck.IsFailure)
            return nameCheck.Error;

        if (countryId == Guid.Empty)
            return Error.Validation("A country is required.", "MasterData.Customer.CountryRequired");

        var emailCheck = NormalizeOfficialEmail(officialEmail);
        if (emailCheck.IsFailure)
            return emailCheck.Error;

        var phoneCheck = ValidateOfficialPhone(officialPhone);
        if (phoneCheck.IsFailure)
            return phoneCheck.Error;

        if (address is null)
            return Error.Validation("An official address is required.", "MasterData.Customer.AddressRequired");

        IataCode = iataCheck.Value;
        IcaoCode = icaoCheck.Value;
        Name = nameCheck.Value;
        CountryId = countryId;
        OfficialEmail = emailCheck.Value;
        OfficialPhone = phoneCheck.Value;
        Address = address;
        UpdatedAtUtc = now;
        return Result.Success();
    }

    public Result SetLogo(string? logoFileReference, DateTimeOffset now)
    {
        LogoFileReference = string.IsNullOrWhiteSpace(logoFileReference) ? null : logoFileReference.Trim();
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

    public Result<CustomerContact> AddContact(string? name, string? jobTitle, string? email, string? phone, DateTimeOffset now)
    {
        var contactResult = CustomerContact.Create(Id, name, jobTitle, email, phone, now);
        if (contactResult.IsFailure)
            return contactResult.Error;

        var contact = contactResult.Value;
        if (_contacts.Any(c => c.IsActive && string.Equals(c.Email, contact.Email, StringComparison.OrdinalIgnoreCase)))
            return Error.Conflict("A contact with this email already exists for the customer.", "MasterData.CustomerContact.EmailNotUnique");

        _contacts.Add(contact);
        UpdatedAtUtc = now;
        return contact;
    }

    /// <summary>
    /// Removes a contact from the customer (soft delete). Returns the removed contact so the caller can
    /// propagate deactivation to a linked portal user. When <paramref name="releaseLink"/> is set (a
    /// permanent removal that releases the login email), the contact is also detached from its user.
    /// </summary>
    public Result<CustomerContact> RemoveContact(Guid contactId, bool releaseLink, DateTimeOffset now)
    {
        var contact = _contacts.FirstOrDefault(c => c.Id == contactId);
        if (contact is null)
            return Error.NotFound("A referenced contact does not exist for the customer.", "MasterData.CustomerContact.NotFound");

        if (!contact.IsActive)
            return Error.Conflict("The contact has already been removed.", "MasterData.CustomerContact.AlreadyRemoved");

        contact.Deactivate(now);
        if (releaseLink)
            contact.UnlinkUser(now);

        UpdatedAtUtc = now;
        return contact;
    }

    /// <summary>
    /// Reconciles the contact collection by stable id: existing contacts are updated, contacts missing
    /// from the incoming set are deactivated, and contacts without an id are created. Active emails must
    /// be unique within the customer.
    /// </summary>
    public Result ReconcileContacts(IReadOnlyCollection<ContactReconciliationItem> incoming, DateTimeOffset now)
    {
        var normalized = new List<(ContactReconciliationItem Item, string Email)>();
        foreach (var item in incoming)
        {
            if (string.IsNullOrWhiteSpace(item.Email))
                return Error.Validation("Contact email is required.", "MasterData.CustomerContact.EmailRequired");

            normalized.Add((item, item.Email.Trim().ToLowerInvariant()));
        }

        var duplicateEmail = normalized
            .GroupBy(x => x.Email, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicateEmail is not null)
            return Error.Conflict("Contact email addresses must be unique within the customer.", "MasterData.CustomerContact.EmailNotUnique");

        var keepIds = new HashSet<Guid>();
        foreach (var (item, _) in normalized)
        {
            if (item.Id is { } existingId && existingId != Guid.Empty)
            {
                var existing = _contacts.FirstOrDefault(c => c.Id == existingId);
                if (existing is null)
                    return Error.NotFound("A referenced contact does not exist for the customer.", "MasterData.CustomerContact.NotFound");

                var updateResult = existing.Update(item.Name, item.JobTitle, item.Email, item.Phone, now);
                if (updateResult.IsFailure)
                    return updateResult.Error;

                keepIds.Add(existingId);
            }
            else
            {
                var createResult = CustomerContact.Create(Id, item.Name, item.JobTitle, item.Email, item.Phone, now);
                if (createResult.IsFailure)
                    return createResult.Error;

                _contacts.Add(createResult.Value);
                keepIds.Add(createResult.Value.Id);
            }
        }

        foreach (var orphan in _contacts.Where(c => c.IsActive && !keepIds.Contains(c.Id)).ToList())
            orphan.Deactivate(now);

        UpdatedAtUtc = now;
        return Result.Success();
    }

    private static Result<string> NormalizeIata(string? iataCode)
    {
        if (string.IsNullOrWhiteSpace(iataCode))
            return Error.Validation("IATA code is required.", "MasterData.Customer.IataRequired");

        var normalized = iataCode.Trim().ToUpperInvariant();
        if (normalized.Length != 2 || !normalized.All(char.IsAsciiLetterOrDigit))
            return Error.Validation("IATA airline code must be exactly two letters or digits.", "MasterData.Customer.IataInvalid");

        return normalized;
    }

    private static Result<string?> NormalizeIcao(string? icaoCode)
    {
        if (string.IsNullOrWhiteSpace(icaoCode))
            return Result.Success<string?>(null);

        var normalized = icaoCode.Trim().ToUpperInvariant();
        if (normalized.Length != 3 || !normalized.All(char.IsAsciiLetter))
            return Error.Validation("ICAO airline code must be exactly three letters.", "MasterData.Customer.IcaoInvalid");

        return Result.Success<string?>(normalized);
    }

    private static Result<string> ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Error.Validation("Customer name is required.", "MasterData.Customer.NameRequired");

        var trimmed = name.Trim();
        if (trimmed.Length > 200)
            return Error.Validation("Customer name must be at most 200 characters.", "MasterData.Customer.NameTooLong");

        return trimmed;
    }

    private static Result<string?> NormalizeOfficialEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return Result.Success<string?>(null);

        var normalized = email.Trim().ToLowerInvariant();
        if (normalized.Length > 256 || !EmailValidation.IsValid(normalized))
            return Error.Validation("Official email is invalid.", "MasterData.Customer.OfficialEmailInvalid");

        return Result.Success<string?>(normalized);
    }

    private static Result<string?> ValidateOfficialPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return Result.Success<string?>(null);

        var trimmed = phone.Trim();
        if (trimmed.Length > 30)
            return Error.Validation("Official phone must be at most 30 characters.", "MasterData.Customer.OfficialPhoneTooLong");

        return Result.Success<string?>(trimmed);
    }
}

/// <summary>An incoming contact specification for reconciliation. A null/empty <see cref="Id"/> creates a new contact.</summary>
public sealed record ContactReconciliationItem(Guid? Id, string? Name, string? JobTitle, string? Email, string? Phone);
