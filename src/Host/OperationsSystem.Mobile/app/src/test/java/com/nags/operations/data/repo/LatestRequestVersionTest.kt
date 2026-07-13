package com.nags.operations.data.repo

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class LatestRequestVersionTest {
    @Test
    fun olderResponseCannotWinAfterANewerRequestStarts() {
        val versions = LatestRequestVersion()
        val older = versions.next()
        val newer = versions.next()

        assertFalse(versions.isLatest(older))
        assertTrue(versions.isLatest(newer))
    }

    @Test
    fun localMutationInvalidatesAnInFlightResponse() {
        val versions = LatestRequestVersion()
        val inFlight = versions.next()

        versions.next()

        assertFalse(versions.isLatest(inFlight))
    }
}
