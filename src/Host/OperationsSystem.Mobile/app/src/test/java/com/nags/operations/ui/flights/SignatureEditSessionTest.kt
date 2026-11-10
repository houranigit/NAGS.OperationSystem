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
        assertEquals(replacement, restoredStroke)
        val restoredOriginal = restoredStroke.undo().undo().undo()
        assertTrue(restoredOriginal.viewingOriginal)
        assertEquals(original, restoredOriginal.displayedInk)
        assertEquals(opened, restoredOriginal)
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

    @Test fun longDraftHistoryIsBoundedWithoutDiscardingThePinnedOriginal() {
        var session = SignatureEditSession("original", true).replace()
        repeat(50) { session = session.draw("x".repeat(4000) + it) }
        assertTrue(session.history.size <= 32)
        assertTrue(session.history.sumOf { it.draft.length } <= 64_000)
        assertEquals("original", session.original)
        assertTrue(session.draft.endsWith("49"))
    }

    @Test fun previewSamplingBoundsEvenVeryLargeAndNarrowPngDimensions() {
        for ((width, height) in listOf(1024 to 512, 2049 to 1, 4000 to 3000, 1 to Int.MAX_VALUE, Int.MAX_VALUE to Int.MAX_VALUE)) {
            val sample = signaturePreviewSampleSize(width, height)
            assertTrue(sample > 0 && (sample and (sample - 1)) == 0)
            assertTrue((width.toLong() + sample - 1) / sample <= 1024 && (height.toLong() + sample - 1) / sample <= 1024)
        }
    }
}
