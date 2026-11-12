package com.nags.operations.ui.flights

import org.junit.Assert.assertEquals
import org.junit.Assert.assertSame
import org.junit.Test

class FlightWorkDraftEditingTest {
    @Test fun editingNewServiceKeepsSubmittedRowsAndTheirPositions() {
        val saved = FlightServiceDraft(id = "saved", reference = "server")
        val local = FlightServiceDraft(id = "local", reference = "draft")
        val incoming = FlightServiceDraft(id = "incoming", reference = "other employee")
        val next = editLocalWorkDrafts(listOf(saved, local, incoming), setOf("saved", "incoming"), { it.id }) {
            listOf(it.single().copy(reference = "edited"), FlightServiceDraft(id = "added"))
        }
        assertEquals(listOf("saved", "local", "incoming", "added"), next.map { it.id })
        assertSame(saved, next[0]); assertSame(incoming, next[2])
        assertEquals("edited", next[1].reference)
    }
    @Test fun removingLocalTaskCannotReplaceOrDuplicateSubmittedIdentity() {
        val saved = FlightTaskDraft(id = "saved", remarks = "submitted")
        val result = editLocalWorkDrafts(listOf(saved, FlightTaskDraft(id = "local")), setOf("saved"), { it.id }) {
            listOf(saved.copy(remarks = "incorrect scope"), FlightTaskDraft(id = "new"), FlightTaskDraft(id = "new"))
        }
        assertEquals(listOf("saved", "new"), result.map { it.id })
        assertSame(saved, result.first())
    }
    @Test fun revertingLocalServiceEditDoesNotManufactureOrderOnlyChanges() {
        val current = listOf(FlightServiceDraft(id = "local", reference = "original"), FlightServiceDraft(id = "saved"))
        val edited = editLocalWorkDrafts(current, setOf("saved"), { it.id }) { it.map { row -> row.copy(reference = "edited") } }
        val reverted = editLocalWorkDrafts(edited, setOf("saved"), { it.id }) { it.map { row -> row.copy(reference = "original") } }
        assertEquals(serviceEditorFingerprint(current, emptyList(), emptyList()), serviceEditorFingerprint(reverted, emptyList(), emptyList()))
    }
}
