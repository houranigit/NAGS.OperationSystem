package com.nags.operations.ui.flights

import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.IntrinsicSize
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowForward
import androidx.compose.material.icons.automirrored.filled.KeyboardArrowRight
import androidx.compose.material.icons.outlined.CalendarMonth
import androidx.compose.material.icons.outlined.Group
import androidx.compose.material.icons.outlined.Layers
import androidx.compose.material.icons.outlined.LocationOn
import androidx.compose.material.icons.outlined.Share
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.key
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.layout.FirstBaseline
import androidx.compose.ui.platform.LocalLayoutDirection
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.ProgressBarRangeInfo
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.paneTitle
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.progressBarRangeInfo
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.stateDescription
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.LayoutDirection
import androidx.compose.ui.unit.dp
import com.nags.operations.R
import com.nags.operations.data.FlightDetail
import com.nags.operations.ui.components.CustomerLogo
import com.nags.operations.ui.components.ErrorState
import com.nags.operations.ui.components.FlightAircraftOperationRow
import com.nags.operations.ui.components.InitialsAvatarStack
import com.nags.operations.ui.components.PerLandingBadge
import com.nags.operations.ui.util.formatFlightDateRangeForDisplay
import com.nags.operations.ui.util.formatIsoTimeForDisplay
import kotlinx.coroutines.flow.Flow

private val DetailFill = Color(0xFFF5F7FA)
private val DetailEdge = Color(0xFFE7EBF0)
private val OperationsActionGreen = Color(0xFF198754)
private val CancellationActionRed = Color(0xFFDC3545)

@OptIn(ExperimentalMaterial3Api::class)
@Composable
internal fun FlightDetailsSheet(
    state: FlightsUiState,
    onClose: () -> Unit,
    onRetry: () -> Unit,
    onInvite: () -> Unit,
    onOperations: () -> Unit,
    onCanceled: () -> Unit,
    loadCustomerLogo: (String) -> Flow<ByteArray?>,
) {
    val detailsTitle = stringResource(R.string.flights_details)
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
            var staffExpanded by rememberSaveable { mutableStateOf(false) }
            var servicesExpanded by rememberSaveable { mutableStateOf(false) }
            val isPerLanding = state.detail?.isPerLanding
                ?: state.items.firstOrNull { it.id == state.selectedFlightId }?.isPerLanding ?: false
            Column(Modifier.fillMaxWidth().semantics { paneTitle = detailsTitle }) {
                Row(Modifier.fillMaxWidth().padding(horizontal = 20.dp),
                    verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    Text(detailsTitle, modifier = Modifier.weight(1f).semantics { heading() },
                        style = MaterialTheme.typography.titleSmall, fontWeight = FontWeight.SemiBold,
                        color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1, softWrap = false, overflow = TextOverflow.Ellipsis)
                    if (isPerLanding) PerLandingBadge()
                    IconButton(onClick = {}, modifier = Modifier.size(48.dp)) {
                        Box(Modifier.size(32.dp).background(DetailFill, RoundedCornerShape(10.dp)), contentAlignment = Alignment.Center) {
                            Icon(Icons.Outlined.Share, stringResource(R.string.flights_share), modifier = Modifier.size(18.dp))
                        }
                    }
                }
                Column(Modifier.fillMaxWidth().weight(1f, fill = false).verticalScroll(rememberScrollState())
                    .padding(start = 20.dp, end = 20.dp, top = 4.dp, bottom = 20.dp)) {
                    if (state.invitationSent) {
                        Text(stringResource(R.string.flights_invite_sent), modifier = Modifier.padding(bottom = 8.dp),
                            style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.primary)
                    }
                    when {
                        state.isLoadingDetail -> FlightDetailsSkeleton(showPeople = !isPerLanding)
                        state.detailError != null -> ErrorState(message = state.detailError, onRetry = onRetry)
                        state.detail != null -> {
                            val flight = state.detail
                            if (flight.fromCache) {
                                Text(stringResource(R.string.flights_cached_detail), modifier = Modifier.padding(bottom = 8.dp),
                                    style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                            }
                            FlightIdentity(flight, loadCustomerLogo)
                            Spacer(Modifier.height(8.dp))
                            FlightAircraftOperationRow(flight.aircraftTypeModel, flight.operationTypeName)
                            Spacer(Modifier.height(6.dp))
                            FlightSchedule(flight)
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
                            FlightRecordingActions(onOperations = onOperations, onCanceled = onCanceled)
                            if (state.canInviteEmployees && !flight.isPerLanding) {
                                TextButton(onClick = onInvite, enabled = state.isOnline && !flight.fromCache && !state.isInviting,
                                    modifier = Modifier.fillMaxWidth(), colors = ButtonDefaults.textButtonColors(contentColor = MaterialTheme.colorScheme.tertiary)) {
                                    Text(stringResource(R.string.flights_invite_employees), fontWeight = FontWeight.SemiBold)
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}

/** Both actions open local recording forms; saving remains unimplemented. */
@Composable
private fun FlightRecordingActions(loading: Boolean = false, onOperations: () -> Unit = {}, onCanceled: () -> Unit = {}) {
    // Keep cancellation on the physical left and operations on the right in both directions.
    val firstIsOperations = LocalLayoutDirection.current == LayoutDirection.Rtl
    Row(Modifier.fillMaxWidth().padding(top = 16.dp), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
        FlightRecordingAction(operations = firstIsOperations, loading = loading, modifier = Modifier.weight(1f), onOperations = onOperations, onCanceled = onCanceled)
        FlightRecordingAction(operations = !firstIsOperations, loading = loading, modifier = Modifier.weight(1f), onOperations = onOperations, onCanceled = onCanceled)
    }
}

@Composable
private fun FlightRecordingAction(operations: Boolean, loading: Boolean, modifier: Modifier, onOperations: () -> Unit, onCanceled: () -> Unit) {
    val shape = RoundedCornerShape(14.dp)
    if (loading) {
        Box(modifier.height(48.dp).background(DetailFill, shape))
    } else {
        Button(onClick = { if (operations) onOperations() else onCanceled() }, modifier = modifier.heightIn(min = 48.dp), shape = shape,
            contentPadding = PaddingValues(horizontal = 12.dp, vertical = 12.dp),
            colors = ButtonDefaults.buttonColors(
                containerColor = if (operations) OperationsActionGreen else CancellationActionRed,
                contentColor = Color.White,
            )) {
            Text(stringResource(if (operations) R.string.flights_operations else R.string.flights_canceled),
                fontWeight = FontWeight.SemiBold, maxLines = 1, softWrap = false, overflow = TextOverflow.Ellipsis)
        }
    }
}

@Composable
private fun FlightIdentity(flight: FlightDetail, loadCustomerLogo: (String) -> Flow<ByteArray?>) {
    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(10.dp), verticalAlignment = Alignment.CenterVertically) {
        CustomerLogo(flight.customerId, loadCustomerLogo, Modifier.size(40.dp))
        Column(Modifier.weight(1f)) {
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
                Text(flight.flightNumber.ifBlank { flight.customerIataCode?.takeIf { it.isNotBlank() } ?: "—" },
                    modifier = Modifier.weight(1f), style = MaterialTheme.typography.headlineMedium.copy(fontWeight = FontWeight.Bold, fontFeatureSettings = "tnum"),
                    maxLines = 1, softWrap = false, overflow = TextOverflow.Ellipsis)
                Surface(Modifier.widthIn(max = 92.dp), shape = RoundedCornerShape(50), color = MaterialTheme.colorScheme.tertiary.copy(alpha = 0.11f)) {
                    Text(stringResource(R.string.flights_scheduled), Modifier.padding(horizontal = 9.dp, vertical = 4.dp),
                        style = MaterialTheme.typography.labelSmall, fontWeight = FontWeight.SemiBold, color = MaterialTheme.colorScheme.tertiary,
                        maxLines = 1, softWrap = false, overflow = TextOverflow.Ellipsis)
                }
            }
            Text(flight.customerName, modifier = Modifier.fillMaxWidth(), style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1, softWrap = false, overflow = TextOverflow.Ellipsis)
        }
    }
}

@Composable
private fun FlightSchedule(flight: FlightDetail) {
    val labelStyle = MaterialTheme.typography.labelSmall
    val valueStyle = MaterialTheme.typography.titleLarge.copy(fontWeight = FontWeight.Bold, fontFeatureSettings = "tnum")
    val fontScale = LocalDensity.current.fontScale
    BoxWithConstraints(Modifier.fillMaxWidth().padding(vertical = 16.dp)) {
        if (maxWidth < 320.dp * fontScale) {
            Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(16.dp)) {
                FlightScheduleStation(flight, labelStyle, valueStyle)
                FlightScheduleTimeGroup(flight, labelStyle, valueStyle, Modifier.align(Alignment.End))
            }
        } else {
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(20.dp)) {
                FlightScheduleStation(flight, labelStyle, valueStyle, Modifier.weight(1f).alignBy(FirstBaseline))
                FlightScheduleTimeGroup(flight, labelStyle, valueStyle, Modifier.alignBy(FirstBaseline))
            }
        }
    }
}

@Composable
private fun FlightScheduleStation(flight: FlightDetail, labelStyle: TextStyle, valueStyle: TextStyle, modifier: Modifier = Modifier) {
    val stationName = flight.stationName.ifBlank { flight.stationIata }
    Column(modifier, verticalArrangement = Arrangement.spacedBy(4.dp)) {
        Text(stringResource(R.string.flights_schedule), style = labelStyle,
            color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1, overflow = TextOverflow.Ellipsis)
        Text(stationName, style = valueStyle, maxLines = 2, overflow = TextOverflow.Ellipsis)
        if (flight.stationIata.isNotBlank() && !flight.stationIata.equals(stationName, ignoreCase = true)) {
            Row(horizontalArrangement = Arrangement.spacedBy(4.dp), verticalAlignment = Alignment.CenterVertically) {
                Icon(Icons.Outlined.LocationOn, null, tint = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.size(14.dp))
                Text(flight.stationIata, style = MaterialTheme.typography.bodySmall, fontWeight = FontWeight.Medium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1, overflow = TextOverflow.Ellipsis)
            }
        }
    }
}

@Composable
private fun FlightScheduleTimeGroup(flight: FlightDetail, labelStyle: TextStyle, valueStyle: TextStyle, modifier: Modifier = Modifier) {
    Column(modifier, horizontalAlignment = Alignment.End, verticalArrangement = Arrangement.spacedBy(4.dp)) {
        FlightScheduleTimes(flight, labelStyle, valueStyle)
        Row(horizontalArrangement = Arrangement.spacedBy(4.dp), verticalAlignment = Alignment.CenterVertically) {
            Icon(Icons.Outlined.CalendarMonth, null, tint = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.size(14.dp))
            Text(formatFlightDateRangeForDisplay(flight.scheduledArrivalUtc, flight.scheduledDepartureUtc),
                style = MaterialTheme.typography.bodySmall, fontWeight = FontWeight.Medium, textAlign = TextAlign.End,
                color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1, overflow = TextOverflow.Ellipsis)
        }
    }
}

@Composable
private fun FlightScheduleTimes(flight: FlightDetail, labelStyle: TextStyle, timeStyle: TextStyle, modifier: Modifier = Modifier) {
    val timeGap = 8.dp
    val arrowSize = 18.dp
    // Both rows share their intrinsic width; the value row centers the arrow on the times.
    Column(modifier.width(IntrinsicSize.Min), verticalArrangement = Arrangement.spacedBy(4.dp)) {
        Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
            Text(stringResource(R.string.flights_card_sta), Modifier.weight(1f), style = labelStyle,
                color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1, overflow = TextOverflow.Ellipsis)
            Spacer(Modifier.width(arrowSize + timeGap * 2))
            Text(stringResource(R.string.flights_card_std), Modifier.weight(1f), style = labelStyle, textAlign = TextAlign.End,
                color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1, overflow = TextOverflow.Ellipsis)
        }
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(timeGap), verticalAlignment = Alignment.CenterVertically) {
            Text(formatIsoTimeForDisplay(flight.scheduledArrivalUtc), Modifier.weight(1f), style = timeStyle,
                maxLines = 1, overflow = TextOverflow.Ellipsis)
            Icon(Icons.AutoMirrored.Filled.ArrowForward, null, tint = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.size(arrowSize))
            Text(formatIsoTimeForDisplay(flight.scheduledDepartureUtc), Modifier.weight(1f), style = timeStyle, textAlign = TextAlign.End,
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
private fun FlightDetailsSkeleton(showPeople: Boolean) {
    Column(Modifier.fillMaxWidth().clearAndSetSemantics { progressBarRangeInfo = ProgressBarRangeInfo.Indeterminate }) {
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(10.dp), verticalAlignment = Alignment.CenterVertically) {
            SkeletonBlock(Modifier.size(40.dp))
            Column(Modifier.weight(1f)) {
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                    SkeletonBlock(Modifier.width(84.dp).height(30.dp))
                    SkeletonBlock(Modifier.width(72.dp).height(22.dp))
                }
                SkeletonBlock(Modifier.fillMaxWidth(0.75f).height(20.dp))
            }
        }
        Spacer(Modifier.height(8.dp))
        SkeletonBlock(Modifier.width(200.dp).height(16.dp))
        Spacer(Modifier.height(6.dp))
        Row(Modifier.fillMaxWidth().padding(vertical = 16.dp), horizontalArrangement = Arrangement.spacedBy(20.dp)) {
            Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                SkeletonBlock(Modifier.width(96.dp).height(12.dp))
                SkeletonBlock(Modifier.fillMaxWidth(0.8f).height(26.dp))
                SkeletonBlock(Modifier.width(44.dp).height(14.dp))
            }
            Column(Modifier.width(136.dp), horizontalAlignment = Alignment.End, verticalArrangement = Arrangement.spacedBy(6.dp)) {
                SkeletonBlock(Modifier.fillMaxWidth().height(12.dp))
                SkeletonBlock(Modifier.fillMaxWidth().height(26.dp))
                SkeletonBlock(Modifier.width(96.dp).height(14.dp))
            }
        }
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
        FlightRecordingActions(loading = true)
    }
}

@Composable
private fun SkeletonBlock(modifier: Modifier) {
    Box(modifier.background(DetailFill, RoundedCornerShape(6.dp)))
}
