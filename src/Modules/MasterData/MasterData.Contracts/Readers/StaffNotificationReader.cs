namespace MasterData.Contracts.Readers;

/// <summary>Minimal cross-module projection used to route notifications to a staff member's portal user.</summary>
public sealed record StaffNotificationRecipient(Guid StaffMemberId, string FullName, Guid? LinkedUserId);

public interface IStaffNotificationReader
{
    public Task<StaffNotificationRecipient?> GetStaffRecipientAsync(Guid staffMemberId, CancellationToken cancellationToken);
}
