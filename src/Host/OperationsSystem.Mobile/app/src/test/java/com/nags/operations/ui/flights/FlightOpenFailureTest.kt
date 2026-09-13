package com.nags.operations.ui.flights

import com.nags.operations.data.ApiException
import java.io.IOException
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class FlightOpenFailureTest {
    @Test
    fun removedAndUnauthorizedNotificationTargetsDoNotOfferAnEndlessRetry() {
        for (status in listOf(400, 403, 404, 410, 422)) {
            val failure = flightOpenFailure(ApiException(status, "unavailable"))
            assertEquals(FlightOpenFailure.Unavailable, failure)
            assertFalse(failure.canRetry)
            assertFalse(shouldUseInformationalFlightFallback(ApiException(status, "unavailable")))
        }
    }

    @Test
    fun expiredSessionHasDistinctGuidanceWithoutMislabelingTheFlightAsMissing() {
        val failure = flightOpenFailure(ApiException(401, "unauthorized"))
        assertEquals(FlightOpenFailure.Session, failure)
        assertTrue(failure.canRetry)
        assertFalse(shouldUseInformationalFlightFallback(ApiException(401, "unauthorized")))
    }

    @Test
    fun networkAndTemporaryServerFailuresRemainRetryable() {
        assertEquals(FlightOpenFailure.Connection, flightOpenFailure(IOException("offline")))
        assertTrue(flightOpenFailure(IOException("offline")).canRetry)
        for (status in listOf(408, 429, 500, 503)) {
            assertEquals(FlightOpenFailure.Temporary, flightOpenFailure(ApiException(status, "temporary")))
            assertTrue(flightOpenFailure(ApiException(status, "temporary")).canRetry)
        }
    }
}
