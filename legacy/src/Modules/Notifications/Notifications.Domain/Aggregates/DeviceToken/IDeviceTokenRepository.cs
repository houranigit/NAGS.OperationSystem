namespace Notifications.Domain.Aggregates.DeviceToken;

public interface IDeviceTokenRepository
{
    Task<DeviceToken?> GetByUserAndTokenAsync(
        Guid userId,
        string token,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DeviceToken>> GetActiveByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    void Add(DeviceToken deviceToken);
    void Update(DeviceToken deviceToken);
}
