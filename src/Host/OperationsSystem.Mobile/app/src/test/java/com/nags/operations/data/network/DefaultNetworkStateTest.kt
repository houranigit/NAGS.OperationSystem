package com.nags.operations.data.network

import org.junit.Assert.*
import org.junit.Test

class DefaultNetworkStateTest {
    @Test fun matchingLostNetworkImmediatelyBecomesOffline() {
        val state = DefaultNetworkState<String>()
        assertFalse(state.isOnline)
        state.onAvailable("wifi")
        assertEquals(true, state.onCapabilitiesChanged("wifi", true))
        assertTrue(state.isOnline)

        assertEquals(false, state.onLost("wifi"))
        assertFalse(state.isOnline)
        // A late capability callback must not resurrect a network already reported lost.
        assertNull(state.onCapabilitiesChanged("wifi", true))
        assertFalse(state.isOnline)
    }

    @Test fun oldNetworkLossCannotClearANewerValidatedDefault() {
        val state = DefaultNetworkState<String>()
        state.onAvailable("wifi")
        state.onCapabilitiesChanged("wifi", true)
        assertEquals(false, state.onAvailable("cellular"))
        assertEquals(true, state.onCapabilitiesChanged("cellular", true))

        assertNull(state.onLost("wifi"))
        assertNull(state.onCapabilitiesChanged("wifi", false))
        assertTrue(state.isOnline)
        assertEquals(false, state.onLost("cellular"))
    }

    @Test fun availabilityWaitsForValidationAndTracksValidationLoss() {
        val state = DefaultNetworkState<String>()
        assertNull(state.onAvailable("captive-portal"))
        assertFalse(state.isOnline)
        assertNull(state.onCapabilitiesChanged("captive-portal", false))
        assertEquals(true, state.onCapabilitiesChanged("captive-portal", true))
        assertEquals(false, state.onCapabilitiesChanged("captive-portal", false))
        assertFalse(state.isOnline)
    }

    @Test fun capabilitiesFromANonDefaultNetworkAreIgnored() {
        val state = DefaultNetworkState<String>()
        state.onAvailable("wifi")
        assertNull(state.onCapabilitiesChanged("cellular", true))
        assertFalse(state.isOnline)
        state.onCapabilitiesChanged("wifi", true)
        assertNull(state.onCapabilitiesChanged("cellular", false))
        assertTrue(state.isOnline)
    }

    @Test fun duplicateAvailabilityDoesNotForgetKnownValidation() {
        val state = DefaultNetworkState<String>()
        state.onAvailable("wifi")
        state.onCapabilitiesChanged("wifi", true)
        assertNull(state.onAvailable("wifi"))
        assertTrue(state.isOnline)
    }
}
