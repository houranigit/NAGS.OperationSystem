package com.nags.operations.ui.flights

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowForward
import androidx.compose.material.icons.outlined.CalendarMonth
import androidx.compose.material.icons.outlined.Engineering
import androidx.compose.material.icons.outlined.FlightLand
import androidx.compose.material.icons.outlined.FlightTakeoff
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.key
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalLayoutDirection
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.LayoutDirection
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.luminance
import com.nags.operations.R
import com.nags.operations.data.FlightDetail
import com.nags.operations.data.FlightReturnToRampRecord
import com.nags.operations.ui.util.formatFlightDateRangeForDisplay
import com.nags.operations.ui.util.formatIsoTimeForDisplay
import com.nags.operations.ui.util.parseOffsetDateTime
import com.nags.operations.ui.util.stationTimeZone
import java.time.Instant
import java.time.ZoneId

/** Each occurrence keeps its actuals and work in the same group as normal recorded work. */
@Composable
internal fun FlightSheetReturnsToRamp(flight: FlightDetail) {
    val zone = stationTimeZone(flight.stationTimeZoneId)
    flight.returnToRamps.sortedBy { it.sequence }.forEach { occurrence ->
        key(occurrence.id) {
            val sync = occurrence.localStatus?.let {
                if (it == "pending") FlightSheetWorkSync.Pending else FlightSheetWorkSync.NeedsReview
            }
            val items = occurrence.services.map { service ->
                FlightSheetWorkItem("service:${service.id}", FlightSheetWorkKind.Service, service.serviceName,
                    service.startUtc, service.endUtc, service.performers.map { it.staffMemberId.lowercase() }.distinct().size, sync)
            } + occurrence.tasks.map { task ->
                FlightSheetWorkItem("task:${task.id}", when (task.taskType) {
                    0 -> FlightSheetWorkKind.MajorTask
                    1 -> FlightSheetWorkKind.MinorTask
                    else -> FlightSheetWorkKind.Task
                }, "", task.startUtc, task.endUtc, task.performers.map { it.staffMemberId.lowercase() }.distinct().size,
                    sync, ataCode = task.ataChapterCode.orEmpty(), ataTitle = task.ataChapterTitle.orEmpty())
            }
            FlightSheetWork(flightId = "${flight.id}:rtr:${occurrence.id}", items = items.sortedWith(compareBy<FlightSheetWorkItem> {
                runCatching { parseOffsetDateTime(it.startUtc).toInstant() }.getOrDefault(Instant.MAX)
            }.thenBy { it.key }), zone = zone,
                title = stringResource(R.string.rtr_sheet_title, occurrence.sequence),
                summaryHeader = { ReturnToRampSummary(occurrence, zone) })
        }
    }
}

@Composable
private fun ReturnToRampSummary(occurrence: FlightReturnToRampRecord, zone: ZoneId) {
    val muted = MaterialTheme.colorScheme.onSurfaceVariant
    val green = if (MaterialTheme.colorScheme.surface.luminance() < 0.5f) Color(0xFF81C995) else Color(0xFF248A3D)
    val label = MaterialTheme.typography.labelSmall
    val value = MaterialTheme.typography.titleLarge.copy(fontSize = 18.sp, lineHeight = 22.sp,
        fontWeight = FontWeight.Bold, fontFeatureSettings = "tnum")
    Column(Modifier.fillMaxWidth().padding(start = 14.dp, end = 14.dp, bottom = 12.dp),
        verticalArrangement = Arrangement.spacedBy(6.dp)) {
        Text(stringResource(if (occurrence.isOpen) R.string.rtr_sheet_open else R.string.rtr_sheet_closed),
            style = label, fontWeight = FontWeight.SemiBold,
            color = if (occurrence.isOpen) MaterialTheme.colorScheme.tertiary else muted)
        CompositionLocalProvider(LocalLayoutDirection provides LayoutDirection.Ltr) {
            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(5.dp)) {
                        Icon(Icons.Outlined.FlightLand, null, Modifier.size(16.dp), tint = green)
                        Text(stringResource(R.string.arrival_ata_short), style = label, color = muted)
                    }
                    Text(formatIsoTimeForDisplay(occurrence.arrivedAtUtc, zone), style = value, maxLines = 1, softWrap = false)
                }
                Icon(Icons.AutoMirrored.Filled.ArrowForward, null, Modifier.size(18.dp), tint = muted)
                Column(Modifier.weight(1f), horizontalAlignment = Alignment.End, verticalArrangement = Arrangement.spacedBy(4.dp)) {
                    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(5.dp)) {
                        Icon(Icons.Outlined.FlightTakeoff, null, Modifier.size(16.dp), tint = green)
                        Text(stringResource(R.string.operations_atd_short), style = label, color = muted)
                    }
                    Text(occurrence.departedAtUtc?.let { formatIsoTimeForDisplay(it, zone) } ?: "—",
                        style = value, textAlign = TextAlign.End, maxLines = 1, softWrap = false)
                }
            }
            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(5.dp, Alignment.End)) {
                Icon(Icons.Outlined.CalendarMonth, null, Modifier.size(14.dp), tint = muted)
                Text(formatFlightDateRangeForDisplay(occurrence.arrivedAtUtc,
                    occurrence.departedAtUtc ?: occurrence.arrivedAtUtc, zone), style = label, color = muted,
                    textAlign = TextAlign.End)
            }
        }
        val people = listOfNotNull(occurrence.receivedByFullName.takeIf(String::isNotBlank),
            occurrence.handledByFullName?.takeIf(String::isNotBlank)).distinct()
        if (people.isNotEmpty()) Row(verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(5.dp)) {
            Icon(Icons.Outlined.Engineering, null, Modifier.size(14.dp), tint = muted)
            Text(people.joinToString(" | "), style = label, color = muted, maxLines = 2, overflow = TextOverflow.Ellipsis)
        }
        if (occurrence.localStatus != null) {
            Text(stringResource(if (occurrence.localStatus == "pending") R.string.flight_actuals_saved_locally else R.string.flight_card_needs_review),
                style = label, color = if (occurrence.localStatus == "pending") muted else MaterialTheme.colorScheme.error)
        }
        occurrence.createdByDisplayName?.takeIf { it.isNotBlank() && occurrence.localStatus == null }?.let { name ->
            Text(stringResource(R.string.rtr_sheet_submitted, name,
                formatFlightDateRangeForDisplay(occurrence.createdAtUtc, occurrence.createdAtUtc, zone),
                formatIsoTimeForDisplay(occurrence.createdAtUtc, zone)), style = label, color = muted)
        }
    }
    HorizontalDivider(thickness = .5.dp, color = MaterialTheme.colorScheme.onSurface.copy(alpha = .06f))
}
