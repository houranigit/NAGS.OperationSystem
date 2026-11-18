package com.nags.operations.notifications

import org.junit.Assert.*
import org.junit.Test

class SyncIssueAlertsTest {
    @Test
    fun retriesAndReviewAlertOnceAcrossRestartAndRemainAccountScoped() {
        val saved = mutableMapOf<String, Set<String>>()
        fun tracker() = SyncIssueAlertTracker({ saved[it].orEmpty() }, { owner, values -> saved[owner] = values })
        val failed = SyncIssueRecord("account-a", "arrival:flight:change", "pending", true)
        val other = failed.copy(owner = "account-b")
        assertTrue(tracker().update("account-a", listOf(failed, other), true))
        assertFalse(tracker().update("account-a", listOf(failed), true))
        assertTrue(tracker().update("account-b", listOf(failed, other), true))
        assertTrue(tracker().update("account-a", listOf(failed.copy(status = "conflict")), true))
        assertFalse(tracker().update("account-a", emptyList(), true))
        assertTrue(tracker().update("account-a", listOf(failed), true))
    }

    @Test
    fun disabledPreferenceDoesNotReplayOldIssuesWhenReenabledAndOfflineSavesDoNotAlert() {
        val saved = mutableMapOf<String, Set<String>>()
        val tracker = SyncIssueAlertTracker({ saved[it].orEmpty() }, { owner, values -> saved[owner] = values })
        val ordinary = SyncIssueRecord("account", "service:flight:change", "pending", false)
        assertFalse(tracker.update("account", listOf(ordinary), true))
        val failed = ordinary.copy(status = "failed")
        assertFalse(tracker.update("account", listOf(failed), false))
        assertFalse(tracker.update("account", listOf(failed), true))
        assertTrue(tracker.update("account", listOf(failed, failed.copy(id = "new-change")), true))
    }
}
