package com.nags.operations.ui.flights

import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.SheetValue

/** Only an intentional upward reversal of a header drag overrides native closing intent. */
@OptIn(ExperimentalMaterial3Api::class)
internal class FlightSheetDragReversal {
    private var tracking = false
    private var startedExpanded = false
    private var touchSlop = 0f
    private var position = 0f
    private var turningPoint = 0f
    private var direction = 0
    private var movedDown = false
    private var keepOpen = false

    fun start(expanded: Boolean, touchSlop: Float) {
        clear()
        tracking = true
        startedExpanded = expanded
        this.touchSlop = touchSlop
    }

    fun clear() {
        tracking = false
        startedExpanded = false
        touchSlop = 0f
        position = 0f
        turningPoint = 0f
        direction = 0
        movedDown = false
        keepOpen = false
    }

    fun move(deltaY: Float) {
        if (!tracking) return
        position += deltaY
        if (direction >= 0) {
            turningPoint = maxOf(turningPoint, position)
            if (turningPoint - position >= touchSlop) {
                direction = -1
                turningPoint = position
                keepOpen = movedDown
            } else if (direction == 0 && position >= touchSlop) {
                direction = 1
                movedDown = true
            }
        } else {
            turningPoint = minOf(turningPoint, position)
            if (position - turningPoint >= touchSlop) {
                direction = 1
                turningPoint = position
                movedDown = true
                keepOpen = false
            }
        }
    }

    fun allows(value: SheetValue): Boolean = !keepOpen || value == SheetValue.Expanded ||
        (value == SheetValue.PartiallyExpanded && !startedExpanded)
}
