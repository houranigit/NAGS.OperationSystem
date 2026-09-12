namespace Identity.Contracts;

/// <summary>Read-only account details used for an employee's work-order submission receipt.</summary>
public interface IWorkOrderEmailRecipientReader
{
    public Task<WorkOrderEmailRecipient?> GetAsync(Guid userId, CancellationToken cancellationToken = default);
}

public sealed record WorkOrderEmailRecipient(
    Guid UserId,
    string Email,
    string DisplayName,
    bool ReceiveWorkOrderSubmissionEmails);
