package com.nags.operations.notifications

import com.google.firebase.messaging.FirebaseMessagingService
import com.google.firebase.messaging.RemoteMessage
import com.nags.operations.AppGraph
import com.nags.operations.data.notifications.NotificationPushPayload
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.launch

@Suppress("OVERRIDE_DEPRECATION")
class OperationsFirebaseMessagingService : FirebaseMessagingService() {
    override fun onNewToken(token: String) {
        AppGraph.get(applicationContext).deviceTokenManager.onRegistered(token)
    }

    override fun onRegistered(installationId: String) {
        AppGraph.get(applicationContext).deviceTokenManager.onRegistered(installationId)
    }

    override fun onMessageReceived(message: RemoteMessage) {
        val payload = NotificationPushPayload.fromData(message.data) ?: return
        val graph = AppGraph.get(applicationContext)
        runBlocking(Dispatchers.IO) {
            if (graph.tokenStore.getAccessToken() == null) return@runBlocking
            val currentSubject = graph.tokenStore.getSessionSubject()
            if (!payload.recipientUserId.isNullOrBlank() &&
                !payload.recipientUserId.equals(currentSubject, ignoreCase = true)
            ) return@runBlocking

            val accepted = runCatching {
                graph.notificationsRepository.ingest(payload.toDto(), currentSubject)
            }.getOrDefault(false)
            if (!accepted) return@runBlocking
            graph.showNotificationIfCurrent(payload, currentSubject ?: return@runBlocking)
        }
    }

    override fun onDeletedMessages() {
        val graph = AppGraph.get(applicationContext)
        graph.appScope.launch {
            if (graph.tokenStore.getAccessToken() != null) {
                runCatching { graph.notificationsRepository.refresh() }
            }
        }
    }
}
