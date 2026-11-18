using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Domain.Results;
using Microsoft.EntityFrameworkCore;
using Notifications.Application.Abstractions;
using Notifications.Contracts;
using Notifications.Domain.Notifications;
using Notifications.Domain.Preferences;

namespace Notifications.Application.Features;

public sealed record GetMyNotificationPreferencesQuery : IQuery<NotificationPreferencesDto>;

public sealed class GetMyNotificationPreferencesQueryHandler(INotificationsDbContext db, IUserContext user)
    : IQueryHandler<GetMyNotificationPreferencesQuery, NotificationPreferencesDto>
{
    public async Task<Result<NotificationPreferencesDto>> Handle(GetMyNotificationPreferencesQuery request, CancellationToken cancellationToken)
    {
        if (user.UserId is not { } userId)
            return Error.Unauthorized("Authentication is required.", "Notifications.Unauthenticated");
        return await NotificationPreferenceReader.ReadAsync(db, userId, cancellationToken);
    }
}

public sealed record UpdateMyNotificationPreferenceCommand(string? Key, bool? Enabled) : ICommand<NotificationPreferencesDto>;

public sealed class UpdateMyNotificationPreferenceCommandHandler(INotificationsDbContext db, IUserContext user)
    : ICommandHandler<UpdateMyNotificationPreferenceCommand, NotificationPreferencesDto>
{
    public async Task<Result<NotificationPreferencesDto>> Handle(UpdateMyNotificationPreferenceCommand request, CancellationToken cancellationToken)
    {
        if (user.UserId is not { } userId)
            return Error.Unauthorized("Authentication is required.", "Notifications.Unauthenticated");
        if (!NotificationPreferenceKeys.IsSupported(request.Key) || request.Enabled is not { } enabled)
            return Error.Validation("A valid preference key and enabled value are required.", "Notifications.Preference.Invalid");

        var preference = await db.NotificationPreferences.SingleOrDefaultAsync(
            item => item.UserId == userId && item.Key == request.Key, cancellationToken);
        if (preference is null)
        {
            var created = NotificationPreference.Create(userId, request.Key!, enabled).Value;
            db.NotificationPreferences.Add(created);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Two devices may create the same choice together. Retry only when the exact
                // account/key now exists; unrelated storage failures must remain failures.
                db.NotificationPreferences.Remove(created);
                preference = await db.NotificationPreferences.SingleOrDefaultAsync(
                    item => item.UserId == userId && item.Key == request.Key, cancellationToken);
                if (preference is null) throw;
                preference.SetEnabled(enabled);
                await db.SaveChangesAsync(cancellationToken);
            }
        }
        else
        {
            preference.SetEnabled(enabled);
            await db.SaveChangesAsync(cancellationToken);
        }
        return await NotificationPreferenceReader.ReadAsync(db, userId, cancellationToken);
    }
}

internal static class NotificationPreferenceReader
{
    public static async Task<NotificationPreferencesDto> ReadAsync(INotificationsDbContext db, Guid userId, CancellationToken cancellationToken)
    {
        var disabled = await db.NotificationPreferences.AsNoTracking()
            .Where(preference => preference.UserId == userId && !preference.Enabled)
            .Select(preference => preference.Key).ToListAsync(cancellationToken);
        return new(
            !disabled.Contains(NotificationPreferenceKeys.Enabled),
            !disabled.Contains(NotificationPreferenceKeys.FlightAssignments),
            !disabled.Contains(NotificationPreferenceKeys.FlightInvitations),
            !disabled.Contains(NotificationPreferenceKeys.ScheduleChanges),
            !disabled.Contains(NotificationPreferenceKeys.ColleagueEdits),
            !disabled.Contains(NotificationPreferenceKeys.SyncIssues),
            !disabled.Contains(NotificationPreferenceKeys.DirectShares),
            !disabled.Contains(NotificationPreferenceKeys.StationShares),
            !disabled.Contains(NotificationPreferenceKeys.FlightReminders));
    }
}

/// <summary>Checks current account choices before creating an inbox row or retrying delivery.</summary>
public static class NotificationPreferenceFilter
{
    public static async Task<bool> IsEnabledAsync(INotificationsDbContext db, Guid recipientUserId, string kind, CancellationToken cancellationToken = default)
    {
        var key = kind switch
        {
            NotificationKind.StaffAssignedToFlight => NotificationPreferenceKeys.FlightAssignments,
            NotificationKind.EmployeeInvitedToFlight => NotificationPreferenceKeys.FlightInvitations,
            NotificationKind.FlightScheduleUpdated => NotificationPreferenceKeys.ScheduleChanges,
            NotificationKind.FlightSubmissionUpdated => NotificationPreferenceKeys.ColleagueEdits,
            NotificationKind.FlightReminder => NotificationPreferenceKeys.FlightReminders,
            NotificationKind.FlightSharedWithEmployee => NotificationPreferenceKeys.DirectShares,
            NotificationKind.FlightSharedWithStation => NotificationPreferenceKeys.StationShares,
            "FlightSyncIssue" => NotificationPreferenceKeys.SyncIssues,
            _ => null
        };
        return !await db.NotificationPreferences.AsNoTracking().AnyAsync(
            preference => preference.UserId == recipientUserId && !preference.Enabled &&
                (preference.Key == NotificationPreferenceKeys.Enabled || preference.Key == key), cancellationToken);
    }
}
