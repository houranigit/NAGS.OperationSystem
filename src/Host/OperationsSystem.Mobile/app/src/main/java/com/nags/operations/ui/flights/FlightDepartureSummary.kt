package com.nags.operations.ui.flights

import androidx.compose.foundation.Image
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.material.icons.Icons
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
import androidx.compose.ui.layout.ContentScale
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
import com.nags.operations.data.FlightDepartureSubmission
import com.nags.operations.ui.util.formatIsoDateForDisplay
import com.nags.operations.ui.util.formatIsoTimeForDisplay
import java.time.ZoneId

/** Another employee's saved departure follows saved arrival in the identity card. */
@Composable
internal fun FlightDepartureSummary(departure: FlightDepartureSubmission, zone: ZoneId, signaturePreview: ByteArray? = null) {
    val signature = rememberSignatureBitmap(signaturePreview)
    val signatureDescription = stringResource(R.string.operations_signature_title)
    HorizontalDivider(Modifier.padding(vertical = 10.dp), thickness = 0.5.dp,
        color = MaterialTheme.colorScheme.onSurface.copy(alpha = 0.06f))
    val labelStyle = MaterialTheme.typography.labelSmall
    val valueStyle = MaterialTheme.typography.titleLarge.copy(fontSize = 18.sp, lineHeight = 22.sp,
        fontWeight = FontWeight.Bold, fontFeatureSettings = "tnum")
    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
        BoxWithConstraints(Modifier.fillMaxWidth()) {
            val employee: @Composable (Modifier) -> Unit = { modifier ->
                Column(modifier, verticalArrangement = Arrangement.spacedBy(4.dp)) {
                    Text(stringResource(R.string.departure_actual_details), style = labelStyle,
                        color = MaterialTheme.colorScheme.onSurfaceVariant)
                    // Fit the preview inside the value line; it must not move the employee caption.
                    Box(Modifier.fillMaxWidth().height(with(LocalDensity.current) { valueStyle.lineHeight.toDp() }),
                        contentAlignment = Alignment.CenterStart) {
                        if (signature != null) Image(signature, signatureDescription, Modifier.fillMaxWidth().height(with(LocalDensity.current) { valueStyle.lineHeight.toDp() }),
                            contentScale = ContentScale.Fit, alignment = Alignment.CenterStart)
                        else Text("-", style = valueStyle, maxLines = 1)
                    }
                    Row(horizontalArrangement = Arrangement.spacedBy(4.dp), verticalAlignment = Alignment.CenterVertically) {
                        Icon(Icons.Outlined.Engineering, null, Modifier.size(14.dp), tint = MaterialTheme.colorScheme.onSurfaceVariant)
                        Text(departure.handledByEmployee.fullName, Modifier.weight(1f), style = labelStyle,
                            color = MaterialTheme.colorScheme.onSurfaceVariant)
                    }
                }
            }
            val time: @Composable (Modifier) -> Unit = { modifier ->
                Column(modifier, horizontalAlignment = Alignment.End, verticalArrangement = Arrangement.spacedBy(4.dp)) {
                    Text(stringResource(R.string.operations_atd_short), style = labelStyle, color = MaterialTheme.colorScheme.onSurfaceVariant)
                    Text(formatIsoTimeForDisplay(departure.atd, zone), style = valueStyle, maxLines = 1)
                    Row(horizontalArrangement = Arrangement.spacedBy(4.dp), verticalAlignment = Alignment.CenterVertically) {
                        Icon(Icons.Outlined.CalendarMonth, null, Modifier.size(14.dp), tint = MaterialTheme.colorScheme.onSurfaceVariant)
                        Text(formatIsoDateForDisplay(departure.atd, zone), style = labelStyle, textAlign = TextAlign.End,
                            color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1, overflow = TextOverflow.Ellipsis)
                    }
                }
            }
            if (maxWidth < 260.dp * LocalDensity.current.fontScale) {
                Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                    employee(Modifier.fillMaxWidth())
                    time(Modifier.align(Alignment.End))
                }
            } else Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(16.dp), verticalAlignment = Alignment.Top) {
                employee(Modifier.weight(1f))
                time(Modifier)
            }
        }
        departure.remarks?.takeIf { it.isNotBlank() }?.let { remarks ->
            val label = stringResource(R.string.flight_form_remarks)
            val quoted = stringResource(R.string.arrival_quoted_remarks, remarks)
            Text(buildAnnotatedString {
                append(label); append(": ")
                withStyle(SpanStyle(fontStyle = FontStyle.Italic)) { append(quoted) }
            }, Modifier.fillMaxWidth(), style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
    }
}
