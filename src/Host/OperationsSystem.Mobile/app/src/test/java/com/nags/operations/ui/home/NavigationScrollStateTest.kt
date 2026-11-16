package com.nags.operations.ui.home

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class NavigationScrollStateTest {
    @Test fun aNewDestinationStartsExpanded() {
        assertFalse(NavigationScrollState().isCompact)
    }

    @Test fun downwardMovementMustAccumulateSixDpBeforeCollapsing() {
        var state = NavigationScrollState().onConsumedScroll(-2f).onConsumedScroll(-3f)
        assertFalse(state.isCompact)
        state = state.onConsumedScroll(-1f)
        assertTrue(state.isCompact)
    }

    @Test fun upwardReversalExpandsAfterTheSameThreshold() {
        var state = NavigationScrollState().onConsumedScroll(-6f)
            .onConsumedScroll(2f).onConsumedScroll(3f)
        assertTrue(state.isCompact)
        state = state.onConsumedScroll(1f)
        assertFalse(state.isCompact)
    }

    @Test fun oppositeDirectionDiscardsPartialMovementInsteadOfCombiningJitter() {
        var state = NavigationScrollState().onConsumedScroll(-5f)
            .onConsumedScroll(2f).onConsumedScroll(-1f)
        assertFalse(state.isCompact)
        state = state.onConsumedScroll(-4f)
        assertFalse(state.isCompact)
        state = state.onConsumedScroll(-1f)
        assertTrue(state.isCompact)
    }

    @Test fun aLongDownwardFlingDoesNotRequireAnEquallyLongUpwardReversal() {
        val compact = NavigationScrollState().onConsumedScroll(-5_000f)
        assertTrue(compact.isCompact)
        assertEquals(compact, compact.onConsumedScroll(-5_000f))
        assertFalse(compact.onConsumedScroll(6f).isCompact)
    }

    @Test fun zeroAndInvalidDeltasDoNotResetAnInProgressDirection() {
        val state = NavigationScrollState().onConsumedScroll(-4f)
        assertEquals(state, state.onConsumedScroll(0f))
        assertEquals(state, state.onConsumedScroll(Float.NaN))
        assertEquals(state, state.onConsumedScroll(Float.POSITIVE_INFINITY))
        assertEquals(state, state.onConsumedScroll(Float.NEGATIVE_INFINITY))
        assertTrue(state.onConsumedScroll(0f).onConsumedScroll(-2f).isCompact)
    }
}
