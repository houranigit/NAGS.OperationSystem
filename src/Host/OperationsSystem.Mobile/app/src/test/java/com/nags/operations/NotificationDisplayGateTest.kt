package com.nags.operations

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class NotificationDisplayGateTest {
    @Test
    fun authenticatedColdStartCanEstablishTheInitialDisplaySubject() {
        val gate = NotificationDisplayGate()

        assertTrue(gate.runIfCurrent("user-1") {})
        assertFalse(gate.runIfCurrent("user-2") { error("wrong account") })
    }

    @Test
    fun deactivateCancelsActiveAccountAndRejectsLateDisplay() {
        val gate = NotificationDisplayGate()
        var displayedFor: String? = null
        var cancelledAccount: String? = null
        gate.activate("user-1")

        assertTrue(gate.runIfCurrent("USER-1") { displayedFor = it })
        gate.deactivate("fallback") { cancelledAccount = it }

        assertEquals("user-1", displayedFor)
        assertEquals("user-1", cancelledAccount)
        assertFalse(gate.runIfCurrent("user-1") { error("late display") })
    }

    @Test
    fun accountSwitchRejectsThePreviousRecipient() {
        val gate = NotificationDisplayGate()
        gate.activate("user-1")
        gate.deactivate(null) {}
        gate.activate("user-2")

        assertFalse(gate.runIfCurrent("user-1") { error("wrong account") })
        assertTrue(gate.runIfCurrent("user-2") {})
    }
}
