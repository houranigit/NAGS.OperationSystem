package com.nags.operations.ui.flights

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.CalendarMonth
import androidx.compose.material.icons.outlined.PersonOutline
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.SpanStyle
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.font.FontStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.text.withStyle
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.nags.operations.R
import com.nags.operations.data.FlightArrivalSubmission
import com.nags.operations.ui.util.formatIsoDateForDisplay
import com.nags.operations.ui.util.formatIsoTimeForDisplay
import java.time.ZoneId

/** Another employee's saved arrival belongs with the flight identity, outside the editor. */
@Composable
internal fun FlightArrivalSummary(arrival: FlightArrivalSubmission, zone: ZoneId) {
    HorizontalDivider(Modifier.padding(vertical = 10.dp), thickness = 0.5.dp,
        color = MaterialTheme.colorScheme.onSurface.copy(alpha = 0.06f))
    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
        BoxWithConstraints(Modifier.fillMaxWidth()) {
            val stackColumns = maxWidth < 260.dp * LocalDensity.current.fontScale
            val labelStyle = MaterialTheme.typography.labelSmall
            val valueStyle = MaterialTheme.typography.titleLarge.copy(fontSize = 18.sp, lineHeight = 22.sp,
                fontWeight = FontWeight.Bold, fontFeatureSettings = "tnum")
            val aircraft: @Composable (Modifier) -> Unit = { modifier ->
                Column(modifier, verticalArrangement = Arrangement.spacedBy(4.dp)) {
                    Text(stringResource(R.string.arrival_actual_details), style = labelStyle,
                        color = MaterialTheme.colorScheme.onSurfaceVariant)
                    Text("${arrival.aircraftTypeModel} | ${arrival.aircraftTailNumber}", style = valueStyle)
                    Row(horizontalArrangement = Arrangement.spacedBy(4.dp), verticalAlignment = Alignment.CenterVertically) {
                        Icon(Icons.Outlined.PersonOutline, null, Modifier.size(14.dp),
                            tint = MaterialTheme.colorScheme.onSurfaceVariant)
                        Text(arrival.receivedByEmployee.fullName, Modifier.weight(1f), style = labelStyle,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                            maxLines = 1, softWrap = false, overflow = TextOverflow.Ellipsis)
                    }
                }
            }
            val actualTime: @Composable (Modifier) -> Unit = { modifier ->
                Column(modifier, horizontalAlignment = Alignment.End, verticalArrangement = Arrangement.spacedBy(4.dp)) {
                    Text(stringResource(R.string.arrival_ata_short), style = labelStyle,
                        color = MaterialTheme.colorScheme.onSurfaceVariant)
                    Text(formatIsoTimeForDisplay(arrival.ata, zone), style = valueStyle, maxLines = 1)
                    Row(horizontalArrangement = Arrangement.spacedBy(4.dp), verticalAlignment = Alignment.CenterVertically) {
                        Icon(Icons.Outlined.CalendarMonth, null, Modifier.size(14.dp),
                            tint = MaterialTheme.colorScheme.onSurfaceVariant)
                        Text(formatIsoDateForDisplay(arrival.ata, zone), style = labelStyle,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                            textAlign = TextAlign.End, maxLines = 1, overflow = TextOverflow.Ellipsis)
                    }
                }
            }
            if (stackColumns) Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                aircraft(Modifier.fillMaxWidth())
                actualTime(Modifier.align(Alignment.End))
            } else Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(16.dp),
                verticalAlignment = Alignment.Top) {
                aircraft(Modifier.weight(1f))
                actualTime(Modifier.weight(1f))
            }
        }
        arrival.remarks?.takeIf { it.isNotBlank() }?.let { remarks ->
            val label = stringResource(R.string.flight_form_remarks)
            val quotedRemarks = stringResource(R.string.arrival_quoted_remarks, remarks)
            Text(buildAnnotatedString {
                append(label)
                append(": ")
                withStyle(SpanStyle(fontStyle = FontStyle.Italic)) { append(quotedRemarks) }
            }, Modifier.fillMaxWidth(), style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
    }
}
