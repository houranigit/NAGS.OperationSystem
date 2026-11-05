package com.nags.operations.ui.flights

/** Each tab pages its own server-filtered result. */
internal fun nextFlightPageToLoad(
    state: FlightsUiState,
    selectedTab: Int,
    visibleItemCount: Int,
    lastVisibleItemIndex: Int,
): Int? {
    if (selectedTab != state.category.ordinal || !state.hasMore || state.permissionDenied || state.isLoading ||
        state.isRefreshing || state.isLoadingMore || state.error != null || state.loadMoreError != null) return null
    return state.page.takeIf { visibleItemCount == 0 || lastVisibleItemIndex >= visibleItemCount - 4 }
}
