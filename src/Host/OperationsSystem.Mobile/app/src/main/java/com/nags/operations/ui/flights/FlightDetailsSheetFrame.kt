package com.nags.operations.ui.flights

import androidx.compose.foundation.ScrollState
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.Orientation
import androidx.compose.foundation.gestures.scrollable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.WindowInsetsSides
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.only
import androidx.compose.foundation.layout.safeDrawing
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.windowInsetsPadding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Close
import androidx.compose.material3.BottomSheetScaffold
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.SheetValue
import androidx.compose.material3.rememberBottomSheetScaffoldState
import androidx.compose.material3.rememberStandardBottomSheetState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.snapshotFlow
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.luminance
import androidx.compose.ui.input.nestedscroll.NestedScrollConnection
import androidx.compose.ui.input.nestedscroll.nestedScroll
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalLayoutDirection
import androidx.compose.ui.platform.LocalView
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.collapse
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.expand
import androidx.compose.ui.semantics.dismiss
import androidx.compose.ui.semantics.paneTitle
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.stateDescription
import androidx.compose.ui.unit.LayoutDirection
import androidx.compose.ui.unit.Velocity
import androidx.compose.ui.unit.dp
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.window.DialogProperties
import androidx.compose.ui.window.DialogWindowProvider
import androidx.core.view.WindowCompat
import com.nags.operations.R
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.launch

/** A modal frame with compact and expanded native sheet anchors. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
internal fun FlightDetailsSheetFrame(
    onClose: () -> Unit,
    onBack: () -> Unit,
    content: @Composable (ScrollState) -> Unit,
) {
    val sheetState = rememberStandardBottomSheetState(initialValue = SheetValue.PartiallyExpanded, skipHiddenState = false)
    val scaffoldState = rememberBottomSheetScaffoldState(bottomSheetState = sheetState)
    val scroll = rememberScrollState()
    val scope = rememberCoroutineScope()
    val currentOnClose by rememberUpdatedState(onClose)
    var closeRequested by remember { mutableStateOf(false) }
    val closeOnce: () -> Unit = {
        if (!closeRequested) {
            closeRequested = true
            currentOnClose()
        }
    }
    val density = LocalDensity.current
    val expandedTopPx = with(density) { 24.dp.toPx() }
    val contentFlingBoundary = remember(scroll, sheetState, expandedTopPx) {
        object : NestedScrollConnection {
            override suspend fun onPostFling(consumed: Velocity, available: Velocity): Velocity {
                // A content fling stops with the first card visible. Only a further drag moves
                // the expanded sheet; drags that have already moved it keep native settling.
                val atExpandedAnchor = sheetState.requireOffset() <= expandedTopPx + 0.5f
                return if (available.y > 0f && scroll.value == 0 && atExpandedAnchor) {
                    Velocity(0f, available.y)
                } else Velocity.Zero
            }
        }
    }
    val isLight = MaterialTheme.colorScheme.surface.luminance() > 0.5f
    val ground = if (isLight) FlightFormGround else MaterialTheme.colorScheme.background
    val detailsTitle = stringResource(R.string.flights_details)
    val expanded = sheetState.targetValue == SheetValue.Expanded
    val toggleLabel = stringResource(if (expanded) R.string.flight_sheet_collapse else R.string.flight_sheet_expand)
    val stateLabel = stringResource(if (expanded) R.string.flights_section_expanded else R.string.flights_section_collapsed)
    val closeLabel = stringResource(R.string.flight_sheet_close)
    val toggleSheet: () -> Unit = {
        scope.launch {
            if (sheetState.targetValue == SheetValue.Expanded) sheetState.partialExpand() else sheetState.expand()
        }
    }

    Dialog(
        onDismissRequest = onBack,
        properties = DialogProperties(
            dismissOnClickOutside = false,
            usePlatformDefaultWidth = false,
            decorFitsSystemWindows = false,
        ),
    ) {
        val view = LocalView.current
        val window = (view.parent as? DialogWindowProvider)?.window
        LaunchedEffect(sheetState, window) {
            snapshotFlow { sheetState.currentValue == SheetValue.Hidden && !sheetState.isAnimationRunning }.first { it }
            // The native sheet already animated offscreen; remove its modal window too.
            window?.setWindowAnimations(0)
            window?.setDimAmount(0f)
            closeOnce()
        }
        DisposableEffect(window, isLight) {
            window?.setWindowAnimations(R.style.Animation_OperationsFlightSheet)
            window?.setDimAmount(0.28f)
            window?.let { WindowCompat.getInsetsController(it, view).isAppearanceLightStatusBars = isLight }
            onDispose { }
        }
        BoxWithConstraints(
            Modifier.fillMaxSize().windowInsetsPadding(
                WindowInsets.safeDrawing.only(WindowInsetsSides.Top + WindowInsetsSides.Horizontal),
            ),
        ) {
            val expandedHeight = (maxHeight - 24.dp).coerceAtLeast(0.dp)
            val peekHeight = (maxHeight * 0.77f).coerceAtMost(expandedHeight)
            BottomSheetScaffold(
                scaffoldState = scaffoldState,
                sheetPeekHeight = peekHeight,
                sheetShape = RoundedCornerShape(topStart = 28.dp, topEnd = 28.dp),
                sheetContainerColor = ground,
                sheetContentColor = MaterialTheme.colorScheme.onSurface,
                sheetTonalElevation = 0.dp,
                sheetShadowElevation = 0.dp,
                sheetDragHandle = null,
                containerColor = Color.Transparent,
                sheetContent = {
                    Column(
                        Modifier.fillMaxWidth().height(expandedHeight)
                            .nestedScroll(contentFlingBoundary)
                            .windowInsetsPadding(WindowInsets.safeDrawing.only(WindowInsetsSides.Bottom))
                            .semantics { paneTitle = detailsTitle },
                    ) {
                        // Drive the same content state from the stationary chrome so a downward
                        // drag reaches the content's top before native nested scroll moves the sheet.
                        CompositionLocalProvider(LocalLayoutDirection provides LayoutDirection.Ltr) {
                            Row(
                                Modifier.fillMaxWidth().height(48.dp)
                                    .scrollable(scroll, Orientation.Vertical, reverseDirection = true),
                                verticalAlignment = Alignment.CenterVertically,
                            ) {
                                Spacer(Modifier.width(48.dp))
                                Box(
                                    Modifier.weight(1f).height(48.dp)
                                        .clickable(
                                            role = Role.Button,
                                            onClickLabel = toggleLabel,
                                            interactionSource = remember { MutableInteractionSource() },
                                            indication = null,
                                            onClick = toggleSheet,
                                        )
                                        .semantics {
                                            contentDescription = toggleLabel
                                            stateDescription = stateLabel
                                            if (expanded) collapse(toggleLabel) { toggleSheet(); true }
                                            else expand(toggleLabel) { toggleSheet(); true }
                                            dismiss(closeLabel) { scope.launch { sheetState.hide() }; true }
                                        },
                                    contentAlignment = Alignment.Center,
                                ) {
                                    Box(Modifier.size(width = 34.dp, height = 4.dp)
                                        .background(MaterialTheme.colorScheme.onSurface.copy(alpha = 0.2f), CircleShape))
                                }
                                Box(
                                    Modifier.size(48.dp)
                                        .clickable(
                                            role = Role.Button,
                                            interactionSource = remember { MutableInteractionSource() },
                                            indication = null,
                                            onClick = closeOnce,
                                        )
                                        .semantics { contentDescription = closeLabel },
                                    contentAlignment = Alignment.Center,
                                ) {
                                    Box(Modifier.size(32.dp)
                                        .background(MaterialTheme.colorScheme.onSurface.copy(alpha = 0.06f), CircleShape),
                                        contentAlignment = Alignment.Center) {
                                        Icon(Icons.Outlined.Close, null, Modifier.size(18.dp))
                                    }
                                }
                            }
                        }
                        Box(Modifier.weight(1f).fillMaxWidth()) { content(scroll) }
                    }
                },
            ) {
                // The sheet Surface blocks taps from falling through to this background sibling.
                Box(Modifier.fillMaxSize().clickable(
                    interactionSource = remember { MutableInteractionSource() },
                    indication = null,
                    role = Role.Button,
                    onClickLabel = closeLabel,
                    onClick = closeOnce,
                ).semantics { contentDescription = closeLabel })
            }
        }
    }
}
