namespace Notifications.Application.Abstractions;

/// <summary>
/// Marker interface used by <c>CompositeNotificationPusher</c> to discover transports
/// (SignalR, FCM, …) without recursing into itself when DI resolves
/// <see cref="INotificationPusher"/>. Each transport implements this marker; the
/// composite implements <see cref="INotificationPusher"/> only.
/// </summary>
public interface IInnerNotificationPusher : INotificationPusher
{
}
