package com.nags.operations.notifications

import android.content.Context
import android.content.Intent
import com.nags.operations.data.notifications.NotificationKinds
import com.nags.operations.data.notifications.NotificationOpenRequest
import com.nags.operations.data.notifications.NotificationPushPayload
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

class NotificationNavigationCoordinator(context: Context) {
    private val preferences = context.getSharedPreferences("notification_navigation", Context.MODE_PRIVATE)
    private val _pending = MutableStateFlow(readPending())
    val pending: StateFlow<NotificationOpenRequest?> = _pending.asStateFlow()

    fun acceptIntent(intent: Intent?) {
        if (intent == null) return
        val data = intent.extras?.keySet()?.associateWith { key -> intent.extras?.getString(key).orEmpty() }
            .orEmpty()
        val parsed = NotificationPushPayload.fromData(data)?.openRequest()
            ?: run {
                // A reminder carrying expiry metadata that failed parsing/expiry validation must
                // never fall through to the legacy flight-id-only navigation path.
                if (data[EXTRA_KIND] == NotificationKinds.FlightReminder) return@run null
                val notificationId = data[EXTRA_NOTIFICATION_ID]?.takeIf(String::isNotBlank)
                val flightId = data[EXTRA_FLIGHT_ID]?.takeIf(String::isNotBlank)
                if (notificationId == null && flightId == null) return@run null
                NotificationOpenRequest(
                    notificationId = notificationId,
                    flightId = flightId,
                    recipientUserId = data[EXTRA_RECIPIENT_USER_ID]?.takeIf(String::isNotBlank),
                    kind = data[EXTRA_KIND]?.takeIf(String::isNotBlank),
                    scheduledArrivalUtc = data[EXTRA_SCHEDULED_ARRIVAL_UTC]
                        ?.takeIf(String::isNotBlank),
                    leadTimeMinutes = data[EXTRA_LEAD_TIME_MINUTES]?.toIntOrNull(),
                )
            }
        parsed?.let(::publish)
    }

    fun publish(request: NotificationOpenRequest) {
        preferences.edit()
            .putString(KEY_NOTIFICATION_ID, request.notificationId)
            .putString(KEY_FLIGHT_ID, request.flightId)
            .putString(KEY_RECIPIENT_ID, request.recipientUserId)
            .putString(KEY_KIND, request.kind)
            .putString(KEY_SCHEDULED_ARRIVAL_UTC, request.scheduledArrivalUtc)
            .putString(KEY_LEAD_TIME_MINUTES, request.leadTimeMinutes?.toString())
            .apply()
        _pending.value = request
    }

    fun consume(notificationId: String?) {
        val current = _pending.value ?: return
        if (notificationId != null && current.notificationId != notificationId) return
        preferences.edit().clear().apply()
        _pending.value = null
    }

    fun discardForWrongAccount() = consume(_pending.value?.notificationId)

    private fun readPending(): NotificationOpenRequest? {
        val notificationId = preferences.getString(KEY_NOTIFICATION_ID, null)?.takeIf(String::isNotBlank)
        val flightId = preferences.getString(KEY_FLIGHT_ID, null)?.takeIf(String::isNotBlank)
        if (notificationId == null && flightId == null) return null
        val request = NotificationOpenRequest(
            notificationId = notificationId,
            flightId = flightId,
            recipientUserId = preferences.getString(KEY_RECIPIENT_ID, null),
            kind = preferences.getString(KEY_KIND, null),
            scheduledArrivalUtc = preferences.getString(KEY_SCHEDULED_ARRIVAL_UTC, null),
            leadTimeMinutes = preferences.getString(KEY_LEAD_TIME_MINUTES, null)?.toIntOrNull(),
        )
        if (request.isExpiredReminder()) {
            // Commit synchronously during startup so a second coordinator cannot restore it again.
            preferences.edit().clear().commit()
            return null
        }
        return request
    }

    companion object {
        const val EXTRA_NOTIFICATION_ID = "notificationId"
        const val EXTRA_FLIGHT_ID = "flightId"
        const val EXTRA_RECIPIENT_USER_ID = "recipientUserId"
        const val EXTRA_KIND = "kind"
        const val EXTRA_SCHEDULED_ARRIVAL_UTC = "scheduledArrivalUtc"
        const val EXTRA_LEAD_TIME_MINUTES = "leadTimeMinutes"

        private const val KEY_NOTIFICATION_ID = "notification_id"
        private const val KEY_FLIGHT_ID = "flight_id"
        private const val KEY_RECIPIENT_ID = "recipient_user_id"
        private const val KEY_KIND = "kind"
        private const val KEY_SCHEDULED_ARRIVAL_UTC = "scheduled_arrival_utc"
        private const val KEY_LEAD_TIME_MINUTES = "lead_time_minutes"
    }
}
