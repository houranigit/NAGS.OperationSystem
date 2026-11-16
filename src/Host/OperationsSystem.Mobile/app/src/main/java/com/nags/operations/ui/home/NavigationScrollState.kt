package com.nags.operations.ui.home

private const val NAVIGATION_DIRECTION_THRESHOLD_DP = 6f

/** A short deliberate reversal changes the target; long drags do not build up reversal debt. */
internal data class NavigationScrollState(
    val isCompact: Boolean = false,
    private val directionDistanceDp: Float = 0f,
) {
    fun onConsumedScroll(consumedYDp: Float): NavigationScrollState {
        if (!consumedYDp.isFinite() || consumedYDp == 0f) return this
        val continuesDirection = (consumedYDp > 0f) == (directionDistanceDp > 0f)
        val distance = ((if (continuesDirection) directionDistanceDp else 0f) + consumedYDp)
            .coerceIn(-NAVIGATION_DIRECTION_THRESHOLD_DP, NAVIGATION_DIRECTION_THRESHOLD_DP)
        val compact = when {
            distance <= -NAVIGATION_DIRECTION_THRESHOLD_DP -> true
            distance >= NAVIGATION_DIRECTION_THRESHOLD_DP -> false
            else -> isCompact
        }
        return copy(isCompact = compact, directionDistanceDp = distance)
    }
}
