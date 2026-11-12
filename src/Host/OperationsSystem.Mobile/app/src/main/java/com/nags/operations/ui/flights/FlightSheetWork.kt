package com.nags.operations.ui.flights

import androidx.compose.animation.AnimatedVisibility
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowForward
import androidx.compose.material.icons.automirrored.filled.KeyboardArrowRight
import androidx.compose.material.icons.automirrored.outlined.Assignment
import androidx.compose.material.icons.outlined.AccessTime
import androidx.compose.material.icons.outlined.Build
import androidx.compose.material.icons.outlined.CalendarMonth
import androidx.compose.material.icons.outlined.CheckCircleOutline
import androidx.compose.material.icons.outlined.Groups
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
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
import androidx.compose.ui.graphics.luminance
import androidx.compose.ui.platform.LocalLayoutDirection
import androidx.compose.ui.res.pluralStringResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.stateDescription
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.LayoutDirection
import androidx.compose.ui.unit.sp
import com.nags.operations.R
import com.nags.operations.data.FlightDetail
import com.nags.operations.data.db.entities.FlightServicePendingEntity
import com.nags.operations.data.db.entities.FlightTaskPendingEntity
import com.nags.operations.ui.util.formatFlightDateRangeForDisplay
import com.nags.operations.ui.util.formatIsoTimeForDisplay
import com.nags.operations.ui.util.parseInUserZone
import com.nags.operations.ui.util.parseOffsetDateTime
import com.nags.operations.ui.util.stationTimeZone
import java.time.Instant
import java.time.ZoneId

internal enum class FlightSheetWorkKind { Service, MajorTask, MinorTask, Task }
internal enum class FlightSheetWorkSync { Pending, NeedsReview }

internal data class FlightSheetWorkItem(
    val key: String,
    val kind: FlightSheetWorkKind,
    val name: String,
    val startUtc: String,
    val endUtc: String?,
    val performerCount: Int,
    val sync: FlightSheetWorkSync? = null,
    val ataCode: String = "",
    val ataTitle: String = "",
)

/** Uses the same published/desired records as the form, without adding any local or planned drafts. */
internal fun flightSheetWorkItems(
    flight: FlightDetail,
    pendingServices: List<FlightServicePendingEntity>,
    pendingTasks: List<FlightTaskPendingEntity>,
    catalogs: FlightOperationsCatalogs,
): List<FlightSheetWorkItem> {
    val zone = stationTimeZone(flight.stationTimeZoneId)
    val services = pendingServices.filter { it.flightId == flight.id }
    val tasks = pendingTasks.filter { it.flightId == flight.id }
    fun sync(status: String?) = status?.let {
        if (it == "pending") FlightSheetWorkSync.Pending else FlightSheetWorkSync.NeedsReview
    }
    val serviceItems = initialServiceEditorDraft(flight, catalogs, services, null, zone).services
        .filter { it.period.start.isNotBlank() }.map { item ->
            FlightSheetWorkItem("service:${item.id}", FlightSheetWorkKind.Service,
                item.serviceName.ifBlank { flight.plannedServices.firstOrNull { it.serviceId.equals(item.serviceId, true) }?.name.orEmpty() },
                item.period.originalStartUtc.orEmpty(), item.period.originalEndUtc,
                item.employees.map { it.employeeId.lowercase() }.distinct().size,
                sync(services.firstOrNull { it.serviceRecordId == item.id }?.status))
        }
    val taskItems = initialTaskEditorDraft(flight, catalogs, tasks, null, zone)
        .filter { it.period.start.isNotBlank() }.map { item ->
            FlightSheetWorkItem("task:${item.id}", when (item.taskType) {
                "Major" -> FlightSheetWorkKind.MajorTask
                "Minor" -> FlightSheetWorkKind.MinorTask
                else -> FlightSheetWorkKind.Task
            }, "", item.period.originalStartUtc.orEmpty(), item.period.originalEndUtc,
                item.employees.map { it.employeeId.lowercase() }.distinct().size,
                sync(tasks.firstOrNull { it.taskRecordId == item.id }?.status), item.ataChapterCode, item.ataChapterTitle)
        }
    return (serviceItems + taskItems).sortedWith(compareBy<FlightSheetWorkItem> {
        runCatching { parseOffsetDateTime(it.startUtc).toInstant() }.getOrDefault(Instant.MAX)
    }.thenBy { it.key })
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
internal fun FlightSheetWork(
    flightId: String,
    items: List<FlightSheetWorkItem>,
    zone: ZoneId,
    modifier: Modifier = Modifier,
) {
    var closedExpanded by rememberSaveable(flightId) { mutableStateOf(false) }
    val active = items.filter { it.endUtc.isNullOrBlank() }
    val closed = items.filter { !it.endUtc.isNullOrBlank() }
    val muted = MaterialTheme.colorScheme.onSurfaceVariant
    val closedColor = if (MaterialTheme.colorScheme.surface.luminance() < .5f) Color(0xFFA3DFB7) else Color(0xFF176A3A)
    val closedRotation = if (LocalLayoutDirection.current == LayoutDirection.Rtl) -90f else 90f
    Surface(modifier.fillMaxWidth(), shape = RoundedCornerShape(16.dp), color = MaterialTheme.colorScheme.surface) {
        Column {
            FlowRow(Modifier.fillMaxWidth().padding(horizontal = 14.dp, vertical = 12.dp),
                verticalArrangement = Arrangement.spacedBy(4.dp), horizontalArrangement = Arrangement.SpaceBetween) {
                Text(stringResource(R.string.sheet_work_title), Modifier.semantics { heading() },
                    style = MaterialTheme.typography.titleMedium.copy(fontSize = 16.sp), fontWeight = FontWeight.Bold)
                Text(stringResource(R.string.sheet_work_summary, active.size, closed.size),
                    style = MaterialTheme.typography.labelSmall, color = muted)
            }
            if (items.isEmpty()) {
                Text(stringResource(R.string.flight_card_no_work), Modifier.padding(start = 14.dp, end = 14.dp, bottom = 14.dp),
                    style = MaterialTheme.typography.bodySmall, color = muted)
            } else {
                if (active.isEmpty()) {
                    Text(stringResource(R.string.sheet_work_no_active), Modifier.padding(start = 14.dp, end = 14.dp, bottom = 12.dp),
                        style = MaterialTheme.typography.bodySmall, color = muted)
                } else {
                    Row(Modifier.padding(start = 14.dp, end = 14.dp, bottom = 4.dp),
                        verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(7.dp)) {
                        Icon(Icons.Outlined.AccessTime, null, Modifier.size(16.dp), tint = MaterialTheme.colorScheme.tertiary)
                        Text(stringResource(R.string.sheet_work_active, active.size), style = MaterialTheme.typography.bodySmall,
                            fontWeight = FontWeight.SemiBold, color = MaterialTheme.colorScheme.tertiary)
                    }
                    active.forEachIndexed { index, item ->
                        if (index > 0) WorkDivider()
                        key(item.key) { WorkSummaryRow(item, zone) }
                    }
                }
                WorkDivider()
                val disclosureState = stringResource(if (closedExpanded) R.string.flights_section_expanded else R.string.flights_section_collapsed)
                Row(Modifier.fillMaxWidth().heightIn(min = 48.dp)
                    .clickable(enabled = closed.isNotEmpty(), role = Role.Button) { closedExpanded = !closedExpanded }
                    .semantics { stateDescription = disclosureState }.padding(horizontal = 14.dp, vertical = 12.dp),
                    verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    Icon(Icons.Outlined.CheckCircleOutline, null, Modifier.size(17.dp), tint = closedColor)
                    Text(stringResource(R.string.sheet_work_closed, closed.size), Modifier.weight(1f),
                        style = MaterialTheme.typography.bodySmall, fontWeight = FontWeight.SemiBold, color = closedColor)
                    if (closed.isNotEmpty()) Icon(Icons.AutoMirrored.Filled.KeyboardArrowRight, null,
                        Modifier.size(18.dp).graphicsLayer { rotationZ = if (closedExpanded) closedRotation else 0f }, tint = muted)
                }
                AnimatedVisibility(closedExpanded && closed.isNotEmpty()) {
                    Column {
                        closed.forEach { item ->
                            WorkDivider()
                            key(item.key) { WorkSummaryRow(item, zone) }
                        }
                    }
                }
            }
        }
    }
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun WorkSummaryRow(item: FlightSheetWorkItem, zone: ZoneId) {
    val muted = MaterialTheme.colorScheme.onSurfaceVariant
    val spansDates = item.endUtc?.let { end -> runCatching {
        parseInUserZone(item.startUtc, zone).toLocalDate() != parseInUserZone(end, zone).toLocalDate()
    }.getOrDefault(false) } == true
    val type = stringResource(when (item.kind) {
        FlightSheetWorkKind.Service -> R.string.sheet_work_service
        FlightSheetWorkKind.MajorTask -> R.string.sheet_work_major_task
        FlightSheetWorkKind.MinorTask -> R.string.sheet_work_minor_task
        FlightSheetWorkKind.Task -> R.string.work_task
    })
    val name = if (item.kind == FlightSheetWorkKind.Service) item.name.ifBlank { type } else when {
        item.ataCode.isNotBlank() && item.ataTitle.isNotBlank() -> stringResource(R.string.sheet_work_ata, item.ataCode, item.ataTitle)
        item.ataCode.isNotBlank() -> stringResource(R.string.sheet_work_ata_code, item.ataCode)
        item.ataTitle.isNotBlank() -> item.ataTitle
        else -> stringResource(R.string.work_task)
    }
    Column(Modifier.fillMaxWidth().padding(horizontal = 14.dp, vertical = 12.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(10.dp), verticalAlignment = Alignment.Top) {
            Icon(if (item.kind == FlightSheetWorkKind.Service) Icons.Outlined.Build else Icons.AutoMirrored.Outlined.Assignment,
                null, Modifier.padding(top = 2.dp).size(17.dp), tint = muted)
            Text(name, Modifier.weight(1f), style = MaterialTheme.typography.bodySmall.copy(fontSize = 13.sp),
                fontWeight = FontWeight.SemiBold, maxLines = 2, overflow = TextOverflow.Ellipsis)
            Surface(shape = RoundedCornerShape(5.dp), color = MaterialTheme.colorScheme.onSurface.copy(alpha = .06f)) {
                Text(type, Modifier.widthIn(max = 100.dp).padding(horizontal = 5.dp, vertical = 2.dp),
                    style = MaterialTheme.typography.labelSmall, color = muted, maxLines = 2, overflow = TextOverflow.Ellipsis)
            }
        }
        FlowRow(Modifier.fillMaxWidth().padding(start = 27.dp), horizontalArrangement = Arrangement.spacedBy(12.dp),
            verticalArrangement = Arrangement.spacedBy(4.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(4.dp)) {
                Text(formatIsoTimeForDisplay(item.startUtc, zone),
                    style = MaterialTheme.typography.labelSmall, color = muted)
                Icon(Icons.AutoMirrored.Filled.ArrowForward, null, Modifier.size(13.dp), tint = muted)
                Text(item.endUtc?.let { formatIsoTimeForDisplay(it, zone) }
                    ?: stringResource(R.string.sheet_work_ongoing), style = MaterialTheme.typography.labelSmall,
                    color = if (item.endUtc == null) MaterialTheme.colorScheme.tertiary else muted)
            }
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(4.dp)) {
                Icon(Icons.Outlined.Groups, null, Modifier.size(14.dp), tint = muted)
                Text(pluralStringResource(R.plurals.sheet_work_employees, item.performerCount, item.performerCount),
                    style = MaterialTheme.typography.labelSmall, color = muted)
            }
            item.sync?.let {
                Text(stringResource(if (it == FlightSheetWorkSync.Pending) R.string.flight_card_pending_sync else R.string.flight_card_needs_review),
                    style = MaterialTheme.typography.labelSmall,
                    color = if (it == FlightSheetWorkSync.NeedsReview) MaterialTheme.colorScheme.error else muted)
            }
        }
        if (spansDates) Row(Modifier.fillMaxWidth().padding(start = 27.dp),
            verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(4.dp)) {
            Icon(Icons.Outlined.CalendarMonth, null, Modifier.size(13.dp), tint = muted)
            Text(formatFlightDateRangeForDisplay(item.startUtc, checkNotNull(item.endUtc), zone), Modifier.weight(1f),
                style = MaterialTheme.typography.labelSmall, color = muted)
        }
    }
}

@Composable
private fun WorkDivider() {
    HorizontalDivider(thickness = .5.dp, color = MaterialTheme.colorScheme.onSurface.copy(alpha = .06f))
}
