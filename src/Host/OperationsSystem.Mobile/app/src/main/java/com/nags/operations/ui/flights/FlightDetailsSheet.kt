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
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.fillMaxSize
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
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import com.nags.operations.ui.components.InlineNotice
import androidx.compose.material3.TextButton
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
import com.nags.operations.data.FlightListItem
import com.nags.operations.ui.components.CustomerLogo
import com.nags.operations.ui.components.FlightAircraftOperationRow
import com.nags.operations.ui.components.FlightReportBadge
import com.nags.operations.ui.components.flightReportLabel
import com.nags.operations.ui.components.flightReportBadgeTextStyle
import com.nags.operations.ui.components.ErrorState
import com.nags.operations.ui.components.InlineErrorBanner
import com.nags.operations.ui.components.InitialsAvatarStack
import com.nags.operations.ui.theme.AccentBlue
import com.nags.operations.ui.theme.AccentRed
import com.nags.operations.ui.util.formatFlightDateRangeForDisplay
import com.nags.operations.ui.util.stationTimeZone
import com.nags.operations.ui.util.formatIsoTimeForDisplay
import kotlinx.coroutines.flow.Flow

private val DetailFill = Color(0xFFF5F7FA)
private val DetailEdge = Color(0xFFE7EBF0)

@Composable
internal fun FlightDetailsSheet(
    state: FlightsUiState,
    actuals: FlightActualDetails?,
    reportStatus: FlightCardReportStatus,
    listItem: FlightListItem?,
    workItems: List<FlightSheetWorkItem>,
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
    FlightDetailsSheetFrame(onClose = onClose, onBack = {
        if (expandedAction != null) { expandedAction = null; expandedActionBounds = null }
        else onClose()
    }) { contentScroll ->
        key(state.selectedFlightId) {
            BackHandler(enabled = expandedAction != null) { expandedAction = null; expandedActionBounds = null }
            var staffExpanded by rememberSaveable { mutableStateOf(false) }
            var servicesExpanded by rememberSaveable { mutableStateOf(false) }
            val isPerLanding = state.detail?.isPerLanding
                ?: state.items.firstOrNull { it.id == state.selectedFlightId }?.isPerLanding ?: false
            val sharedStationFlight = isPerLanding || state.detail?.isAdHocFlight == true ||
                state.items.any { it.id == state.selectedFlightId && (it.isAdHocFlight || it.isMyAdHocFlight) }
            val actions: @Composable (Boolean) -> Unit = { loading ->
                FlightRecordingActions(loading = loading, availableActions = availableActions, metrics = actionMetrics, expandedAction = expandedAction,
                    onExpandedActionChange = { expandedAction = it; expandedActionBounds = null },
                    onExpandedBoundsChange = { expandedActionBounds = it },
                    onOperations = onOperations, onCanceled = onCanceled, onRevertCancellation = onRevertCancellation)
            }
            Column(Modifier.fillMaxSize().onGloballyPositioned { contentOrigin = it.positionInWindow() }
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
                Column(Modifier.fillMaxWidth().weight(1f).verticalScroll(contentScroll)
                    .padding(start = 12.dp, end = 12.dp, bottom = 20.dp),
                    verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    if (state.invitationSent) {
                        InlineNotice(stringResource(R.string.flights_invite_sent))
                    }
                    when {
                        state.isLoadingDetail && state.detail == null -> FlightDetailsSkeleton(showPeople = !sharedStationFlight, metrics = actionMetrics, actions = { actions(true) })
                        state.detailError != null && state.detail == null -> ErrorState(message = state.detailError, onRetry = onRetry)
                        state.detail != null -> {
                            val flight = state.detail
                            if (flight.fromCache) {
                                InlineNotice(stringResource(R.string.flights_cached_detail))
                            }
                            FlightSheetGroup {
                                FlightIdentity(flight, reportStatus, listItem, loadCustomerLogo, metrics = actionMetrics, actions = { actions(false) })
                            }
                            FlightSheetGroup {
                                FlightSchedule(flight)
                                CompositionLocalProvider(LocalLayoutDirection provides LayoutDirection.Ltr) {
                                    if (actuals != null) FlightActualSummary(actuals, stationTimeZone(flight.stationTimeZoneId))
                                    else {
                                        DetailDivider(Modifier.padding(vertical = 12.dp))
                                        Text(stringResource(R.string.flight_actual_details), style = MaterialTheme.typography.labelSmall,
                                            color = MaterialTheme.colorScheme.onSurfaceVariant)
                                        Text(stringResource(R.string.flight_sheet_no_actuals), Modifier.padding(top = 6.dp),
                                            style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                                    }
                                }
                            }
                            FlightSheetWork(flight.id, workItems, stationTimeZone(flight.stationTimeZoneId))
                            if (!flight.isPerLanding && !flight.isAdHocFlight) FlightSheetGroup {
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
                                if (state.canInviteEmployees) {
                                    DetailDivider()
                                    TextButton(onClick = onInvite, enabled = state.isOnline && !flight.fromCache && !state.isInviting && !state.isLoadingDetail,
                                        modifier = Modifier.fillMaxWidth(), colors = ButtonDefaults.textButtonColors(contentColor = MaterialTheme.colorScheme.tertiary)) {
                                        Text(stringResource(R.string.flights_invite_employees), fontWeight = FontWeight.SemiBold)
                                    }
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
    val slotWidth: Dp = 40.dp,
    val toolbarWidth: Dp = 128.dp,
    val collapsedFaceSize: Dp = 36.dp,
    val expandedFaceWidth: Dp = 128.dp,
    val iconSize: Dp = 20.dp,
)

@Composable
private fun flightActionMetrics(): FlightActionMetrics {
    val measurer = rememberTextMeasurer()
    val density = LocalDensity.current
    val labelHeight = measurer.measure(stringResource(R.string.flights_operate),
        style = MaterialTheme.typography.labelMedium.copy(fontWeight = FontWeight.SemiBold), maxLines = 1).size.height
    val headingHeight = measurer.measure("0", style = MaterialTheme.typography.headlineMedium.copy(fontWeight = FontWeight.Bold), maxLines = 1).size.height
    val heading = with(density) { headingHeight.toDp() }
    val face = maxOf(32.dp, with(density) { labelHeight.toDp() } + 8.dp)
    return FlightActionMetrics(heading, face, maxOf(48.dp, maxOf(36.dp, face) + 4.dp))
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
                    val faceWidth = metrics.collapsedFaceSize + (metrics.expandedFaceWidth - metrics.collapsedFaceSize) * expansion
                    val faceHeight = metrics.collapsedFaceSize + (metrics.expandedFaceHeight - metrics.collapsedFaceSize) * expansion
                    val accent = when {
                        expanded && action == FlightRecordingAction.Cancel -> AccentRed
                        expanded && action == FlightRecordingAction.Send -> AccentBlue
                        else -> null
                    }
                    val containerColor = accent ?: DetailFill
                    val contentColor = if (accent != null) Color.White else MaterialTheme.colorScheme.onSurface
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
                    val labelLimit = metrics.expandedFaceWidth - 16.dp - metrics.iconSize - 6.dp
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
                        // Align the painted edge, not an invisible touch-slot inset, with the sheet gutter.
                        Box(Modifier.width(faceWidth).height(faceHeight).align(Alignment.TopEnd)
                            .offset(y = 2.dp)
                            .background(if (loading) DetailFill else containerColor, CircleShape), contentAlignment = Alignment.Center) {
                            if (!loading) Row(Modifier.padding(horizontal = 8.dp).clearAndSetSemantics { },
                                verticalAlignment = Alignment.CenterVertically) {
                                Icon(icon, null, Modifier.size(metrics.iconSize), tint = contentColor)
                                Spacer(Modifier.width(6.dp * expansion))
                                Box(Modifier.width(labelWidth * expansion).clipToBounds().graphicsLayer { alpha = expansion }) {
                                    Text(label, Modifier.wrapContentWidth(Alignment.Start, unbounded = true).width(labelWidth),
                                        style = style, color = contentColor, maxLines = 1, softWrap = false, overflow = TextOverflow.Ellipsis)
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
private fun FlightSheetGroup(content: @Composable ColumnScope.() -> Unit) {
    Surface(Modifier.fillMaxWidth(), shape = RoundedCornerShape(16.dp), color = MaterialTheme.colorScheme.surface) {
        Column(Modifier.padding(12.dp), content = content)
    }
}

@Composable
private fun FlightIdentity(flight: FlightDetail, reportStatus: FlightCardReportStatus, listItem: FlightListItem?,
    loadCustomerLogo: (String) -> Flow<ByteArray?>, metrics: FlightActionMetrics, actions: @Composable () -> Unit) {
    val headingStyle = MaterialTheme.typography.headlineSmall.copy(fontSize = 22.sp, lineHeight = 26.sp,
        fontWeight = FontWeight.Bold, fontFeatureSettings = "tnum")
    val badgeStyle = flightReportBadgeTextStyle(compact = true)
    val flightNumber = flight.flightNumber.ifBlank { flight.customerIataCode?.takeIf { it.isNotBlank() } ?: "—" }
    val badgeLabel = flightReportLabel(reportStatus)
    val density = LocalDensity.current
    val measurer = rememberTextMeasurer()
    CompositionLocalProvider(LocalLayoutDirection provides LayoutDirection.Ltr) {
        Column {
            BoxWithConstraints(Modifier.fillMaxWidth()) {
                val heading = measurer.measure(flightNumber, headingStyle, maxLines = 1, softWrap = false)
                val badge = measurer.measure(badgeLabel, badgeStyle, maxLines = 1, softWrap = false)
                val identityHeight = with(density) { maxOf(48.dp, heading.size.height.toDp() + 2.dp + badge.size.height.toDp() + 4.dp) }
                val iconWidth = if (reportStatus == FlightCardReportStatus.Conflict || reportStatus == FlightCardReportStatus.ReturnedToRamp) 13.dp else 0.dp
                val copyWidth = with(density) { maxOf(heading.size.width.toDp(), badge.size.width.toDp() + 12.dp + iconWidth) }
                val stacked = maxWidth < identityHeight + 10.dp + copyWidth + 8.dp + metrics.toolbarWidth
                Column {
                    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(10.dp), verticalAlignment = Alignment.Top) {
                        CustomerLogo(flight.customerId, loadCustomerLogo, Modifier.size(identityHeight))
                        Column(Modifier.weight(1f).height(identityHeight), verticalArrangement = Arrangement.spacedBy(2.dp),
                            horizontalAlignment = Alignment.Start) {
                            Text(flightNumber, Modifier.fillMaxWidth(), style = headingStyle, maxLines = 1,
                                softWrap = false, overflow = TextOverflow.Ellipsis)
                            FlightReportBadge(reportStatus, compact = true)
                        }
                        if (!stacked) actions()
                    }
                    if (stacked) Box(Modifier.fillMaxWidth().padding(top = 4.dp), contentAlignment = Alignment.CenterEnd) { actions() }
                }
            }
            Text(flight.customerName, Modifier.fillMaxWidth().padding(top = 4.dp),
                style = MaterialTheme.typography.bodySmall.copy(fontWeight = FontWeight.Bold),
                maxLines = 2, overflow = TextOverflow.Ellipsis)
            Box(Modifier.padding(top = 2.dp)) {
                FlightAircraftOperationRow(flight.aircraftTypeModel, flight.operationTypeName, flight.isPerLanding,
                    listItem?.latestReturnToRampSequence, listItem?.hasOpenReturnToRamp == true)
            }
        }
    }
}

@Composable
private fun FlightSchedule(flight: FlightDetail) {
    val labelStyle = MaterialTheme.typography.labelSmall
    val valueStyle = MaterialTheme.typography.titleLarge.copy(fontSize = 18.sp, lineHeight = 22.sp,
        fontWeight = FontWeight.Bold, fontFeatureSettings = "tnum")
    BoxWithConstraints(Modifier.fillMaxWidth()) {
        val timeGroupMaxWidth = maxWidth * .56f
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            FlightScheduleAircraft(flight, labelStyle, valueStyle, Modifier.weight(1f).alignBy(FirstBaseline))
            FlightScheduleTimeGroup(flight, labelStyle, valueStyle,
                Modifier.widthIn(max = timeGroupMaxWidth).alignBy(FirstBaseline), captionStyle = labelStyle)
        }
    }
}

@Composable
private fun FlightScheduleAircraft(flight: FlightDetail, labelStyle: TextStyle, valueStyle: TextStyle, modifier: Modifier = Modifier) {
    Column(modifier, verticalArrangement = Arrangement.spacedBy(4.dp)) {
        Text(stringResource(R.string.flights_schedule), style = labelStyle,
            color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1, softWrap = false, overflow = TextOverflow.Ellipsis)
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
            Text(title, modifier = Modifier.weight(1f), style = MaterialTheme.typography.bodySmall,
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
    Column(Modifier.fillMaxWidth().clearAndSetSemantics { progressBarRangeInfo = ProgressBarRangeInfo.Indeterminate },
        verticalArrangement = Arrangement.spacedBy(8.dp)) {
        FlightSheetGroup {
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(10.dp), verticalAlignment = Alignment.Top) {
                SkeletonBlock(Modifier.size(48.dp))
                Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                    SkeletonBlock(Modifier.fillMaxWidth().height(24.dp))
                    SkeletonBlock(Modifier.width(72.dp).height(20.dp))
                }
                actions()
            }
            SkeletonBlock(Modifier.padding(top = 4.dp).fillMaxWidth(.65f).height(16.dp))
            SkeletonBlock(Modifier.padding(top = 2.dp).fillMaxWidth(.85f).height(16.dp))
        }
        FlightSheetGroup {
            repeat(2) { index ->
                if (index > 0) DetailDivider(Modifier.padding(vertical = 12.dp))
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(16.dp)) {
                    Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                        SkeletonBlock(Modifier.width(96.dp).height(12.dp))
                        SkeletonBlock(Modifier.fillMaxWidth(.8f).height(22.dp))
                        SkeletonBlock(Modifier.fillMaxWidth(.8f).height(14.dp))
                    }
                    Column(Modifier.width(120.dp), horizontalAlignment = Alignment.End, verticalArrangement = Arrangement.spacedBy(4.dp)) {
                        SkeletonBlock(Modifier.fillMaxWidth().height(12.dp))
                        SkeletonBlock(Modifier.fillMaxWidth().height(22.dp))
                        SkeletonBlock(Modifier.width(96.dp).height(14.dp))
                    }
                }
            }
        }
        FlightSheetGroup {
            SkeletonBlock(Modifier.width(80.dp).height(20.dp))
            SkeletonBlock(Modifier.padding(top = 12.dp).fillMaxWidth(.7f).height(16.dp))
        }
        if (showPeople) FlightSheetGroup {
            repeat(2) { index ->
                if (index > 0) DetailDivider()
                Row(Modifier.fillMaxWidth().height(56.dp), horizontalArrangement = Arrangement.spacedBy(10.dp),
                    verticalAlignment = Alignment.CenterVertically) {
                    SkeletonBlock(Modifier.size(20.dp))
                    SkeletonBlock(Modifier.weight(1f).height(16.dp))
                    Row(horizontalArrangement = Arrangement.spacedBy((-8).dp)) {
                        repeat(3) { Box(Modifier.size(28.dp).background(DetailFill, CircleShape)) }
                    }
                }
            }
        }
    }
}

@Composable
private fun SkeletonBlock(modifier: Modifier) {
    Box(modifier.background(DetailFill, RoundedCornerShape(6.dp)))
}
