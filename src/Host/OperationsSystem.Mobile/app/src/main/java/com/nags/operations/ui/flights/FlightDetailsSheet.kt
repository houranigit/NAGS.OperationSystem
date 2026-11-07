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
import androidx.compose.material3.VerticalDivider
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
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.paneTitle
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.stateDescription
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.LayoutDirection
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.nags.operations.R
import com.nags.operations.data.FlightDetail
import com.nags.operations.ui.components.CustomerLogo
import com.nags.operations.ui.components.ErrorState
import com.nags.operations.ui.components.FlightAircraftOperationRow
import com.nags.operations.ui.components.InitialsAvatarStack
import com.nags.operations.ui.util.formatFlightDateRangeForDisplay
import com.nags.operations.ui.util.formatIsoTimeForDisplay
import kotlinx.coroutines.flow.Flow

private val DetailBlue = Color(0xFF007AFF)
private val DetailFill = Color(0xFFF5F7FA)
private val DetailEdge = Color(0xFFE7EBF0)

@OptIn(ExperimentalMaterial3Api::class)
@Composable
internal fun FlightDetailsSheet(
    state: FlightsUiState,
    onClose: () -> Unit,
    onRetry: () -> Unit,
    onInvite: () -> Unit,
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
            Box(Modifier.padding(top = 10.dp, bottom = 8.dp).size(width = 32.dp, height = 4.dp)
                .background(DetailEdge, CircleShape))
        },
    ) {
        key(state.selectedFlightId) {
            var staffExpanded by rememberSaveable { mutableStateOf(false) }
            var servicesExpanded by rememberSaveable { mutableStateOf(false) }
            Column(Modifier.fillMaxWidth().semantics { paneTitle = detailsTitle }) {
                Row(Modifier.fillMaxWidth().padding(start = 20.dp, end = 20.dp, top = 4.dp, bottom = 8.dp),
                    verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                    Text(detailsTitle, modifier = Modifier.weight(1f), fontSize = 20.sp, fontWeight = FontWeight.Bold,
                        maxLines = 1, overflow = TextOverflow.Ellipsis)
                    IconButton(onClick = {}, modifier = Modifier.size(44.dp).background(DetailFill, RoundedCornerShape(12.dp))) {
                        Icon(Icons.Outlined.Share, stringResource(R.string.flights_share), modifier = Modifier.size(21.dp))
                    }
                }
                DetailDivider(Modifier.padding(horizontal = 20.dp))
                Column(Modifier.fillMaxWidth().weight(1f, fill = false).verticalScroll(rememberScrollState())
                    .padding(start = 20.dp, end = 20.dp, top = 12.dp, bottom = 20.dp)) {
                    if (state.invitationSent) {
                        Text(stringResource(R.string.flights_invite_sent), modifier = Modifier.padding(bottom = 8.dp),
                            style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.primary)
                    }
                    when {
                        state.isLoadingDetail -> FlightDetailsSkeleton()
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
                            DetailPeopleRow(
                                title = stringResource(R.string.flights_assigned_staff), icon = Icons.Outlined.Group,
                                names = flight.assignedEmployees.map { it.fullName },
                                secondary = flight.assignedEmployees.map { it.employeeId },
                                emptyLabel = stringResource(R.string.flights_no_assigned_staff),
                                expanded = staffExpanded, onToggle = { staffExpanded = !staffExpanded },
                            )
                            DetailDivider()
                            DetailPeopleRow(
                                title = stringResource(R.string.flights_planned_services), icon = Icons.Outlined.Layers,
                                names = flight.plannedServices.map { it.name },
                                emptyLabel = stringResource(R.string.flights_no_planned_services),
                                expanded = servicesExpanded, onToggle = { servicesExpanded = !servicesExpanded },
                            )
                            DetailDivider()
                            Button(onClick = {}, modifier = Modifier.fillMaxWidth().padding(top = 12.dp).heightIn(min = 46.dp),
                                shape = RoundedCornerShape(14.dp),
                                colors = ButtonDefaults.buttonColors(containerColor = DetailBlue, contentColor = Color.White)) {
                                Text(stringResource(R.string.flights_manage), fontWeight = FontWeight.SemiBold)
                            }
                            if (state.canInviteEmployees && !flight.isPerLanding) {
                                TextButton(onClick = onInvite, enabled = state.isOnline && !flight.fromCache && !state.isInviting,
                                    modifier = Modifier.fillMaxWidth(), colors = ButtonDefaults.textButtonColors(contentColor = DetailBlue)) {
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

@Composable
private fun FlightIdentity(flight: FlightDetail, loadCustomerLogo: (String) -> Flow<ByteArray?>) {
    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(10.dp), verticalAlignment = Alignment.CenterVertically) {
        CustomerLogo(flight.customerId, loadCustomerLogo, Modifier.size(40.dp))
        VerticalDivider(Modifier.height(32.dp), color = DetailEdge)
        Column(Modifier.weight(1f)) {
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
                Row(Modifier.weight(1f), horizontalArrangement = Arrangement.spacedBy(6.dp), verticalAlignment = Alignment.CenterVertically) {
                    Text(flight.flightNumber.ifBlank { flight.customerIataCode?.takeIf { it.isNotBlank() } ?: "—" },
                        modifier = Modifier.weight(1f, fill = false), fontSize = 20.sp, fontWeight = FontWeight.Bold,
                        maxLines = 1, overflow = TextOverflow.Ellipsis)
                    if (flight.isPerLanding) {
                        Surface(Modifier.widthIn(max = 76.dp), shape = RoundedCornerShape(6.dp),
                            color = MaterialTheme.colorScheme.primaryContainer) {
                            Text(stringResource(R.string.flights_per_landing), Modifier.padding(horizontal = 6.dp, vertical = 3.dp),
                                style = MaterialTheme.typography.labelSmall.copy(fontSize = 10.sp),
                                maxLines = 1, overflow = TextOverflow.Ellipsis)
                        }
                    }
                }
                Surface(Modifier.widthIn(max = 92.dp), shape = RoundedCornerShape(50), color = DetailBlue.copy(alpha = 0.11f)) {
                    Text(stringResource(R.string.flights_scheduled), Modifier.padding(horizontal = 9.dp, vertical = 4.dp),
                        style = MaterialTheme.typography.labelSmall, fontWeight = FontWeight.SemiBold, color = DetailBlue,
                        maxLines = 1, overflow = TextOverflow.Ellipsis)
                }
            }
            Text(flight.customerName, modifier = Modifier.fillMaxWidth(), style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1, overflow = TextOverflow.Ellipsis)
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
        Text(stringResource(R.string.flights_scheduled_arrival), style = labelStyle,
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
    Column {
        Row(Modifier.fillMaxWidth().clickable(enabled = names.isNotEmpty(), role = Role.Button,
            onClickLabel = action, onClick = onToggle).semantics { stateDescription = disclosureState }.heightIn(min = 52.dp),
            verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            Icon(icon, null, tint = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.size(20.dp))
            Text(title, modifier = Modifier.weight(1f), style = MaterialTheme.typography.bodyMedium,
                fontWeight = FontWeight.SemiBold, maxLines = 1, overflow = TextOverflow.Ellipsis)
            if (names.isNotEmpty()) {
                InitialsAvatarStack(names, maxVisible = 3, avatarSize = 28.dp)
                Icon(Icons.AutoMirrored.Filled.KeyboardArrowRight, null, tint = MaterialTheme.colorScheme.onSurfaceVariant,
                    modifier = Modifier.size(20.dp).graphicsLayer { rotationZ = rotation })
            } else {
                Text(emptyLabel, modifier = Modifier.weight(1f), style = MaterialTheme.typography.bodySmall, textAlign = TextAlign.End,
                    color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1, overflow = TextOverflow.Ellipsis)
            }
        }
        AnimatedVisibility(visible = expanded && names.isNotEmpty()) {
            Column(Modifier.fillMaxWidth().padding(start = 30.dp, bottom = 12.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                names.forEachIndexed { index, name ->
                    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                        Box(Modifier.clearAndSetSemantics { }) {
                            InitialsAvatarStack(listOf(name), avatarSize = 28.dp)
                        }
                        Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                            Text(name, style = MaterialTheme.typography.bodySmall, maxLines = 1, overflow = TextOverflow.Ellipsis)
                            secondary.getOrNull(index)?.takeIf { it.isNotBlank() }?.let {
                                Text(it, style = MaterialTheme.typography.labelSmall,
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
private fun FlightDetailsSkeleton() {
    Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(8.dp)) {
        listOf(40.dp, 20.dp, 44.dp, 52.dp, 52.dp).forEach { height ->
            Box(Modifier.fillMaxWidth().height(height).background(DetailFill, RoundedCornerShape(12.dp)))
        }
    }
}
