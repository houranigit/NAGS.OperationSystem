using Core.Application.Features.Customer.Commands.CreateCustomer;
using Core.Application.Features.Customer.Commands.UpdateCustomer;
using Core.Contracts.Features.Customer;

namespace Host.Web.Components.Pages.Customers.Dialog;

/// <summary>
/// UI form state for Customer Add/Update dialogs. Maps to Create/Update commands — keep validation helpers here
/// or on Radzen validators; sections bind to this model via [Parameter] Model.
/// </summary>
public sealed class CustomerFormModel
{
    public Guid? Id { get; set; }
    public byte[]? LogoBytes { get; set; }
    public string? LogoFileError { get; set; }

    public string IataCode { get; set; } = "";
    public string? IcaoCode { get; set; }
    public string Name { get; set; } = "";
    public string? OfficialEmail { get; set; }
    public string? OfficialPhone { get; set; }
    public bool IsActive { get; set; } = true;

    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? PostalCode { get; set; }
    public Guid? AddressCountryId { get; set; }

    public List<CustomerContactEditorLine> ContactLines { get; set; } = [new()];

    public bool IsLogoFileInputValid() => LogoFileError is null;

    public static bool ContactLineHasAnyField(CustomerContactEditorLine line)
    {
        var name = (line.Name ?? string.Empty).Trim();
        var em = (line.Email ?? string.Empty).Trim();
        var jt = (line.JobTitle ?? string.Empty).Trim();
        var ph = (line.Phone ?? string.Empty).Trim();
        return name.Length > 0 || em.Length > 0 || jt.Length > 0 || ph.Length > 0;
    }

    public bool IsContactNameInputValidForRow(int index)
    {
        if (index < 0 || index >= ContactLines.Count) return true;
        var line = ContactLines[index];
        var name = (line.Name ?? string.Empty).Trim();
        var em = (line.Email ?? string.Empty).Trim();
        if (!ContactLineHasAnyField(line)) return true;
        if (name.Length == 0) return false;
        if (name.Length > 0 && em.Length > 0) return name.Length <= 150;
        return true;
    }

    public bool IsContactEmailInputValidForRow(int index)
    {
        if (index < 0 || index >= ContactLines.Count) return true;
        var line = ContactLines[index];
        var name = (line.Name ?? string.Empty).Trim();
        var em = (line.Email ?? string.Empty).Trim();
        if (!ContactLineHasAnyField(line)) return true;
        if (em.Length == 0) return false;
        if (name.Length > 0 && em.Length > 0)
            return em.Contains('@') && em.Contains('.') && em.Length <= 254;
        return true;
    }

    public bool IsContactJobTitleInputValidForRow(int index)
    {
        if (index < 0 || index >= ContactLines.Count) return true;
        var line = ContactLines[index];
        var name = (line.Name ?? string.Empty).Trim();
        var em = (line.Email ?? string.Empty).Trim();
        if (name.Length > 0 && em.Length > 0)
            return (line.JobTitle ?? string.Empty).Trim().Length <= 100;
        return true;
    }

    public bool IsContactPhoneInputValidForRow(int index)
    {
        if (index < 0 || index >= ContactLines.Count) return true;
        var line = ContactLines[index];
        var name = (line.Name ?? string.Empty).Trim();
        var em = (line.Email ?? string.Empty).Trim();
        if (name.Length > 0 && em.Length > 0)
            return (line.Phone ?? string.Empty).Trim().Length <= 50;
        return true;
    }

    public bool AreContactEmailAddressesUnique()
    {
        var emails = ContactLines
            .Select(c => (c.Email ?? string.Empty).Trim().ToLowerInvariant())
            .Where(x => x.Length > 0)
            .ToList();
        return emails.Distinct().Count() == emails.Count;
    }

    public static CustomerFormModel FromDto(CustomerDto dto)
    {
        var a = dto.Address;
        var lines = dto.Contacts.Count == 0
            ? new List<CustomerContactEditorLine> { new() }
            : dto.Contacts.Select(c => new CustomerContactEditorLine
            {
                Id = c.Id,
                Name = c.Name,
                Email = c.Email,
                Phone = c.Phone,
                JobTitle = null,
                CreateUser = false,
                IsActive = c.IsActive
            }).ToList();

        return new CustomerFormModel
        {
            Id = dto.Id,
            IataCode = dto.IataCode,
            IcaoCode = dto.IcaoCode,
            Name = dto.Name,
            OfficialEmail = dto.OfficialEmail,
            OfficialPhone = dto.OfficialPhone,
            IsActive = dto.IsActive,
            AddressLine1 = a?.Line1,
            AddressLine2 = a?.Line2,
            City = a?.City,
            PostalCode = a?.PostalCode,
            AddressCountryId = a?.Country?.Id,
            ContactLines = lines,
            LogoBytes = dto.Logo is { Length: > 0 } ? [..dto.Logo] : null
        };
    }

    public CustomerFormModel Clone() =>
        new()
        {
            Id = Id,
            LogoBytes = LogoBytes is { Length: > 0 } ? [..LogoBytes] : null,
            LogoFileError = LogoFileError,
            IataCode = IataCode,
            IcaoCode = IcaoCode,
            Name = Name,
            OfficialEmail = OfficialEmail,
            OfficialPhone = OfficialPhone,
            IsActive = IsActive,
            AddressLine1 = AddressLine1,
            AddressLine2 = AddressLine2,
            City = City,
            PostalCode = PostalCode,
            AddressCountryId = AddressCountryId,
            ContactLines = ContactLines.Select(c => c.Clone()).ToList()
        };

    public CustomerAddressInput? BuildAddressInput()
    {
        var line1 = (AddressLine1 ?? "").Trim();
        var city = (City ?? "").Trim();
        if (line1.Length == 0 || city.Length == 0)
            return null;

        if (AddressCountryId is null)
            return null;

        return new CustomerAddressInput(
            line1,
            string.IsNullOrWhiteSpace(AddressLine2) ? null : AddressLine2.Trim(),
            city,
            string.IsNullOrWhiteSpace(PostalCode) ? null : PostalCode.Trim(),
            AddressCountryId.Value);
    }

    public IReadOnlyList<CustomerContactInput> BuildContactInputs() =>
        ContactLines
            .Where(ContactLineHasAnyField)
            .Select(c => new CustomerContactInput(
                c.Id,
                (c.Name ?? "").Trim(),
                (c.Email ?? "").Trim(),
                string.IsNullOrWhiteSpace(c.Phone) ? null : c.Phone.Trim(),
                c.CreateUser))
            .ToList();

    public CreateCustomerCommand ToCreateCustomerCommand() =>
        new(
            IataCode.Trim(),
            string.IsNullOrWhiteSpace(IcaoCode) ? null : IcaoCode.Trim(),
            Name.Trim(),
            string.IsNullOrWhiteSpace(OfficialEmail) ? null : OfficialEmail.Trim(),
            string.IsNullOrWhiteSpace(OfficialPhone) ? null : OfficialPhone.Trim(),
            BuildAddressInput(),
            IsActive,
            LogoBytes is { Length: > 0 } ? LogoBytes : null,
            BuildContactInputs());

    public UpdateCustomerCommand ToUpdateCustomerCommand(Guid id) =>
        new(
            id,
            IataCode.Trim(),
            string.IsNullOrWhiteSpace(IcaoCode) ? null : IcaoCode.Trim(),
            Name.Trim(),
            string.IsNullOrWhiteSpace(OfficialEmail) ? null : OfficialEmail.Trim(),
            string.IsNullOrWhiteSpace(OfficialPhone) ? null : OfficialPhone.Trim(),
            BuildAddressInput(),
            IsActive,
            LogoBytes is { Length: > 0 } ? LogoBytes : null,
            BuildContactInputs());
}

/// <summary>Editable row for airline contacts (add / wizard UI).</summary>
public sealed class CustomerContactEditorLine
{
    public Guid? Id { get; set; }

    public string Name { get; set; } = "";

    public string Email { get; set; } = "";

    public string? JobTitle { get; set; }

    public string? Phone { get; set; }

    /// <summary>When true and the row saves, Identity may provision a user for this contact.</summary>
    public bool CreateUser { get; set; }

    public bool IsActive { get; set; } = true;

    public CustomerContactEditorLine Clone() =>
        new()
        {
            Id = Id,
            Name = Name,
            Email = Email,
            JobTitle = JobTitle,
            Phone = Phone,
            CreateUser = CreateUser,
            IsActive = IsActive
        };
}
