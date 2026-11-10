package com.nags.operations.ui.flights

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.IntrinsicSize
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowForward
import androidx.compose.material.icons.outlined.CalendarMonth
import androidx.compose.material.icons.outlined.Engineering
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.nags.operations.R
import com.nags.operations.ui.util.formatFlightDateRangeForDisplay
import com.nags.operations.ui.util.formatIsoTimeForDisplay
import java.time.ZoneId

/** Compact actuals for the bottom sheet; full attribution and remarks stay in the form. */
@Composable
internal fun FlightActualSummary(actuals: FlightActualDetails?, zone: ZoneId) {
    if (actuals == null) return
    HorizontalDivider(Modifier.padding(vertical = 10.dp), thickness = 0.5.dp,
        color = MaterialTheme.colorScheme.onSurface.copy(alpha = 0.06f))
    val labelStyle = MaterialTheme.typography.labelSmall
    val valueStyle = MaterialTheme.typography.titleLarge.copy(fontSize = 18.sp, lineHeight = 22.sp,
        fontWeight = FontWeight.Bold, fontFeatureSettings = "tnum")
    Column {
        BoxWithConstraints(Modifier.fillMaxWidth()) {
            val aircraft: @Composable (Modifier) -> Unit = { modifier ->
                Column(modifier, verticalArrangement = Arrangement.spacedBy(4.dp)) {
                    Text(stringResource(R.string.flight_actual_details), style = labelStyle, color = MaterialTheme.colorScheme.onSurfaceVariant)
                    Text(actuals.tailNumber?.let { "${actuals.aircraftModel ?: stringResource(R.string.flights_card_aircraft_unavailable)} | $it" } ?: "—",
                        style = valueStyle, maxLines = 2, overflow = TextOverflow.Ellipsis)
                    Row(horizontalArrangement = Arrangement.spacedBy(4.dp), verticalAlignment = Alignment.CenterVertically) {
                        Icon(Icons.Outlined.Engineering, null, Modifier.size(14.dp), tint = MaterialTheme.colorScheme.onSurfaceVariant)
                        Text(actuals.employeeNames.joinToString(" | "), Modifier.weight(1f), style = labelStyle,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                            maxLines = 1, softWrap = false, overflow = TextOverflow.Ellipsis)
                    }
                }
            }
            val times: @Composable (Modifier) -> Unit = { modifier ->
                val arrowSize = 18.dp
                val timeGap = 8.dp
                Column(modifier.width(IntrinsicSize.Max), horizontalAlignment = Alignment.End, verticalArrangement = Arrangement.spacedBy(4.dp)) {
                    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                        Text(stringResource(R.string.arrival_ata_short), Modifier.weight(1f), style = labelStyle,
                            color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1, overflow = TextOverflow.Ellipsis)
                        Spacer(Modifier.width(arrowSize + timeGap * 2))
                        Text(stringResource(R.string.operations_atd_short), Modifier.weight(1f), style = labelStyle, textAlign = TextAlign.End,
                            color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1, overflow = TextOverflow.Ellipsis)
                    }
                    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(timeGap), verticalAlignment = Alignment.CenterVertically) {
                        Text(actuals.ata?.let { formatIsoTimeForDisplay(it, zone) } ?: "—", Modifier.weight(1f), style = valueStyle,
                            maxLines = 1, overflow = TextOverflow.Ellipsis)
                        Icon(Icons.AutoMirrored.Filled.ArrowForward, null, Modifier.size(arrowSize), tint = MaterialTheme.colorScheme.onSurfaceVariant)
                        Text(actuals.atd?.let { formatIsoTimeForDisplay(it, zone) } ?: "—", Modifier.weight(1f), style = valueStyle,
                            textAlign = TextAlign.End, maxLines = 1, overflow = TextOverflow.Ellipsis)
                    }
                    val first = actuals.ata ?: checkNotNull(actuals.atd)
                    val last = actuals.atd ?: first
                    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(4.dp, Alignment.End), verticalAlignment = Alignment.CenterVertically) {
                        Icon(Icons.Outlined.CalendarMonth, null, Modifier.size(14.dp), tint = MaterialTheme.colorScheme.onSurfaceVariant)
                        Text(formatFlightDateRangeForDisplay(first, last, zone), style = labelStyle, textAlign = TextAlign.End,
                            color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1, overflow = TextOverflow.Ellipsis)
                    }
                }
            }
            if (maxWidth < 320.dp * LocalDensity.current.fontScale) {
                Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                    aircraft(Modifier.fillMaxWidth())
                    times(Modifier.align(Alignment.End))
                }
            } else Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(16.dp), verticalAlignment = Alignment.Top) {
                aircraft(Modifier.weight(1f))
                times(Modifier)
            }
        }
        if (actuals.savedLocally) Text(stringResource(R.string.flight_actuals_saved_locally),
            Modifier.padding(top = 6.dp), style = labelStyle, color = MaterialTheme.colorScheme.onSurfaceVariant)
    }
}
