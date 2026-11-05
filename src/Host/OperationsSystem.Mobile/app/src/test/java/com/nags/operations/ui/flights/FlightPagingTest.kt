package com.nags.operations.ui.flights

import com.nags.operations.data.FlightCategory
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class FlightPagingTest {
    @Test
    fun eachCategoryPrefetchesNearTheEndOfItsOwnPage() {
        FlightCategory.entries.forEach { category ->
            val state = readyPage().copy(category = category)
            assertNull(nextFlightPageToLoad(state, selectedTab = category.ordinal,
                visibleItemCount = 20, lastVisibleItemIndex = 15))
            assertEquals(3, nextFlightPageToLoad(state, selectedTab = category.ordinal,
                visibleItemCount = 20, lastVisibleItemIndex = 16))
        }
    }

    @Test
    fun anExpiredEmptyPageContinuesToTheNextScopedPage() {
        val state = readyPage()
        assertEquals(3, nextFlightPageToLoad(state, selectedTab = 0,
            visibleItemCount = 0, lastVisibleItemIndex = -1))
        assertNull(nextFlightPageToLoad(state.copy(hasMore = false), selectedTab = 0,
            visibleItemCount = 0, lastVisibleItemIndex = -1))
    }

    @Test
    fun tabSwitchesLoadingAndErrorsDoNotStartDuplicateReads() {
        val ready = readyPage()
        assertNull(nextFlightPageToLoad(ready, selectedTab = 1,
            visibleItemCount = 0, lastVisibleItemIndex = -1))
        listOf(ready.copy(isLoading = true), ready.copy(isRefreshing = true),
            ready.copy(isLoadingMore = true), ready.copy(permissionDenied = true),
            ready.copy(error = "Access denied"), ready.copy(loadMoreError = "Connection lost")).forEach { state ->
            assertNull(nextFlightPageToLoad(state, selectedTab = 0,
                visibleItemCount = 0, lastVisibleItemIndex = -1))
        }
    }

    private fun readyPage() = FlightsUiState(page = 3, hasMore = true, isLoading = false)
}
