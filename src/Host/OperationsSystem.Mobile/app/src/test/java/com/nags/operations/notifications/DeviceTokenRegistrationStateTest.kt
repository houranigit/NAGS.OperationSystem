package com.nags.operations.notifications

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class DeviceTokenRegistrationStateTest {
    @Test
    fun unchangedDestinationDoesNotRetriggerRegistration() {
        val state = RegistrationDestinationState("fid-1")

        assertFalse(state.update("fid-1"))
        assertTrue(state.update("fid-2"))
        assertFalse(state.update("fid-2"))
        assertEquals("fid-2", state.current())
    }

    @Test
    fun clearedDestinationCanBeRegisteredByTheNextSession() {
        val state = RegistrationDestinationState("fid-1")

        state.clear()

        assertNull(state.current())
        assertTrue(state.update("fid-1"))
    }
}
