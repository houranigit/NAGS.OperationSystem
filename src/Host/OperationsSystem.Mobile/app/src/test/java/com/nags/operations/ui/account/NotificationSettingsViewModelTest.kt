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
        }, { _, _ -> writes++; saved }, scope)
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
            { _, _ -> response.await() }, scope)
        model.setPreference(NotificationPreferenceKey.Enabled, false)
        assertEquals(NotificationPreferenceKey.Enabled, model.state.value.savingKey)
        assertTrue(model.state.value.preferences!!.enabled)
        response.completeExceptionally(java.io.IOException())
        assertTrue(model.state.value.preferences!!.enabled)
        assertNotNull(model.state.value.error)
        assertNull(model.state.value.savingKey)
        assertTrue(model.state.value.canEdit)
    }
}
