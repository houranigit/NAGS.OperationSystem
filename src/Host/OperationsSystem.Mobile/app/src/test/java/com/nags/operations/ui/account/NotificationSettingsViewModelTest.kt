package com.nags.operations.ui.account

import com.nags.operations.data.notifications.NotificationPreferenceKey
import com.nags.operations.data.notifications.NotificationPreferences
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.flow.MutableStateFlow
import org.junit.After
import org.junit.Assert.*
import org.junit.Test

class NotificationSettingsViewModelTest {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Unconfined)
    @After fun tearDown() = scope.cancel()

    @Test
    fun offlineSettingsAreReadOnlyAndReconnectLoadsAccountValues() {
        val online = MutableStateFlow(false)
        val saved = NotificationPreferences(flightAssignments = false)
        var reads = 0
        var writes = 0
        val model = NotificationSettingsViewModel(online, { saved }, {
            reads++; saved.copy(stationShares = false)
        }, { _, _ -> writes++; saved }, { false }, {}, scope)
        assertEquals(saved, model.state.value.preferences)
        assertFalse(model.state.value.canEdit)
        model.setPreference(NotificationPreferenceKey.Enabled, false)
        assertEquals(0, writes)
        online.value = true
        assertEquals(1, reads)
        assertFalse(model.state.value.preferences!!.stationShares)
        assertTrue(model.state.value.canEdit)
    }

    @Test
    fun toggleWaitsForServerAndFailureDoesNotShowAnUnsavedValue() {
        val response = CompletableDeferred<NotificationPreferences>()
        val original = NotificationPreferences()
        val model = NotificationSettingsViewModel(MutableStateFlow(true), { null }, { original },
            { _, _ -> response.await() }, { false }, {}, scope)
        model.setPreference(NotificationPreferenceKey.Enabled, false)
        assertEquals(NotificationPreferenceKey.Enabled, model.state.value.savingKey)
        assertTrue(model.state.value.preferences!!.enabled)
        response.completeExceptionally(java.io.IOException())
        assertTrue(model.state.value.preferences!!.enabled)
        assertNotNull(model.state.value.error)
        assertNull(model.state.value.savingKey)
        assertTrue(model.state.value.canEdit)
    }

    @Test
    fun workOrderEmailsSaveIndependentlyOfTheNotificationsMasterSwitch() {
        val online = MutableStateFlow(true)
        val original = NotificationPreferences(enabled = false)
        val writes = mutableListOf<Boolean>()
        val response = CompletableDeferred<Unit>()
        val model = NotificationSettingsViewModel(online, { null }, { original },
            { _, _ -> error("Email uses the Identity setting") }, { false },
            { enabled ->
                writes += enabled
                response.await()
            }, scope)

        assertTrue(model.state.value.canEditWorkOrderEmail)
        model.setWorkOrderEmail(true)
        assertEquals(listOf(true), writes)
        assertEquals(false, model.state.value.workOrderEmailEnabled)
        assertFalse(model.state.value.canEditWorkOrderEmail)
        response.complete(Unit)
        assertEquals(true, model.state.value.workOrderEmailEnabled)
        assertFalse(model.state.value.preferences!!.enabled)

        online.value = false
        model.setWorkOrderEmail(false)
        assertEquals(listOf(true), writes)
        assertEquals(true, model.state.value.workOrderEmailEnabled)
        assertFalse(model.state.value.canEditWorkOrderEmail)
    }

    @Test
    fun rejectedWorkOrderEmailChangeKeepsTheLastAcknowledgedValue() {
        val model = NotificationSettingsViewModel(MutableStateFlow(true), { null }, { NotificationPreferences() },
            { _, _ -> error("Email uses the Identity setting") }, { true },
            { throw java.io.IOException() }, scope)

        model.setWorkOrderEmail(false)

        assertEquals(true, model.state.value.workOrderEmailEnabled)
        assertNotNull(model.state.value.workOrderEmailError)
        assertTrue(model.state.value.canEditWorkOrderEmail)
        assertNull(model.state.value.error)
    }
}
