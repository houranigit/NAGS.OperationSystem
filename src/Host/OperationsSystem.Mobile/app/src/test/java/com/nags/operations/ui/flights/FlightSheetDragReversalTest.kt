package com.nags.operations.ui.flights

import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.SheetValue
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

@OptIn(ExperimentalMaterial3Api::class)
class FlightSheetDragReversalTest {
    private val drag = FlightSheetDragReversal()

    @Test fun aShortUpwardReversalKeepsTheOriginalOpenAnchorAfterALongDownwardDrag() {
        drag.start(expanded = false, touchSlop = 8f)
        drag.move(240f)
        drag.move(-12f)
        assertFalse(drag.allows(SheetValue.Hidden))
        assertTrue(drag.allows(SheetValue.PartiallyExpanded))
        assertTrue(drag.allows(SheetValue.Expanded))

        drag.start(expanded = true, touchSlop = 8f)
        drag.move(240f)
        drag.move(-12f)
        assertFalse(drag.allows(SheetValue.Hidden))
        assertFalse(drag.allows(SheetValue.PartiallyExpanded))
        assertTrue(drag.allows(SheetValue.Expanded))
    }

    @Test fun continuingDownOrOnlySmallUpwardJitterKeepsNativeDismissal() {
        drag.start(expanded = false, touchSlop = 8f)
        drag.move(240f)
        assertTrue(drag.allows(SheetValue.Hidden))
        drag.move(-3f)
        assertTrue(drag.allows(SheetValue.Hidden))
    }

    @Test fun theLastDeliberateDirectionWinsIncludingMultipleReversals() {
        drag.start(expanded = false, touchSlop = 8f)
        drag.move(240f)
        drag.move(-12f)
        drag.move(3f)
        assertFalse(drag.allows(SheetValue.Hidden))
        drag.move(9f)
        assertTrue(drag.allows(SheetValue.Hidden))
        drag.move(-12f)
        assertFalse(drag.allows(SheetValue.Hidden))
    }

    @Test fun smallJitterDoesNotEraseADevelopingReversal() {
        drag.start(expanded = false, touchSlop = 8f)
        drag.move(240f)
        drag.move(-5f)
        drag.move(1f)
        drag.move(-5f)
        assertFalse(drag.allows(SheetValue.Hidden))
    }

    @Test fun newBodyGesturesAndExplicitControlsCanDismissAfterAReversal() {
        drag.start(expanded = false, touchSlop = 8f)
        drag.move(240f)
        drag.move(-12f)
        drag.clear()
        drag.move(-12f)
        assertTrue(drag.allows(SheetValue.Hidden))
        drag.start(expanded = false, touchSlop = 8f)
        drag.move(240f)
        assertTrue(drag.allows(SheetValue.Hidden))
    }
}
