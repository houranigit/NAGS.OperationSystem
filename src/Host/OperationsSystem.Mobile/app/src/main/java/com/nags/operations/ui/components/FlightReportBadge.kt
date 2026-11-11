package com.nags.operations.ui.components

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.outlined.Undo
import androidx.compose.material.icons.outlined.WarningAmber
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.luminance
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.nags.operations.R
import com.nags.operations.ui.flights.FlightCardReportStatus

@Composable
internal fun FlightReportBadge(status: FlightCardReportStatus, modifier: Modifier = Modifier) {
    val label = stringResource(when (status) {
        FlightCardReportStatus.Scheduled -> R.string.flights_scheduled
        FlightCardReportStatus.Canceled -> R.string.flight_card_canceled
        FlightCardReportStatus.DueSoon -> R.string.flight_card_due_soon
        FlightCardReportStatus.ArrivalNotRecorded -> R.string.flight_card_arrival_not_recorded
        FlightCardReportStatus.OnGround -> R.string.flight_card_on_ground
        FlightCardReportStatus.ReturnedToRamp -> R.string.flight_card_returned_to_ramp
        FlightCardReportStatus.Departed -> R.string.flight_card_departed
        FlightCardReportStatus.Conflict -> R.string.flight_card_conflict
    })
    val colors = flightStatusColors(status)
    Surface(modifier, shape = RoundedCornerShape(50), color = colors.background, contentColor = colors.foreground) {
        Row(Modifier.padding(horizontal = 7.dp, vertical = 3.dp), horizontalArrangement = Arrangement.spacedBy(3.dp),
            verticalAlignment = Alignment.CenterVertically) {
            if (status == FlightCardReportStatus.Conflict) Icon(Icons.Outlined.WarningAmber, null, Modifier.size(11.dp))
            if (status == FlightCardReportStatus.ReturnedToRamp) Icon(Icons.AutoMirrored.Outlined.Undo, null, Modifier.size(11.dp))
            Text(label, Modifier.weight(1f, fill = false), style = MaterialTheme.typography.labelSmall.copy(fontSize = 11.sp, lineHeight = 13.sp),
                fontWeight = FontWeight.SemiBold, maxLines = 2, overflow = TextOverflow.Ellipsis)
        }
    }
}

internal data class FlightStatusColors(val background: Color, val foreground: Color, val stripe: Color, val border: Color)

@Composable
internal fun flightStatusColors(status: FlightCardReportStatus): FlightStatusColors {
    val dark = MaterialTheme.colorScheme.surface.luminance() < 0.5f
    return when (status) {
        FlightCardReportStatus.Scheduled, FlightCardReportStatus.OnGround -> if (dark)
            FlightStatusColors(Color(0xFF21334B), Color(0xFFA4CBFF), Color(0xFF76B1FF), Color(0xFF365677))
        else FlightStatusColors(Color(0xFFE8F2FF), Color(0xFF155EAB), Color(0xFF007AFF), Color(0xFFC7DDFA))
        FlightCardReportStatus.Departed -> if (dark)
            FlightStatusColors(Color(0xFF233C2D), Color(0xFFA3DFB7), Color(0xFF7CC796), Color(0xFF3C5F49))
        else FlightStatusColors(Color(0xFFE8F5EC), Color(0xFF176A3A), Color(0xFF249454), Color(0xFFC2DFCC))
        FlightCardReportStatus.Canceled -> if (dark)
            FlightStatusColors(Color(0xFF482A2B), Color(0xFFFFB4AE), Color(0xFFEF928C), Color(0xFF744744))
        else FlightStatusColors(Color(0xFFFDECEB), Color(0xFFA12622), Color(0xFFD9443D), Color(0xFFECC2BF))
        FlightCardReportStatus.DueSoon, FlightCardReportStatus.ArrivalNotRecorded,
        FlightCardReportStatus.ReturnedToRamp, FlightCardReportStatus.Conflict -> if (dark)
            FlightStatusColors(Color(0xFF49361D), Color(0xFFFFD18E), Color(0xFFE7B767), Color(0xFF715937))
        else FlightStatusColors(Color(0xFFFFF3D6), Color(0xFF8B4B00), Color(0xFFD48A0B), Color(0xFFEDD4A7))
    }
}
