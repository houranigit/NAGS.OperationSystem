using BuildingBlocks.Domain.Entities;
using BuildingBlocks.Domain.Results;

namespace Core.Domain.Aggregates.Customer;

public sealed class CustomerContact : Entity<CustomerContactId>
{
    public CustomerId CustomerId { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string? JobTitle { get; private set; }
    public string Email { get; private set; } = null!;
    public string? Phone { get; private set; }
    public Guid? LinkedUserId { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private CustomerContact() { }

    internal static Result<CustomerContact> Create(
        CustomerId customerId,
        string name,
        string? jobTitle,
        string email,
        string? phone)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Error.Validation("Contact name is required.");

        if (name.Length > 150)
            return Error.Validation("Contact name must not exceed 150 characters.");

        if (string.IsNullOrWhiteSpace(email))
            return Error.Validation("Contact email is required.");

        if (!email.Contains('@') || !email.Contains('.'))
            return Error.Validation("Contact email format is invalid.");

        if (email.Length > 254)
            return Error.Validation("Contact email must not exceed 254 characters.");

        if (jobTitle is not null && jobTitle.Length > 100)
            return Error.Validation("Job title must not exceed 100 characters.");

        if (phone is not null && phone.Length > 50)
            return Error.Validation("Phone must not exceed 50 characters.");

        return new CustomerContact
        {
            Id = CustomerContactId.New(),
            CustomerId = customerId,
            Name = name.Trim(),
            JobTitle = jobTitle?.Trim(),
            Email = email.Trim().ToLowerInvariant(),
            Phone = phone?.Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
    }

    internal Result Update(string name, string? jobTitle, string email, string? phone)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Error.Validation("Contact name is required.");

        if (name.Length > 150)
            return Error.Validation("Contact name must not exceed 150 characters.");

        if (string.IsNullOrWhiteSpace(email))
            return Error.Validation("Contact email is required.");

        if (!email.Contains('@') || !email.Contains('.'))
            return Error.Validation("Contact email format is invalid.");

        if (email.Length > 254)
            return Error.Validation("Contact email must not exceed 254 characters.");

        if (jobTitle is not null && jobTitle.Length > 100)
            return Error.Validation("Job title must not exceed 100 characters.");

        if (phone is not null && phone.Length > 50)
            return Error.Validation("Phone must not exceed 50 characters.");

        Name = name.Trim();
        JobTitle = jobTitle?.Trim();
        Email = email.Trim().ToLowerInvariant();
        Phone = phone?.Trim();

        return Result.Success();
    }

    internal void LinkUser(Guid userId)
    {
        LinkedUserId = userId;
    }
}
