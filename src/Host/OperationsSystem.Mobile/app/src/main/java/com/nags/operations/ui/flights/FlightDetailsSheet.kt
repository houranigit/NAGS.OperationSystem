package com.nags.operations.ui.flights

import androidx.activity.compose.BackHandler
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.core.spring
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.gestures.awaitEachGesture
import androidx.compose.foundation.gestures.awaitFirstDown
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.IntrinsicSize
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.wrapContentWidth
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.automirrored.outlined.Undo
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowForward
import androidx.compose.material.icons.automirrored.filled.KeyboardArrowRight
import androidx.compose.material.icons.outlined.CalendarMonth
import androidx.compose.material.icons.outlined.Group
import androidx.compose.material.icons.outlined.Layers
import androidx.compose.material.icons.outlined.LocationOn
import androidx.compose.material.icons.outlined.Share
import androidx.compose.material.icons.outlined.EventBusy
import androidx.compose.material.icons.outlined.Edit
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.getValue
import androidx.compose.runtime.key
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clipToBounds
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Rect
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.input.pointer.PointerEventPass
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.FirstBaseline
import androidx.compose.ui.layout.boundsInWindow
import androidx.compose.ui.layout.onGloballyPositioned
import androidx.compose.ui.layout.positionInWindow
import androidx.compose.ui.platform.LocalLayoutDirection
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.ProgressBarRangeInfo
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.paneTitle
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.progressBarRangeInfo
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.stateDescription
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.rememberTextMeasurer
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Constraints
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.LayoutDirection
import androidx.compose.ui.unit.IntOffset
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.zIndex
import com.nags.operations.R
import com.nags.operations.data.FlightDetail
import com.nags.operations.ui.components.CustomerLogo
import com.nags.operations.ui.components.ErrorState
import com.nags.operations.ui.components.InlineErrorBanner
import com.nags.operations.ui.components.InitialsAvatarStack
import com.nags.operations.ui.util.formatFlightDateRangeForDisplay
import com.nags.operations.ui.util.stationTimeZone
import com.nags.operations.ui.util.formatIsoTimeForDisplay
import kotlinx.coroutines.flow.Flow

private val DetailFill = Color(0xFFF5F7FA)
private val DetailEdge = Color(0xFFE7EBF0)

@OptIn(ExperimentalMaterial3Api::class)
@Composable
internal fun FlightDetailsSheet(
    state: FlightsUiState,
    actuals: FlightActualDetails?,
    progressStatus: FlightProgressStatus,
    showCancellation: Boolean,
    showOperations: Boolean,
    showRevertCancellation: Boolean,
    showReviewCancellation: Boolean,
    onRevertCancellation: () -> Unit,
    onClose: () -> Unit,
    onRetry: () -> Unit,
    onInvite: () -> Unit,
    onOperations: () -> Unit,
    onCanceled: () -> Unit,
    loadCustomerLogo: (String) -> Flow<ByteArray?>,
) {
    val detailsTitle = stringResource(R.string.flights_details)
    val availableActions = buildList {
        if (showCancellation) add(FlightRecordingAction.Cancel)
        if (showReviewCancellation) add(FlightRecordingAction.Review)
        if (showRevertCancellation) add(FlightRecordingAction.Revert)
        add(FlightRecordingAction.Send)
        if (showOperations) add(FlightRecordingAction.Operate)
    }
    val actionMetrics = flightActionMetrics()
    var expandedAction by rememberSaveable(state.selectedFlightId) { mutableStateOf<FlightRecordingAction?>(null) }
    androidx.compose.runtime.LaunchedEffect(availableActions) {
        if (expandedAction !in availableActions) expandedAction = null
    }
    var contentOrigin by remember(state.selectedFlightId) { mutableStateOf(Offset.Zero) }
    var expandedActionBounds by remember(state.selectedFlightId) { mutableStateOf<Rect?>(null) }
    ModalBottomSheet(
        onDismissRequest = onClose,
        sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true),
        shape = RoundedCornerShape(topStart = 28.dp, topEnd = 28.dp),
        containerColor = MaterialTheme.colorScheme.surface,
        tonalElevation = 0.dp,
        scrimColor = Color.Black.copy(alpha = 0.28f),
        dragHandle = {
            Box(Modifier.padding(top = 8.dp, bottom = 4.dp).size(width = 32.dp, height = 4.dp)
                .background(DetailEdge, CircleShape))
        },
    ) {
        key(state.selectedFlightId) {
            BackHandler(enabled = expandedAction != null) { expandedAction = null; expandedActionBounds = null }
            var staffExpanded by rememberSaveable { mutableStateOf(false) }
            var servicesExpanded by rememberSaveable { mutableStateOf(false) }
            val isPerLanding = state.detail?.isPerLanding
                ?: state.items.firstOrNull { it.id == state.selectedFlightId }?.isPerLanding ?: false
            val actions: @Composable (Boolean) -> Unit = { loading ->
                FlightRecordingActions(loading = loading, availableActions = availableActions, metrics = actionMetrics, expandedAction = expandedAction,
                    onExpandedActionChange = { expandedAction = it; expandedActionBounds = null },
                    onExpandedBoundsChange = { expandedActionBounds = it },
                    onOperations = onOperations, onCanceled = onCanceled, onRevertCancellation = onRevertCancellation)
            }
            Column(Modifier.fillMaxWidth().onGloballyPositioned { contentOrigin = it.positionInWindow() }
                .pointerInput(expandedAction) {
                    awaitEachGesture {
                        val down = awaitFirstDown(requireUnconsumed = false, pass = PointerEventPass.Initial)
                        val outsideAction = expandedAction != null && expandedActionBounds?.contains(down.position + contentOrigin) == false
                        var isTap = true
                        do {
                            // Observe after children; their clicks and scrolling stay intact.
                            val event = awaitPointerEvent(PointerEventPass.Final)
                            if (event.changes.size > 1 || event.changes.any {
                                (it.position - down.position).getDistance() > viewConfiguration.touchSlop
                            }) isTap = false
                        } while (event.changes.any { it.pressed })
                        if (isTap && outsideAction) { expandedAction = null; expandedActionBounds = null }
                    }
                }.semantics { paneTitle = detailsTitle }) {
                Column(Modifier.fillMaxWidth().weight(1f, fill = false).verticalScroll(rememberScrollState())
                    .padding(start = 20.dp, end = 20.dp, top = 4.dp, bottom = 20.dp)) {
                    if (state.invitationSent) {
                        Text(stringResource(R.string.flights_invite_sent), modifier = Modifier.padding(bottom = 8.dp),
                            style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.primary)
                    }
                    when {
                        state.isLoadingDetail && state.detail == null -> FlightDetailsSkeleton(showPeople = !isPerLanding, metrics = actionMetrics, actions = { actions(true) })
                        state.detailError != null && state.detail == null -> ErrorState(message = state.detailError, onRetry = onRetry)
                        state.detail != null -> {
                            val flight = state.detail
                            if (flight.fromCache) {
                                Text(stringResource(R.string.flights_cached_detail), modifier = Modifier.padding(bottom = 8.dp),
                                    style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                            }
                            FlightIdentity(flight, loadCustomerLogo, metrics = actionMetrics, actions = { actions(false) })
                            Spacer(Modifier.height(8.dp))
                            FlightOperationStatusRow(flight.operationTypeName, progressStatus)
                            DetailDivider(Modifier.padding(vertical = 10.dp))
                            FlightSchedule(flight)
                            CompositionLocalProvider(LocalLayoutDirection provides LayoutDirection.Ltr) {
                                FlightActualSummary(actuals, stationTimeZone(flight.stationTimeZoneId))
                            }
                            Spacer(Modifier.height(10.dp))
                            DetailDivider()
                            if (!flight.isPerLanding) {
                                DetailPeopleRow(
                                    title = stringResource(R.string.flights_planned_services), icon = Icons.Outlined.Layers,
                                    names = flight.plannedServices.map { it.name },
                                    emptyLabel = stringResource(R.string.flights_no_planned_services),
                                    expanded = servicesExpanded, onToggle = { servicesExpanded = !servicesExpanded },
                                )
                                DetailDivider()
                                DetailPeopleRow(
                                    title = stringResource(R.string.flights_assigned_staff), icon = Icons.Outlined.Group,
                                    names = flight.assignedEmployees.map { it.fullName },
                                    secondary = flight.assignedEmployees.map { it.employeeId },
                                    emptyLabel = stringResource(R.string.flights_no_assigned_staff),
                                    expanded = staffExpanded, onToggle = { staffExpanded = !staffExpanded },
                                )
                                DetailDivider()
                            }
                            if (state.canInviteEmployees && !flight.isPerLanding) {
                                TextButton(onClick = onInvite, enabled = state.isOnline && !flight.fromCache && !state.isInviting && !state.isLoadingDetail,
                                    modifier = Modifier.fillMaxWidth(), colors = ButtonDefaults.textButtonColors(contentColor = MaterialTheme.colorScheme.tertiary)) {
                                    Text(stringResource(R.string.flights_invite_employees), fontWeight = FontWeight.SemiBold)
                                }
                            }
                            state.detailError?.let { error ->
                                InlineErrorBanner(message = error, onRetry = onRetry,
                                    modifier = Modifier.padding(top = 8.dp))
                            }
                        }
                    }
                }
            }
        }
    }
}

private data class FlightActionMetrics(
    val headingHeight: Dp,
    val expandedFaceHeight: Dp,
    val touchHeight: Dp,
    val bottomInset: Dp,
    val slotWidth: Dp = 48.dp,
    val toolbarWidth: Dp = 144.dp,
    val collapsedFaceSize: Dp = 44.dp,
)

@Composable
private fun flightActionMetrics(): FlightActionMetrics {
    val measurer = rememberTextMeasurer()
    val density = LocalDensity.current
    val labelHeight = measurer.measure(stringResource(R.string.flights_operate),
        style = MaterialTheme.typography.labelMedium.copy(fontWeight = FontWeight.SemiBold), maxLines = 1).size.height
    val headingLayout = measurer.measure("0", style = MaterialTheme.typography.headlineMedium.copy(fontWeight = FontWeight.Bold), maxLines = 1)
    val heading = with(density) { headingLayout.size.height.toDp() }
    val inset = with(density) { (headingLayout.size.height - headingLayout.lastBaseline).toDp() }.coerceAtLeast(0.dp)
    val face = maxOf(44.dp, with(density) { labelHeight.toDp() } + 8.dp)
    return FlightActionMetrics(heading, face, maxOf(48.dp, face + inset + 2.dp), inset)
}

private enum class FlightRecordingAction { Cancel, Review, Revert, Send, Operate }

/** First tap reveals one action; a second tap opens its existing flow. */
@Composable
private fun FlightRecordingActions(
    loading: Boolean,
    availableActions: List<FlightRecordingAction>,
    metrics: FlightActionMetrics,
    expandedAction: FlightRecordingAction?,
    onExpandedActionChange: (FlightRecordingAction?) -> Unit,
    onExpandedBoundsChange: (Rect) -> Unit,
    onOperations: () -> Unit,
    onCanceled: () -> Unit,
    onRevertCancellation: () -> Unit,
) {
    val style = MaterialTheme.typography.labelMedium.copy(fontWeight = FontWeight.SemiBold)
    val density = LocalDensity.current
    val measurer = rememberTextMeasurer()
    val collapsedWidth = metrics.slotWidth * availableActions.size
    // Slots stay mounted so each action expands continuously from its current position.
    val expansions = availableActions.map { action ->
        key(action) { animateFloatAsState(
            targetValue = if (expandedAction == action) 1f else 0f,
            animationSpec = spring(dampingRatio = 1f, stiffness = 400f),
            label = "${action.name} expansion",
        ).value }
    }
    CompositionLocalProvider(LocalLayoutDirection provides LayoutDirection.Ltr) {
        Box(Modifier.width(metrics.toolbarWidth).height(metrics.touchHeight)) {
            availableActions.forEachIndexed { index, action ->
                key(action) {
                    val expansion = expansions[index]
                    val expanded = expandedAction == action
                    val otherExpansion = expansions.filterIndexed { other, _ -> other != index }.maxOrNull() ?: 0f
                    val opacity = (1f - otherExpansion * 3f).coerceIn(0f, 1f)
                    val enabled = !loading && (expanded || expandedAction == null && opacity > 0.5f)
                    val buttonWidth = metrics.slotWidth + (metrics.toolbarWidth - metrics.slotWidth) * expansion
                    val faceWidth = buttonWidth - (metrics.slotWidth - metrics.collapsedFaceSize)
                    val faceHeight = metrics.collapsedFaceSize + (metrics.expandedFaceHeight - metrics.collapsedFaceSize) * expansion
                    val accessibleLabel = stringResource(when (action) {
                        FlightRecordingAction.Cancel -> R.string.flights_cancel_flight
                        FlightRecordingAction.Review -> R.string.cancellation_review
                        FlightRecordingAction.Revert -> R.string.cancellation_revert
                        FlightRecordingAction.Send -> R.string.flights_send
                        FlightRecordingAction.Operate -> R.string.flights_operate
                    })
                    val label = when (action) {
                        FlightRecordingAction.Revert -> stringResource(R.string.cancellation_revert_short)
                        FlightRecordingAction.Review -> stringResource(R.string.cancellation_review_short)
                        else -> accessibleLabel
                    }
                    val icon = when (action) {
                        FlightRecordingAction.Cancel -> Icons.Outlined.EventBusy
                        FlightRecordingAction.Review -> Icons.Outlined.Edit
                        FlightRecordingAction.Revert -> Icons.AutoMirrored.Outlined.Undo
                        FlightRecordingAction.Send -> Icons.Outlined.Share
                        FlightRecordingAction.Operate -> Icons.Outlined.Edit
                    }
                    val labelLimit = metrics.toolbarWidth - (metrics.slotWidth - metrics.collapsedFaceSize) - 16.dp - 16.dp - 6.dp
                    val labelWidth = with(density) {
                        measurer.measure(label, style = style, maxLines = 1, softWrap = false, overflow = TextOverflow.Ellipsis,
                            constraints = Constraints(maxWidth = labelLimit.roundToPx().coerceAtLeast(0))).size.width.toDp()
                    }
                    val interactions = remember(action) { MutableInteractionSource() }
                    Box(Modifier
                        .offset { IntOffset(((metrics.toolbarWidth - collapsedWidth + metrics.slotWidth * index) * (1f - expansion)).roundToPx(), 0) }
                        .width(buttonWidth).height(metrics.touchHeight)
                        .zIndex(if (expanded) 2f else if (expansion > 0f) 1f else 0f)
                        .graphicsLayer { alpha = opacity }
                        .onGloballyPositioned { if (expanded) onExpandedBoundsChange(it.boundsInWindow()) }
                        .clickable(enabled = enabled, role = Role.Button, interactionSource = interactions, indication = null) {
                            if (expanded) {
                                onExpandedActionChange(null)
                                when (action) {
                                    FlightRecordingAction.Cancel, FlightRecordingAction.Review -> onCanceled()
                                    FlightRecordingAction.Revert -> onRevertCancellation()
                                    FlightRecordingAction.Send -> Unit
                                    FlightRecordingAction.Operate -> onOperations()
                                }
                            } else onExpandedActionChange(action)
                        }
                        .semantics { contentDescription = accessibleLabel }
                        .then(if (!enabled) Modifier.clearAndSetSemantics { } else Modifier)) {
                        Box(Modifier.width(faceWidth).height(faceHeight).align(Alignment.BottomCenter)
                            .offset(y = -metrics.bottomInset).background(DetailFill, CircleShape), contentAlignment = Alignment.Center) {
                            if (!loading) Row(Modifier.padding(horizontal = 8.dp).clearAndSetSemantics { },
                                verticalAlignment = Alignment.CenterVertically) {
                                Icon(icon, null, Modifier.size(16.dp))
                                Spacer(Modifier.width(6.dp * expansion))
                                Box(Modifier.width(labelWidth * expansion).clipToBounds().graphicsLayer { alpha = expansion }) {
                                    Text(label, Modifier.wrapContentWidth(Alignment.Start, unbounded = true).width(labelWidth),
                                        style = style, maxLines = 1, softWrap = false, overflow = TextOverflow.Ellipsis)
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun FlightIdentity(flight: FlightDetail, loadCustomerLogo: (String) -> Flow<ByteArray?>,
    metrics: FlightActionMetrics, actions: @Composable () -> Unit) {
    CompositionLocalProvider(LocalLayoutDirection provides LayoutDirection.Ltr) {
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(10.dp), verticalAlignment = Alignment.CenterVertically) {
            CustomerLogo(flight.customerId, loadCustomerLogo, Modifier.size(40.dp))
            Column(Modifier.weight(1f)) {
                Row(Modifier.fillMaxWidth().heightIn(min = metrics.touchHeight), horizontalArrangement = Arrangement.spacedBy(8.dp),
                    verticalAlignment = Alignment.Bottom) {
                    Text(flight.flightNumber.ifBlank { flight.customerIataCode?.takeIf { it.isNotBlank() } ?: "—" },
                        Modifier.weight(1f), style = MaterialTheme.typography.headlineMedium.copy(fontWeight = FontWeight.Bold, fontFeatureSettings = "tnum"),
                        maxLines = 1, softWrap = false, overflow = TextOverflow.Ellipsis)
                    actions()
                }
                Text(flight.customerName, Modifier.fillMaxWidth(), style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1, softWrap = false, overflow = TextOverflow.Ellipsis)
            }
        }
    }
}

@Composable
private fun FlightSchedule(flight: FlightDetail) {
    val labelStyle = MaterialTheme.typography.labelSmall
    val valueStyle = MaterialTheme.typography.titleLarge.copy(fontSize = 18.sp, lineHeight = 22.sp,
        fontWeight = FontWeight.Bold, fontFeatureSettings = "tnum")
    val fontScale = LocalDensity.current.fontScale
    BoxWithConstraints(Modifier.fillMaxWidth()) {
        if (maxWidth < 320.dp * fontScale) {
            Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                FlightScheduleAircraft(flight, labelStyle, valueStyle)
                FlightScheduleTimeGroup(flight, labelStyle, valueStyle, Modifier.align(Alignment.End), captionStyle = labelStyle)
            }
        } else {
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(16.dp)) {
                FlightScheduleAircraft(flight, labelStyle, valueStyle, Modifier.weight(1f).alignBy(FirstBaseline))
                FlightScheduleTimeGroup(flight, labelStyle, valueStyle, Modifier.alignBy(FirstBaseline), captionStyle = labelStyle)
            }
        }
    }
}

@Composable
private fun FlightScheduleAircraft(flight: FlightDetail, labelStyle: TextStyle, valueStyle: TextStyle, modifier: Modifier = Modifier) {
    Column(modifier, verticalArrangement = Arrangement.spacedBy(4.dp)) {
        Text(stringResource(R.string.flights_schedule), style = labelStyle,
            color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1, overflow = TextOverflow.Ellipsis)
        Text(flight.aircraftTypeModel?.takeIf { it.isNotBlank() } ?: stringResource(R.string.flights_card_aircraft_unavailable),
            style = valueStyle, maxLines = 2, overflow = TextOverflow.Ellipsis)
        Row(horizontalArrangement = Arrangement.spacedBy(4.dp), verticalAlignment = Alignment.CenterVertically) {
            Icon(Icons.Outlined.LocationOn, null, tint = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.size(14.dp))
            Text(flight.stationName.ifBlank { stringResource(R.string.flights_card_aircraft_unavailable) }, Modifier.weight(1f), style = labelStyle,
                color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 2, overflow = TextOverflow.Ellipsis)
        }
    }
}

@Composable
internal fun FlightScheduleTimeGroup(flight: FlightDetail, labelStyle: TextStyle, valueStyle: TextStyle, modifier: Modifier = Modifier,
    captionStyle: TextStyle = MaterialTheme.typography.bodySmall.copy(fontWeight = FontWeight.Medium),
) {
    Column(modifier.width(IntrinsicSize.Max), horizontalAlignment = Alignment.End, verticalArrangement = Arrangement.spacedBy(4.dp)) {
        FlightScheduleTimes(flight, labelStyle, valueStyle)
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(4.dp, Alignment.End), verticalAlignment = Alignment.CenterVertically) {
            Icon(Icons.Outlined.CalendarMonth, null, tint = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.size(14.dp))
            Text(formatFlightDateRangeForDisplay(flight.scheduledArrivalUtc, flight.scheduledDepartureUtc, stationTimeZone(flight.stationTimeZoneId)),
                style = captionStyle, textAlign = TextAlign.End,
                color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1, overflow = TextOverflow.Ellipsis)
        }
    }
}

@Composable
private fun FlightScheduleTimes(flight: FlightDetail, labelStyle: TextStyle, timeStyle: TextStyle, modifier: Modifier = Modifier) {
    val timeGap = 8.dp
    val arrowSize = 18.dp
    // All schedule rows share the outer group's width; the arrow stays centered on the times.
    Column(modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(4.dp)) {
        Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
            Text(stringResource(R.string.flights_card_sta), Modifier.weight(1f), style = labelStyle,
                color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1, overflow = TextOverflow.Ellipsis)
            Spacer(Modifier.width(arrowSize + timeGap * 2))
            Text(stringResource(R.string.flights_card_std), Modifier.weight(1f), style = labelStyle, textAlign = TextAlign.End,
                color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1, overflow = TextOverflow.Ellipsis)
        }
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(timeGap), verticalAlignment = Alignment.CenterVertically) {
            Text(formatIsoTimeForDisplay(flight.scheduledArrivalUtc, stationTimeZone(flight.stationTimeZoneId)), Modifier.weight(1f), style = timeStyle,
                maxLines = 1, overflow = TextOverflow.Ellipsis)
            Icon(Icons.AutoMirrored.Filled.ArrowForward, null, tint = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.size(arrowSize))
            Text(formatIsoTimeForDisplay(flight.scheduledDepartureUtc, stationTimeZone(flight.stationTimeZoneId)), Modifier.weight(1f), style = timeStyle, textAlign = TextAlign.End,
                maxLines = 1, overflow = TextOverflow.Ellipsis)
        }
    }
}

@Composable
private fun DetailDivider(modifier: Modifier = Modifier) {
    HorizontalDivider(modifier, color = DetailEdge)
}

@Composable
private fun DetailPeopleRow(
    title: String,
    icon: ImageVector,
    names: List<String>,
    emptyLabel: String,
    expanded: Boolean,
    onToggle: () -> Unit,
    secondary: List<String> = emptyList(),
) {
    val action = stringResource(if (expanded) R.string.flights_hide_details else R.string.flights_show_details)
    val disclosureState = stringResource(if (expanded) R.string.flights_section_expanded else R.string.flights_section_collapsed)
    val expandedRotation = if (LocalLayoutDirection.current == LayoutDirection.Rtl) -90f else 90f
    val rotation by animateFloatAsState(if (expanded) expandedRotation else 0f, label = "Details disclosure")
    val disclosure = if (names.isNotEmpty()) Modifier.clickable(role = Role.Button, onClickLabel = action, onClick = onToggle)
        .semantics { stateDescription = disclosureState } else Modifier
    Column {
        Row(Modifier.fillMaxWidth().heightIn(min = 56.dp).then(disclosure).padding(vertical = 8.dp),
            verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            Icon(icon, null, tint = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.size(20.dp))
            Text(title, modifier = Modifier.weight(1f), style = MaterialTheme.typography.bodyMedium,
                fontWeight = FontWeight.SemiBold, maxLines = 2, overflow = TextOverflow.Ellipsis)
            if (names.isEmpty()) {
                Text(emptyLabel, modifier = Modifier.weight(1f), style = MaterialTheme.typography.bodySmall,
                    textAlign = TextAlign.End, color = MaterialTheme.colorScheme.onSurfaceVariant,
                    maxLines = 2, overflow = TextOverflow.Ellipsis)
            }
            if (names.isNotEmpty()) {
                if (!expanded) InitialsAvatarStack(names, maxVisible = 3, avatarSize = 28.dp)
                Icon(Icons.AutoMirrored.Filled.KeyboardArrowRight, null, tint = MaterialTheme.colorScheme.onSurfaceVariant,
                    modifier = Modifier.size(20.dp).graphicsLayer { rotationZ = rotation })
            }
        }
        AnimatedVisibility(visible = expanded && names.isNotEmpty()) {
            Column(Modifier.fillMaxWidth().padding(start = 30.dp, bottom = 12.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                names.forEachIndexed { index, name ->
                    Row(Modifier.fillMaxWidth().heightIn(min = 40.dp), verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                        Box(Modifier.clearAndSetSemantics { }) {
                            InitialsAvatarStack(listOf(name), avatarSize = 28.dp, colorOffset = index)
                        }
                        Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                            Text(name, style = MaterialTheme.typography.bodyMedium, maxLines = 1, overflow = TextOverflow.Ellipsis)
                            secondary.getOrNull(index)?.takeIf { it.isNotBlank() }?.let {
                                Text(it, style = MaterialTheme.typography.bodySmall,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1, overflow = TextOverflow.Ellipsis)
                            }
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun FlightDetailsSkeleton(showPeople: Boolean, metrics: FlightActionMetrics, actions: @Composable () -> Unit) {
    Column(Modifier.fillMaxWidth().clearAndSetSemantics { progressBarRangeInfo = ProgressBarRangeInfo.Indeterminate }) {
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(10.dp), verticalAlignment = Alignment.CenterVertically) {
            SkeletonBlock(Modifier.size(40.dp))
            Column(Modifier.weight(1f)) {
                Row(Modifier.fillMaxWidth().heightIn(min = metrics.touchHeight), horizontalArrangement = Arrangement.spacedBy(8.dp),
                    verticalAlignment = Alignment.Bottom) {
                    SkeletonBlock(Modifier.weight(1f).height(metrics.headingHeight))
                    actions()
                }
                SkeletonBlock(Modifier.fillMaxWidth(0.75f).height(20.dp))
            }
        }
        Spacer(Modifier.height(8.dp))
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
            SkeletonBlock(Modifier.width(156.dp).height(16.dp))
            SkeletonBlock(Modifier.width(104.dp).height(22.dp))
        }
        DetailDivider(Modifier.padding(vertical = 10.dp))
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(16.dp)) {
            Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                SkeletonBlock(Modifier.width(96.dp).height(12.dp))
                SkeletonBlock(Modifier.fillMaxWidth(0.8f).height(22.dp))
                SkeletonBlock(Modifier.fillMaxWidth(0.8f).height(14.dp))
            }
            Column(Modifier.width(136.dp), horizontalAlignment = Alignment.End, verticalArrangement = Arrangement.spacedBy(4.dp)) {
                SkeletonBlock(Modifier.fillMaxWidth().height(12.dp))
                SkeletonBlock(Modifier.fillMaxWidth().height(22.dp))
                SkeletonBlock(Modifier.width(96.dp).height(14.dp))
            }
        }
        Spacer(Modifier.height(10.dp))
        repeat(if (showPeople) 2 else 0) {
            DetailDivider()
            Row(Modifier.fillMaxWidth().height(56.dp), horizontalArrangement = Arrangement.spacedBy(10.dp),
                verticalAlignment = Alignment.CenterVertically) {
                SkeletonBlock(Modifier.size(20.dp))
                SkeletonBlock(Modifier.weight(1f).height(16.dp))
                Row(horizontalArrangement = Arrangement.spacedBy((-8).dp)) {
                    repeat(3) { Box(Modifier.size(28.dp).background(DetailFill, CircleShape)) }
                }
            }
        }
        DetailDivider()
    }
}

@Composable
private fun SkeletonBlock(modifier: Modifier) {
    Box(modifier.background(DetailFill, RoundedCornerShape(6.dp)))
}
