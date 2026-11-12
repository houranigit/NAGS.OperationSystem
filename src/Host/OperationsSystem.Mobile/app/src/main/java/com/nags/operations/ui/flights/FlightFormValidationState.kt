package com.nags.operations.ui.flights

import androidx.compose.foundation.relocation.BringIntoViewRequester
import androidx.compose.foundation.relocation.bringIntoViewRequester
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.SideEffect
import androidx.compose.runtime.remember
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.runtime.withFrameNanos
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.geometry.Rect
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.layout.onGloballyPositioned
import androidx.compose.ui.layout.positionInWindow

/** Form-local positions. Registering an error never moves the viewport or takes focus. */
internal class FlightFormValidationState {
    private val targets = mutableSetOf<FlightFormTarget>()
    internal var topInsetPx = 0f
    internal var navigationHeadPx = 48f
    internal var navigateToWindowTop: (suspend (Float) -> Unit)? = null

    internal fun register(target: FlightFormTarget) { targets.add(target) }
    internal fun unregister(target: FlightFormTarget) { targets.remove(target) }

    /** Call from the Save event after validation has entered composition. */
    suspend fun revealFirstError(): Boolean {
        val target = targets.asSequence().filter { it.message != null && it.bounds != null }
            .minWithOrNull(compareBy<FlightFormTarget> { it.bounds!!.top }.thenBy { it.bounds!!.left })
            ?: return false
        reveal(target, focus = true)
        return true
    }

    suspend fun revealTarget(key: String): Boolean {
        val target = targets.firstOrNull { it.navigationKey == key && it.bounds != null } ?: return false
        val navigate = navigateToWindowTop
        if (navigate != null) navigate(target.bounds!!.top) else reveal(target, focus = false)
        return true
    }

    private suspend fun reveal(target: FlightFormTarget, focus: Boolean) {
        if (focus && target.focusTargets > 0) {
            target.focusRequester.requestFocus()
            withFrameNanos { }
        }
        val bounds = target.bounds ?: return
        // Include the fixed form header above the row in the requested visible area.
        val bottom = if (target.navigationKey != null) minOf(bounds.height, navigationHeadPx) else bounds.height
        target.bringIntoViewRequester.bringIntoView(Rect(0f, -topInsetPx, bounds.width, bottom))
    }
}

internal class FlightFormTarget {
    val bringIntoViewRequester = BringIntoViewRequester()
    val focusRequester = FocusRequester()
    var bounds: Rect? = null
    var message: String? = null
    var navigationKey: String? = null
    var focusTargets = 0
}

internal val LocalFlightFormValidationState = staticCompositionLocalOf<FlightFormValidationState?> { null }
private val LocalFlightFormValidationTarget = staticCompositionLocalOf<FlightFormTarget?> { null }

@Composable
internal fun rememberFlightFormValidationState(): FlightFormValidationState = remember { FlightFormValidationState() }

@Composable
internal fun FlightFormValidationTarget(message: String?, registerError: Boolean, modifier: Modifier,
    content: @Composable (Modifier) -> Unit) {
    val state = LocalFlightFormValidationState.current
    val inheritedTarget = LocalFlightFormValidationTarget.current
    val target = remember(state) { FlightFormTarget() }
    SideEffect { target.message = message.takeIf { registerError } }
    DisposableEffect(state, target) {
        state?.register(target)
        onDispose { state?.unregister(target) }
    }
    // A child row without its own error can supply focus to its enclosing validation
    // (for example an optional End row inside the work-period validation wrapper).
    val focusTarget = if (message == null) inheritedTarget ?: target else target
    CompositionLocalProvider(LocalFlightFormValidationTarget provides focusTarget.takeIf { state != null }) {
        content(if (state == null) modifier else modifier.flightFormPosition(target))
    }
}

/** Attach to a choice control or editable field inside FlightFormValidation. */
@Composable
internal fun Modifier.flightFormValidationFocus(enabled: Boolean = true): Modifier {
    val target = LocalFlightFormValidationTarget.current
    DisposableEffect(target, enabled) {
        if (enabled && target != null) target.focusTargets++
        onDispose { if (enabled && target != null) target.focusTargets-- }
    }
    return if (enabled && target != null) focusRequester(target.focusRequester) else this
}

/** The same registry also exposes saved service/task card destinations. */
@Composable
internal fun Modifier.flightFormNavigationTarget(key: String): Modifier {
    val state = LocalFlightFormValidationState.current ?: return this
    val target = remember(state, key) { FlightFormTarget().apply { navigationKey = key } }
    DisposableEffect(state, target) {
        state.register(target)
        onDispose { state.unregister(target) }
    }
    return flightFormPosition(target)
}

private fun Modifier.flightFormPosition(target: FlightFormTarget): Modifier =
    bringIntoViewRequester(target.bringIntoViewRequester).onGloballyPositioned {
        // Unlike clipped bounds, this preserves the order of rows above the viewport.
        target.bounds = Rect(it.positionInWindow(), Size(it.size.width.toFloat(), it.size.height.toFloat()))
    }
