package com.nags.operations.ui.components

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.WarningAmber
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.nags.operations.R
import com.nags.operations.ui.flights.FlightCardReportStatus

@Composable
internal fun FlightReportBadge(status: FlightCardReportStatus) {
    val label = stringResource(when (status) {
        FlightCardReportStatus.Scheduled -> R.string.flights_scheduled
        FlightCardReportStatus.Canceled -> R.string.flight_card_canceled
        FlightCardReportStatus.Arrived -> R.string.flight_card_arrived
        FlightCardReportStatus.Departed -> R.string.flight_card_departed
        FlightCardReportStatus.Conflict -> R.string.flight_card_conflict
    })
    val (background, foreground) = when (status) {
        FlightCardReportStatus.Scheduled, FlightCardReportStatus.Arrived -> Color(0xFFE8F2FF) to Color(0xFF155EAB)
        FlightCardReportStatus.Departed -> Color(0xFFE8F5EC) to Color(0xFF176A3A)
        FlightCardReportStatus.Canceled -> Color(0xFFFDEDEB) to Color(0xFFA12622)
        FlightCardReportStatus.Conflict -> Color(0xFFFFF3D6) to Color(0xFF8B4B00)
    }
    Surface(Modifier.widthIn(max = 92.dp), shape = RoundedCornerShape(50), color = background, contentColor = foreground) {
        Row(Modifier.padding(horizontal = 9.dp, vertical = 4.dp), horizontalArrangement = Arrangement.spacedBy(3.dp),
            verticalAlignment = Alignment.CenterVertically) {
            if (status == FlightCardReportStatus.Conflict) Icon(Icons.Outlined.WarningAmber, null, Modifier.size(12.dp))
            Text(label, Modifier.weight(1f, fill = false), style = MaterialTheme.typography.labelSmall,
                fontWeight = FontWeight.SemiBold, maxLines = 1, overflow = TextOverflow.Ellipsis)
        }
    }
}
