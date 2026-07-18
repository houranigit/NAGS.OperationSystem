package com.nags.operations.notifications

import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import androidx.core.content.ContextCompat
import com.nags.operations.MainActivity
import com.nags.operations.R
import com.nags.operations.data.notifications.NotificationPushPayload
import java.util.Locale

class SystemNotificationManager(private val context: Context) {
    private val preferences = context.getSharedPreferences(PREFERENCES_NAME, Context.MODE_PRIVATE)

    fun ensureChannel() {
        val channel = NotificationChannel(
            context.getString(R.string.notification_channel_id),
            context.getString(R.string.notification_channel_name),
            NotificationManager.IMPORTANCE_HIGH,
        ).apply {
            description = context.getString(R.string.notification_channel_description)
            enableVibration(true)
        }
        context.getSystemService(NotificationManager::class.java).createNotificationChannel(channel)
    }

    fun show(payload: NotificationPushPayload, recipientUserId: String) {
        ensureChannel()
        if (Build.VERSION.SDK_INT >= 33 && ContextCompat.checkSelfPermission(
                context,
                android.Manifest.permission.POST_NOTIFICATIONS,
            ) != PackageManager.PERMISSION_GRANTED
        ) return

        val arabic = Locale.getDefault().language.equals("ar", ignoreCase = true)
        val title = (if (arabic) payload.titleAr else payload.titleEn).ifBlank {
            context.getString(R.string.notification_assigned_title)
        }
        val body = (if (arabic) payload.bodyAr else payload.bodyEn).ifBlank {
            context.getString(
                R.string.notification_assigned_body,
                payload.inviterName ?: context.getString(R.string.notification_fallback_teammate),
                payload.flightNumber ?: context.getString(R.string.notification_unknown_flight),
            )
        }

        val intent = Intent(context, MainActivity::class.java).apply {
            flags = Intent.FLAG_ACTIVITY_CLEAR_TOP or Intent.FLAG_ACTIVITY_SINGLE_TOP
            putExtra(NotificationNavigationCoordinator.EXTRA_NOTIFICATION_ID, payload.id)
            putExtra(NotificationNavigationCoordinator.EXTRA_KIND, payload.kind)
            payload.flightId?.let { putExtra(NotificationNavigationCoordinator.EXTRA_FLIGHT_ID, it) }
            payload.recipientUserId?.let {
                putExtra(NotificationNavigationCoordinator.EXTRA_RECIPIENT_USER_ID, it)
            }
            payload.scheduledArrivalUtc?.let {
                putExtra(NotificationNavigationCoordinator.EXTRA_SCHEDULED_ARRIVAL_UTC, it)
            }
            payload.leadTimeMinutes?.let {
                putExtra(NotificationNavigationCoordinator.EXTRA_LEAD_TIME_MINUTES, it.toString())
            }
        }
        val pendingIntent = PendingIntent.getActivity(
            context,
            payload.id.hashCode(),
            intent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
        )
        val notification = NotificationCompat.Builder(context, context.getString(R.string.notification_channel_id))
            .setSmallIcon(R.drawable.ic_stat_notification)
            .setColor(ContextCompat.getColor(context, R.color.brand_red))
            .setContentTitle(title)
            .setContentText(body)
            .setStyle(NotificationCompat.BigTextStyle().bigText(body))
            .setCategory(NotificationCompat.CATEGORY_EVENT)
            .setPriority(NotificationCompat.PRIORITY_HIGH)
            .setVisibility(NotificationCompat.VISIBILITY_PRIVATE)
            .setGroup(GROUP_FLIGHT_ASSIGNMENTS)
            .setAutoCancel(true)
            .setContentIntent(pendingIntent)
            .build()
        rememberPostedNotification(recipientUserId, payload.id)
        NotificationManagerCompat.from(context).notify(payload.id.hashCode(), notification)
    }

    /** Removes only alerts posted for the account leaving this device session. */
    fun cancelForAccount(recipientUserId: String) = synchronized(preferences) {
        val key = accountKey(recipientUserId)
        val ids = preferences.getStringSet(key, emptySet()).orEmpty().toSet()
        val manager = NotificationManagerCompat.from(context)
        ids.forEach { manager.cancel(it.hashCode()) }
        preferences.edit().remove(key).commit()
    }

    /** Defensive cleanup for an older build or an unreadable session owner. */
    fun cancelAllManaged() = synchronized(preferences) {
        val keys = preferences.all.keys.filter { it.startsWith(ACCOUNT_PREFIX) }
        val manager = NotificationManagerCompat.from(context)
        keys.flatMap { preferences.getStringSet(it, emptySet()).orEmpty() }
            .forEach { manager.cancel(it.hashCode()) }
        preferences.edit().also { editor -> keys.forEach(editor::remove) }.commit()
    }

    private fun rememberPostedNotification(recipientUserId: String, notificationId: String) =
        synchronized(preferences) {
            val key = accountKey(recipientUserId)
            val ids = preferences.getStringSet(key, emptySet()).orEmpty().toMutableSet()
            ids += notificationId
            // Commit before posting so a process death cannot leave an untracked visible alert.
            preferences.edit().putStringSet(key, ids).commit()
        }

    private fun accountKey(recipientUserId: String): String =
        ACCOUNT_PREFIX + recipientUserId.lowercase(Locale.ROOT)

    companion object {
        private const val GROUP_FLIGHT_ASSIGNMENTS = "flight_assignments"
        private const val PREFERENCES_NAME = "posted_flight_assignment_notifications"
        private const val ACCOUNT_PREFIX = "account:"
    }
}
