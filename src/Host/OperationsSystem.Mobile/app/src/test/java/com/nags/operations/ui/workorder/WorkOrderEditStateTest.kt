package com.nags.operations.ui.workorder

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class WorkOrderEditStateTest {
    @Test
    fun older_save_completion_does_not_clear_a_newer_edit() {
        val firstEdit = WorkOrderEditState().afterEdit()
        val savedSnapshotRevision = firstEdit.revision
        val editedDuringSave = firstEdit.afterEdit()

        val afterOlderSave = editedDuringSave.afterSuccessfulSave(savedSnapshotRevision)

        assertEquals(2L, afterOlderSave.revision)
        assertEquals(1L, afterOlderSave.lastSavedRevision)
        assertTrue(afterOlderSave.hasUnsavedChanges)
        assertFalse(
            afterOlderSave
                .afterSuccessfulSave(afterOlderSave.revision)
                .hasUnsavedChanges,
        )
    }

    @Test
    fun stale_save_completion_cannot_move_the_saved_revision_backwards() {
        val state = WorkOrderEditState(
            revision = 4L,
            lastSavedRevision = 3L,
        )

        val result = state.afterSuccessfulSave(snapshotRevision = 2L)

        assertEquals(3L, result.lastSavedRevision)
        assertTrue(result.hasUnsavedChanges)
    }

    @Test
    fun only_idle_persistence_state_accepts_another_save_or_submit() {
        assertTrue(WorkOrderPersistenceState.Idle.canStartWrite())
        assertFalse(WorkOrderPersistenceState.SavingDraft.canStartWrite())
        assertFalse(WorkOrderPersistenceState.Submitting.canStartWrite())
    }

    @Test
    fun draft_id_reuses_active_then_session_identity_before_generating() {
        var generated = 0
        val createId = { "new-${++generated}" }

        assertEquals("active", stableDraftId("active", "session", createId))
        assertEquals("session", stableDraftId(null, "session", createId))
        val first = stableDraftId(null, null, createId)
        val retry = stableDraftId(null, first, createId)

        assertEquals("new-1", first)
        assertEquals(first, retry)
        assertEquals(1, generated)
    }
}
