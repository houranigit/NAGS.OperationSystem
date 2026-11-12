package com.nags.operations.ui.flights

import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import org.junit.Assert.*
import org.junit.Test

class SignatureEditSessionTest {
    @Test fun existingSignatureIsReadOnlyUntilReplaceAndUndoRestoresTheOriginalPreview() {
        val original = "2.0|100,200;300,400"
        val opened = SignatureEditSession(original, viewingOriginal = true)
        assertEquals(opened, opened.draw("accidental touch"))
        val replacement = opened.replace().draw("new stroke").draw("two strokes")
        assertEquals(original, replacement.original)
        assertEquals("two strokes", replacement.displayedInk)
        val restoredStroke = replacement.clear().undo()
        assertEquals(replacement.displayedInk, restoredStroke.displayedInk)
        assertEquals("", restoredStroke.redo().displayedInk)
        val restoredOriginal = restoredStroke.undo().undo().undo()
        assertTrue(restoredOriginal.viewingOriginal)
        assertEquals(original, restoredOriginal.displayedInk)
        assertEquals(opened.original, restoredOriginal.original)
        assertEquals(replacement.displayedInk, restoredOriginal.redo().redo().redo().displayedInk)
    }

    @Test fun aSavedPngCanBeKeptAfterReplacingAndClearingWithoutInventingInk() {
        val opened = SignatureEditSession("", viewingOriginal = true)
        val drawing = opened.replace().draw("new ink")
        assertEquals("", drawing.original)
        assertEquals("new ink", drawing.clear().undo().draft)
        assertTrue(drawing.undo().undo().viewingOriginal)
        assertEquals("", opened.draft)
        assertEquals(drawing, Json.decodeFromString<SignatureEditSession>(Json.encodeToString(drawing)))
    }

    @Test fun clearAndReplacementCanBeRedoneWithoutMutatingThePinnedSignature() {
        val original = "saved original"
        val opened = SignatureEditSession(original, true)
        val cleared = opened.replace().draw("replacement").clear()
        assertEquals("replacement", cleared.undo().draft)
        assertEquals("", cleared.undo().redo().draft)
        assertEquals(original, cleared.original)
        assertEquals(original, opened.displayedInk)

        val originalAgain = cleared.undo().undo().undo()
        assertTrue(originalAgain.viewingOriginal)
        assertFalse(originalAgain.hasChanges)
        assertEquals("replacement", originalAgain.redo().redo().draft)
    }

    @Test fun drawingAfterUndoStartsANewBranchWhileUnchangedInputKeepsRedo() {
        val beforeUndo = SignatureEditSession("", false).draw("one").draw("two")
        val undone = beforeUndo.undo()
        assertEquals("two", undone.draw("one").redo().draft)
        val branched = undone.draw("replacement")
        assertTrue(branched.redoHistory.isEmpty())
        assertEquals(branched, branched.redo())
        assertEquals("one", branched.undo().draft)
    }

    @Test fun redoSurvivesConfigurationSerializationAndOldSessionStateStillRestores() {
        val undone = SignatureEditSession("original", true).replace().draw("replacement").undo()
        val restored = Json.decodeFromString<SignatureEditSession>(Json.encodeToString(undone))
        assertEquals("replacement", restored.redo().draft)
        assertEquals("original", restored.original)
        val old = Json.decodeFromString<SignatureEditSession>("""{"original":"saved","viewingOriginal":true}""")
        assertEquals("saved", old.displayedInk)
        assertTrue(old.history.isEmpty())
        assertTrue(old.redoHistory.isEmpty())
    }

    @Test fun longDraftHistoryIsBoundedWithoutDiscardingThePinnedOriginal() {
        var session = SignatureEditSession("original", true).replace()
        repeat(50) { session = session.draw("x".repeat(4000) + it) }
        fun assertBudget(value: SignatureEditSession) {
            assertTrue(value.history.size + value.redoHistory.size <= 32)
            assertTrue(value.history.sumOf { it.draft.length.toLong() } + value.redoHistory.sumOf { it.draft.length.toLong() } <= 64_000L)
            assertEquals("original", value.original)
        }
        assertBudget(session)
        assertEquals("original", session.original)
        assertTrue(session.draft.endsWith("49"))
        repeat(50) { session = session.undo(); assertBudget(session) }
        repeat(50) { session = session.redo(); assertBudget(session) }
    }

    @Test fun shortStrokeHistoryRetainsThirtyTwoUndoAndRedoSteps() {
        var session = SignatureEditSession("", false)
        repeat(40) { session = session.draw("stroke $it") }
        assertEquals(32, session.history.size)
        repeat(32) { session = session.undo() }
        assertEquals("stroke 7", session.draft)
        assertTrue(session.history.isEmpty())
        assertEquals(32, session.redoHistory.size)
        repeat(32) { session = session.redo() }
        assertEquals("stroke 39", session.draft)
        assertTrue(session.redoHistory.isEmpty())
    }

    @Test fun previewSamplingBoundsEvenVeryLargeAndNarrowPngDimensions() {
        for ((width, height) in listOf(1024 to 512, 2049 to 1, 4000 to 3000, 1 to Int.MAX_VALUE, Int.MAX_VALUE to Int.MAX_VALUE)) {
            val sample = signaturePreviewSampleSize(width, height)
            assertTrue(sample > 0 && (sample and (sample - 1)) == 0)
            assertTrue((width.toLong() + sample - 1) / sample <= 1024 && (height.toLong() + sample - 1) / sample <= 1024)
        }
    }
}
