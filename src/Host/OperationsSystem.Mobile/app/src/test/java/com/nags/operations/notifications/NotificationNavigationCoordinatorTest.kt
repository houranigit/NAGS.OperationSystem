package com.nags.operations.notifications

import android.content.SharedPreferences
import com.nags.operations.data.notifications.NotificationKinds
import com.nags.operations.data.notifications.NotificationOpenRequest
import com.nags.operations.data.notifications.NotificationPushPayload
import java.lang.reflect.Proxy
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class NotificationNavigationCoordinatorTest {
    @Test
    fun acceptedTapIsPersistedBeforeTheRetainedIntentIsCleared() {
        val preferences = TestPreferences()
        val coordinator = NotificationNavigationCoordinator(preferences.value)
        val intentData = assignmentData().toMutableMap()

        coordinator.acceptIntentData(intentData.toMap()) {
            assertEquals("notification-1", NotificationNavigationCoordinator(preferences.value).pending.value?.notificationId)
            intentData.clear()
        }

        assertTrue(intentData.isEmpty())
        assertEquals("flight-1", coordinator.pending.value?.flightId)
    }

    @Test
    fun consumedTapCannotReplayFromTheRetainedIntentOnRecreation() {
        val preferences = TestPreferences()
        val coordinator = NotificationNavigationCoordinator(preferences.value)
        val intentData = assignmentData().toMutableMap()
        coordinator.acceptIntentData(intentData.toMap()) { intentData.clear() }
        coordinator.consume("notification-1")

        val recreated = NotificationNavigationCoordinator(preferences.value)
        recreated.acceptIntentData(intentData, restoringActivity = true) { intentData.clear() }

        assertNull(recreated.pending.value)
    }

    @Test
    fun processRestorationDoesNotRepublishAndroidsOriginalLaunchExtrasAfterConsumption() {
        val preferences = TestPreferences()
        val originalLaunchData = assignmentData()
        val coordinator = NotificationNavigationCoordinator(preferences.value)
        coordinator.acceptIntentData(originalLaunchData) { }
        coordinator.consume("notification-1")
        var cleared = false

        val restored = NotificationNavigationCoordinator(preferences.value)
        restored.acceptIntentData(originalLaunchData, restoringActivity = true) { cleared = true }

        assertNull(restored.pending.value)
        assertTrue(cleared)
    }

    @Test
    fun anUnfinishedTapSurvivesRecreationAndSameUserReauthentication() {
        val preferences = TestPreferences()
        val originalLaunchData = assignmentData()
        val coordinator = NotificationNavigationCoordinator(preferences.value)
        coordinator.acceptIntentData(originalLaunchData) { }

        // A token expiry does not clear navigation. The login screen can restore this pending tap.
        val restored = NotificationNavigationCoordinator(preferences.value)
        restored.acceptIntentData(originalLaunchData, restoringActivity = true) { }

        assertEquals(coordinator.pending.value, restored.pending.value)
        assertEquals("notification-1", restored.pending.value?.notificationId)
    }

    @Test
    fun aFreshTapAfterRecreationCanOpenTheSameNotificationAgain() {
        val preferences = TestPreferences()
        val originalLaunchData = assignmentData()
        val coordinator = NotificationNavigationCoordinator(preferences.value)
        coordinator.acceptIntentData(originalLaunchData) { }
        coordinator.consume("notification-1")
        val recreated = NotificationNavigationCoordinator(preferences.value)
        recreated.acceptIntentData(originalLaunchData, restoringActivity = true) { }

        recreated.acceptIntentData(originalLaunchData) { }

        assertEquals("notification-1", recreated.pending.value?.notificationId)
    }

    @Test
    fun aNewTapReplacesAnUnfinishedOlderTapAndCannotBeClearedByItsCompletion() {
        val preferences = TestPreferences()
        val coordinator = NotificationNavigationCoordinator(preferences.value)
        coordinator.acceptIntentData(assignmentData()) { }
        coordinator.acceptIntentData(assignmentData("notification-2", "flight-2")) { }

        coordinator.consume("notification-1")

        assertEquals("notification-2", coordinator.pending.value?.notificationId)
        assertEquals("flight-2", NotificationNavigationCoordinator(preferences.value).pending.value?.flightId)
    }

    @Test
    fun explicitSessionEndClearsEvenAnUnownedLegacyOrInboxHandoff() {
        val preferences = TestPreferences()
        val coordinator = NotificationNavigationCoordinator(preferences.value)
        coordinator.publish(NotificationOpenRequest("notification-1", "flight-1"))

        coordinator.clearForSessionEnd()

        assertNull(coordinator.pending.value)
        assertNull(NotificationNavigationCoordinator(preferences.value).pending.value)
    }

    @Test
    fun systemTapAlwaysCarriesTheVerifiedRecipientIncludingLegacyPayloads() {
        for (untrustedRecipient in listOf(null, "another-user")) {
            val payload = requireNotNull(NotificationPushPayload.fromData(assignmentData())).copy(
                recipientUserId = untrustedRecipient,
            )
            val data = systemNotificationIntentData(payload, "verified-user")
            val coordinator = NotificationNavigationCoordinator(TestPreferences().value)

            coordinator.acceptIntentData(data) { }

            assertEquals("verified-user", coordinator.pending.value?.recipientUserId)
        }
    }

    @Test
    fun aNormalLauncherIntentDoesNotReplaceAnUnfinishedHandoff() {
        val coordinator = NotificationNavigationCoordinator(TestPreferences().value)
        coordinator.acceptIntentData(assignmentData()) { }
        var cleared = false

        coordinator.acceptIntentData(emptyMap()) { cleared = true }

        assertFalse(cleared)
        assertEquals("notification-1", coordinator.pending.value?.notificationId)
    }

    private fun assignmentData(notificationId: String = "notification-1", flightId: String = "flight-1") = mapOf(
        "notificationId" to notificationId,
        "flightId" to flightId,
        "kind" to NotificationKinds.StaffAssignedToFlight,
    )

    /** Implements only the persistence contract used by the coordinator, with durable shared data. */
    private class TestPreferences {
        private val stored = mutableMapOf<String, String?>()
        val value: SharedPreferences = Proxy.newProxyInstance(
            SharedPreferences::class.java.classLoader,
            arrayOf(SharedPreferences::class.java),
        ) { _, method, args ->
            when (method.name) {
                "getString" -> stored[args!![0] as String] ?: args[1]
                "edit" -> editor()
                else -> error("Unexpected preferences method ${method.name}")
            }
        } as SharedPreferences

        private fun editor(): SharedPreferences.Editor {
            val updates = mutableMapOf<String, String?>()
            var clear = false
            lateinit var editor: SharedPreferences.Editor
            editor = Proxy.newProxyInstance(
                SharedPreferences.Editor::class.java.classLoader,
                arrayOf(SharedPreferences.Editor::class.java),
            ) { _, method, args ->
                when (method.name) {
                    "putString" -> {
                        updates[args!![0] as String] = args[1] as String?
                        editor
                    }
                    "clear" -> { clear = true; editor }
                    "commit" -> {
                        if (clear) stored.clear()
                        updates.forEach { (key, value) -> if (value == null) stored.remove(key) else stored[key] = value }
                        true
                    }
                    else -> error("Unexpected preferences editor method ${method.name}")
                }
            } as SharedPreferences.Editor
            return editor
        }
    }
}
