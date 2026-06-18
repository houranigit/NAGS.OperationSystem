namespace Core.Contracts.Features.Customer;

/// <summary>
/// One row in the contacts snapshot sent with customer create/update — aggregate reconciles the full list (updates by id, removes omitted ids, inserts null ids).
/// </summary>
/// <param name="Id">Persisted contact id for an existing row; <see langword="null"/> for a new contact.</param>
/// <param name="CreateLinkedUserOnAdd">Honored only when the row is added (new contact).</param>
public sealed record CustomerContactInput(
    Guid? Id,
    string Name,
    string Email,
    string? Phone,
    bool CreateLinkedUserOnAdd);
