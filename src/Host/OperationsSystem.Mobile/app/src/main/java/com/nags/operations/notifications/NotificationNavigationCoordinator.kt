package com.nags.operations.notifications

import android.content.Context
import android.content.Intent
import android.content.SharedPreferences
import com.nags.operations.data.notifications.NotificationKinds
import com.nags.operations.data.notifications.NotificationOpenRequest
import com.nags.operations.data.notifications.NotificationPushPayload
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

class NotificationNavigationCoordinator internal constructor(private val preferences: SharedPreferences) {
    constructor(context: Context) : this(context.getSharedPreferences("notification_navigation", Context.MODE_PRIVATE))
    private val _pending = MutableStateFlow(readPending())
    val pending: StateFlow<NotificationOpenRequest?> = _pending.asStateFlow()

    fun acceptIntent(intent: Intent?, restoringActivity: Boolean = false) {
        if (intent == null) return
        val data = intent.extras?.keySet()?.associateWith { key -> intent.extras?.getString(key).orEmpty() }
            .orEmpty()
        acceptIntentData(data, restoringActivity) {
            NAVIGATION_EXTRA_KEYS.forEach(intent::removeExtra)
        }
    }

    /** The durable pending handoff is the restoration source; a saved launch intent is not a new tap. */
    internal fun acceptIntentData(
        data: Map<String, String>,
        restoringActivity: Boolean = false,
        clearIntentData: () -> Unit,
    ) {
        if (restoringActivity) {
            clearIntentData()
            return
        }
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
        if (parsed != null && persistAndPublish(parsed)) {
            // The same Intent is retained by MainActivity and can otherwise replay on recreation.
            // Persist first so clearing its extras cannot lose an unfinished navigation request.
            clearIntentData()
        }
    }

    fun publish(request: NotificationOpenRequest) {
        persistAndPublish(request)
    }

    private fun persistAndPublish(request: NotificationOpenRequest): Boolean {
        val persisted = preferences.edit()
            .putString(KEY_NOTIFICATION_ID, request.notificationId)
            .putString(KEY_FLIGHT_ID, request.flightId)
            .putString(KEY_RECIPIENT_ID, request.recipientUserId)
            .putString(KEY_KIND, request.kind)
            .putString(KEY_SCHEDULED_ARRIVAL_UTC, request.scheduledArrivalUtc)
            .putString(KEY_LEAD_TIME_MINUTES, request.leadTimeMinutes?.toString())
            .commit()
        _pending.value = request
        return persisted
    }

    fun consume(notificationId: String?) {
        val current = _pending.value ?: return
        if (notificationId != null && current.notificationId != notificationId) return
        clearForSessionEnd()
    }

    fun discardForWrongAccount() = consume(_pending.value?.notificationId)

    /** Explicit logout/account switching ends the old account's navigation, even if opening failed. */
    fun clearForSessionEnd() {
        preferences.edit().clear().commit()
        _pending.value = null
    }

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

        private val NAVIGATION_EXTRA_KEYS = setOf(
            EXTRA_NOTIFICATION_ID, EXTRA_FLIGHT_ID, EXTRA_RECIPIENT_USER_ID, EXTRA_KIND,
            EXTRA_SCHEDULED_ARRIVAL_UTC, EXTRA_LEAD_TIME_MINUTES, "id", "payloadJson",
        )

        private const val KEY_NOTIFICATION_ID = "notification_id"
        private const val KEY_FLIGHT_ID = "flight_id"
        private const val KEY_RECIPIENT_ID = "recipient_user_id"
        private const val KEY_KIND = "kind"
        private const val KEY_SCHEDULED_ARRIVAL_UTC = "scheduled_arrival_utc"
        private const val KEY_LEAD_TIME_MINUTES = "lead_time_minutes"
    }
}
