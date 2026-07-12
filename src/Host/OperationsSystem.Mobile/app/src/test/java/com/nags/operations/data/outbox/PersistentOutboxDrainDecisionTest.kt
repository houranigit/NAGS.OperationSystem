package com.nags.operations.data.outbox

import org.junit.Assert.assertEquals
import org.junit.Test

class PersistentOutboxDrainDecisionTest {
    @Test
    fun signedOutCompletesWithoutRetryingQueuedRows() {
        assertEquals(
            PersistentDrainResult.Complete,
            backgroundDrainDecision(
                signedIn = false,
                sawRetryable = true,
                pendingRemaining = true,
            ),
        )
    }

    @Test
    fun retryableTransportFailureUsesWorkManagerBackoff() {
        assertEquals(
            PersistentDrainResult.Retry,
            backgroundDrainDecision(
                signedIn = true,
                sawRetryable = true,
                pendingRemaining = true,
            ),
        )
    }

    @Test
    fun boundedPassRetriesWhenMorePendingRowsRemain() {
        assertEquals(
            PersistentDrainResult.Retry,
            backgroundDrainDecision(
                signedIn = true,
                sawRetryable = false,
                pendingRemaining = true,
            ),
        )
    }

    @Test
    fun terminalRowsDoNotKeepBackgroundWorkAlive() {
        assertEquals(
            PersistentDrainResult.Complete,
            backgroundDrainDecision(
                signedIn = true,
                sawRetryable = false,
                pendingRemaining = false,
            ),
        )
    }

    @Test
    fun transientHttpResponsesStayRetryable() {
        listOf(401, 408, 425, 429, 500, 503).forEach { code ->
            assertEquals(OutboxHttpDisposition.Retry, outboxHttpDisposition(code))
        }
        assertEquals(OutboxHttpDisposition.Conflict, outboxHttpDisposition(409))
        assertEquals(OutboxHttpDisposition.Failed, outboxHttpDisposition(400))
        assertEquals(OutboxHttpDisposition.Failed, outboxHttpDisposition(422))
    }
}
