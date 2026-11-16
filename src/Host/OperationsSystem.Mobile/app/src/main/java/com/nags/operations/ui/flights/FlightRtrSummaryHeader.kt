package com.nags.operations.ui.flights

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowForward
import androidx.compose.material.icons.filled.FlightLand
import androidx.compose.material.icons.filled.FlightTakeoff
import androidx.compose.material.icons.outlined.CalendarMonth
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.luminance
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalLayoutDirection
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.rememberTextMeasurer
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.LayoutDirection
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.nags.operations.R
import com.nags.operations.ui.util.formatFlightDateRangeForDisplay
import com.nags.operations.ui.util.formatIsoTimeForDisplay
import java.time.ZoneId

/** The same compact right-hand timing group for submitted RTRs in the form and details sheet. */
@Composable
internal fun FlightRtrSummaryHeader(arrivedAtUtc: String, departedAtUtc: String?, zone: ZoneId,
    info: @Composable () -> Unit,
) {
    val muted = MaterialTheme.colorScheme.onSurfaceVariant
    val green = if (MaterialTheme.colorScheme.surface.luminance() < 0.5f) Color(0xFF81C995) else Color(0xFF248A3D)
    val labelStyle = MaterialTheme.typography.bodySmall.copy(fontSize = 12.sp, lineHeight = 16.sp, fontWeight = FontWeight.SemiBold)
    val valueStyle = MaterialTheme.typography.bodySmall.copy(fontSize = 18.sp, lineHeight = 22.sp,
        fontWeight = FontWeight.Bold, fontFeatureSettings = "tnum")
    val dateStyle = MaterialTheme.typography.bodySmall.copy(fontSize = 12.sp, lineHeight = 16.sp, fontWeight = FontWeight.Medium)
    val arrivalLabel = stringResource(R.string.arrival_ata_short)
    val departureLabel = stringResource(R.string.operations_atd_short)
    val arrival = formatIsoTimeForDisplay(arrivedAtUtc, zone)
    val departure = departedAtUtc?.let { formatIsoTimeForDisplay(it, zone) } ?: "—"
    val date = formatFlightDateRangeForDisplay(arrivedAtUtc, departedAtUtc ?: arrivedAtUtc, zone)
    val measurer = rememberTextMeasurer()
    val density = LocalDensity.current
    CompositionLocalProvider(LocalLayoutDirection provides LayoutDirection.Ltr) {
        BoxWithConstraints(Modifier.fillMaxWidth()) {
            val labelWidth = with(density) { maxOf(measurer.measure(arrivalLabel, labelStyle).size.width,
                measurer.measure(departureLabel, labelStyle).size.width).toDp() } + 18.dp
            val cellWidth = maxOf(labelWidth, with(density) { maxOf(measurer.measure(arrival, valueStyle).size.width,
                measurer.measure(departure, valueStyle).size.width).toDp() })
            val timesWidth = cellWidth * 2 + 26.dp
            val dateWidth = with(density) { measurer.measure(date, dateStyle).size.width.toDp() } + 18.dp
            val timingWidth = maxOf(timesWidth, dateWidth).coerceAtMost(maxWidth)
            val gap = if (maxWidth < 260.dp) 8.dp else 16.dp
            val stack = timingWidth + gap + 76.dp * density.fontScale > maxWidth
            val timing: @Composable (Modifier) -> Unit = { modifier ->
                Column(modifier.width(timingWidth), horizontalAlignment = Alignment.End,
                    verticalArrangement = Arrangement.spacedBy(4.dp)) {
                    Row(Modifier.width(timesWidth), verticalAlignment = Alignment.Bottom,
                        horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                        Column(Modifier.width(cellWidth), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                            Row(horizontalArrangement = Arrangement.spacedBy(4.dp), verticalAlignment = Alignment.CenterVertically) {
                                Icon(Icons.Filled.FlightLand, null, Modifier.size(14.dp), tint = green)
                                Text(arrivalLabel, style = labelStyle, color = muted, maxLines = 1, softWrap = false)
                            }
                            Text(arrival, style = valueStyle, maxLines = 1, softWrap = false)
                        }
                        Icon(Icons.AutoMirrored.Filled.ArrowForward, null,
                            Modifier.padding(bottom = 4.dp).size(14.dp), tint = muted)
                        Column(Modifier.width(cellWidth), horizontalAlignment = Alignment.End,
                            verticalArrangement = Arrangement.spacedBy(4.dp)) {
                            Row(horizontalArrangement = Arrangement.spacedBy(4.dp), verticalAlignment = Alignment.CenterVertically) {
                                Icon(Icons.Filled.FlightTakeoff, null, Modifier.size(14.dp), tint = green)
                                Text(departureLabel, style = labelStyle, color = muted, maxLines = 1, softWrap = false)
                            }
                            Text(departure, style = valueStyle, maxLines = 1, softWrap = false)
                        }
                    }
                    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(4.dp, Alignment.End),
                        verticalAlignment = Alignment.Top) {
                        Icon(Icons.Outlined.CalendarMonth, null, Modifier.size(14.dp), tint = muted)
                        Text(date, Modifier.weight(1f, fill = false), style = dateStyle, color = muted, textAlign = TextAlign.End)
                    }
                }
            }
            if (stack) Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(12.dp)) {
                Column(verticalArrangement = Arrangement.spacedBy(6.dp)) { info() }
                timing(Modifier.align(Alignment.End))
            } else Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(gap), verticalAlignment = Alignment.Top) {
                Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(6.dp)) { info() }
                timing(Modifier)
            }
        }
    }
}
