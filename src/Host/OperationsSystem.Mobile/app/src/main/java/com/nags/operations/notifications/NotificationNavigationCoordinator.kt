package com.nags.operations.notifications

import android.content.Context
import android.content.Intent
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
            ?: data[EXTRA_FLIGHT_ID]?.takeIf(String::isNotBlank)?.let {
                NotificationOpenRequest(
                    notificationId = data[EXTRA_NOTIFICATION_ID]?.takeIf(String::isNotBlank),
                    flightId = it,
                    recipientUserId = data[EXTRA_RECIPIENT_USER_ID]?.takeIf(String::isNotBlank),
                )
            }
        parsed?.let(::publish)
    }

    fun publish(request: NotificationOpenRequest) {
        preferences.edit()
            .putString(KEY_NOTIFICATION_ID, request.notificationId)
            .putString(KEY_FLIGHT_ID, request.flightId)
            .putString(KEY_RECIPIENT_ID, request.recipientUserId)
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
        val flightId = preferences.getString(KEY_FLIGHT_ID, null)?.takeIf(String::isNotBlank) ?: return null
        return NotificationOpenRequest(
            notificationId = preferences.getString(KEY_NOTIFICATION_ID, null),
            flightId = flightId,
            recipientUserId = preferences.getString(KEY_RECIPIENT_ID, null),
        )
    }

    companion object {
        const val EXTRA_NOTIFICATION_ID = "notificationId"
        const val EXTRA_FLIGHT_ID = "flightId"
        const val EXTRA_RECIPIENT_USER_ID = "recipientUserId"

        private const val KEY_NOTIFICATION_ID = "notification_id"
        private const val KEY_FLIGHT_ID = "flight_id"
        private const val KEY_RECIPIENT_ID = "recipient_user_id"
    }
}
