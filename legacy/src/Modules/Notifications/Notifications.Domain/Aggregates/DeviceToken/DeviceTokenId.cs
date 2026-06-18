namespace Notifications.Domain.Aggregates.DeviceToken;

public readonly record struct DeviceTokenId(Guid Value)
{
    public static DeviceTokenId New() => new(Guid.NewGuid());
    public static DeviceTokenId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}
