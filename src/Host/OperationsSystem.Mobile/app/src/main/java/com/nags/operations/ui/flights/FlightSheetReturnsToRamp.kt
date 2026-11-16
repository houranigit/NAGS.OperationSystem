package com.nags.operations.ui.flights

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Engineering
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.key
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
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
    val label = MaterialTheme.typography.labelSmall
    val people = listOfNotNull(occurrence.receivedByFullName.takeIf(String::isNotBlank),
        occurrence.handledByFullName?.takeIf(String::isNotBlank)).distinct()
    Column(Modifier.fillMaxWidth().padding(start = 14.dp, end = 14.dp, bottom = 12.dp),
        verticalArrangement = Arrangement.spacedBy(6.dp)) {
        FlightRtrSummaryHeader(occurrence.arrivedAtUtc, occurrence.departedAtUtc, zone) {
            Text(stringResource(if (occurrence.isOpen) R.string.rtr_sheet_open else R.string.rtr_sheet_closed),
                style = label, fontWeight = FontWeight.SemiBold,
                color = if (occurrence.isOpen) MaterialTheme.colorScheme.tertiary else muted)
            if (people.isNotEmpty()) Row(verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(5.dp)) {
                Icon(Icons.Outlined.Engineering, null, Modifier.size(14.dp), tint = muted)
                Text(people.joinToString(" | "), style = label, color = muted, maxLines = 2, overflow = TextOverflow.Ellipsis)
            }
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
