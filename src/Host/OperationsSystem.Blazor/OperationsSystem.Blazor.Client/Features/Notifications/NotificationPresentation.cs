using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.State;

namespace OperationsSystem.Blazor.Client.Features.Notifications;

public static class NotificationPresentation
{
    public static string Title(NotificationDto notification, LocaleState locale) =>
        locale.IsRightToLeft && !string.IsNullOrWhiteSpace(notification.TitleAr)
            ? notification.TitleAr
            : notification.TitleEn;

    public static string Body(NotificationDto notification, LocaleState locale) =>
        locale.IsRightToLeft && !string.IsNullOrWhiteSpace(notification.BodyAr)
            ? notification.BodyAr
            : notification.BodyEn;

    public static bool TryGetFlightId(NotificationDto notification, out Guid flightId)
    {
        foreach (var item in notification.Payload)
        {
            if (string.Equals(item.Key, "flightId", StringComparison.OrdinalIgnoreCase) &&
                Guid.TryParse(item.Value, out flightId) &&
                flightId != Guid.Empty)
            {
                return true;
            }
        }

        flightId = Guid.Empty;
        return false;
    }

    public static string? DeepLink(NotificationDto notification) =>
        TryGetFlightId(notification, out var flightId)
            ? $"/operations/flights/{flightId}"
            : null;
}
