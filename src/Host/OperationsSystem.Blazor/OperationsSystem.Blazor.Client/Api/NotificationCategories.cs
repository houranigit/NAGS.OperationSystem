namespace OperationsSystem.Blazor.Client.Api;

public static class NotificationCategories
{
    public const string Invitations = "Invitations";
    public const string Reminders = "Reminders";
    public const string FlightUpdates = "FlightUpdates";

    public static IReadOnlyList<string> All { get; } = [Invitations, Reminders, FlightUpdates];
}
