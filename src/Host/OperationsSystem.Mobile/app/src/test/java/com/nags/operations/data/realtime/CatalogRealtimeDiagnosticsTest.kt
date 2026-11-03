package com.nags.operations.data.realtime

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class CatalogRealtimeDiagnosticsTest {
    @Test
    fun onlyAcceptedSharedCatalogChangesAdvanceTheLastEventTime() {
        val diagnostics = CatalogRealtimeDiagnostics()
        val connection = diagnostics.beginAttempt(reconnecting = false)
        assertNull(diagnostics.lastEventAt.value)
        assertFalse(diagnostics.acceptChange(connection, "customers", 100))
        assertTrue(diagnostics.connected(connection))
        assertNull(diagnostics.lastEventAt.value)
        assertFalse(diagnostics.acceptChange(connection, "flights", 200))
        assertFalse(diagnostics.acceptChange(connection, "work-orders", 200))
        assertFalse(diagnostics.acceptChange(connection, null, 200))
        assertTrue(diagnostics.acceptChange(connection, "customers", 300))
        assertEquals(300L, diagnostics.lastEventAt.value)

        // Network loss changes the badge without erasing the useful last-event timestamp.
        diagnostics.disconnect()
        assertEquals(RealtimeChannelState.Disconnected, diagnostics.state.value)
        assertEquals(300L, diagnostics.lastEventAt.value)
        assertFalse(diagnostics.acceptChange(connection, "customers", 400))
        val reconnected = diagnostics.beginAttempt(reconnecting = true)
        diagnostics.connected(reconnected)
        assertEquals(300L, diagnostics.lastEventAt.value)
    }

    @Test
    fun oldConnectionCallbacksCannotChangeTheNewConnectionOrPublishEvents() {
        val diagnostics = CatalogRealtimeDiagnostics()
        val first = diagnostics.beginAttempt(reconnecting = false)
        diagnostics.connected(first)
        diagnostics.acceptChange(first, "services", 100)
        diagnostics.reconnecting(first)
        assertEquals(RealtimeChannelState.Reconnecting, diagnostics.state.value)
        assertFalse(diagnostics.acceptChange(first, "services", 200))

        val second = diagnostics.beginAttempt(reconnecting = true)
        diagnostics.connected(second)
        diagnostics.reconnecting(first)
        diagnostics.disconnect(first)
        assertFalse(diagnostics.connected(first))
        assertFalse(diagnostics.acceptChange(first, "services", 300))
        assertEquals(RealtimeChannelState.Connected, diagnostics.state.value)
        assertEquals(100L, diagnostics.lastEventAt.value)
        assertTrue(diagnostics.acceptChange(second, "aircraft-types", 400))
        assertEquals(400L, diagnostics.lastEventAt.value)
    }

    @Test
    fun endingTheSessionClearsItsEventTimeAndRejectsLateConnectionCompletion() {
        val diagnostics = CatalogRealtimeDiagnostics()
        val oldAccount = diagnostics.beginAttempt(reconnecting = false)
        diagnostics.connected(oldAccount)
        diagnostics.acceptChange(oldAccount, "customers", 100)
        diagnostics.disconnect(clearLastEvent = true)
        assertEquals(RealtimeChannelState.Disconnected, diagnostics.state.value)
        assertNull(diagnostics.lastEventAt.value)
        assertFalse(diagnostics.connected(oldAccount))
        assertFalse(diagnostics.acceptChange(oldAccount, "customers", 200))

        val newAccount = diagnostics.beginAttempt(reconnecting = false)
        diagnostics.connected(newAccount)
        assertFalse(diagnostics.acceptChange(oldAccount, "customers", 300))
        assertNull(diagnostics.lastEventAt.value)
    }
}
