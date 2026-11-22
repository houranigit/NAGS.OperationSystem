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
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.nags.operations.R
import com.nags.operations.ui.flights.FlightCardReportStatus

@Composable
internal fun FlightReportBadge(status: FlightCardReportStatus, modifier: Modifier = Modifier, compact: Boolean = false) {
    val label = flightReportLabel(status)
    val colors = flightStatusColors(status)
    Surface(modifier, shape = RoundedCornerShape(50), color = colors.background, contentColor = colors.foreground) {
        Row(Modifier.padding(horizontal = if (compact) 6.dp else 7.dp, vertical = if (compact) 2.dp else 3.dp), horizontalArrangement = Arrangement.spacedBy(3.dp),
            verticalAlignment = Alignment.CenterVertically) {
            if (status == FlightCardReportStatus.Conflict) Icon(Icons.Outlined.WarningAmber, null, Modifier.size(if (compact) 10.dp else 11.dp))
            if (status == FlightCardReportStatus.ReturnedToRamp) Icon(Icons.AutoMirrored.Outlined.Undo, null, Modifier.size(if (compact) 10.dp else 11.dp))
            Text(label, Modifier.weight(1f, fill = false), style = flightReportBadgeTextStyle(compact),
                maxLines = if (compact) 1 else 2, softWrap = !compact, overflow = TextOverflow.Ellipsis)
        }
    }
}

@Composable
internal fun flightReportBadgeTextStyle(compact: Boolean = false): TextStyle =
    MaterialTheme.typography.labelSmall.copy(fontSize = if (compact) 10.sp else 11.sp,
        lineHeight = if (compact) 12.sp else 13.sp, fontWeight = FontWeight.SemiBold, letterSpacing = 0.sp)

@Composable
internal fun flightReportLabel(status: FlightCardReportStatus): String =
    stringResource(when (status) {
        FlightCardReportStatus.Scheduled -> R.string.flights_scheduled
        FlightCardReportStatus.Canceled -> R.string.flight_card_canceled
        FlightCardReportStatus.DueSoon -> R.string.flight_card_due_soon
        FlightCardReportStatus.ArrivalNotRecorded -> R.string.flight_card_arrival_not_recorded
        FlightCardReportStatus.OnGround -> R.string.flight_card_on_ground
        FlightCardReportStatus.ReturnedToRamp -> R.string.flight_card_returned_to_ramp
        FlightCardReportStatus.Departed -> R.string.flight_card_departed
        FlightCardReportStatus.Conflict -> R.string.flight_card_conflict
    })

internal data class FlightStatusColors(val background: Color, val foreground: Color, val stripe: Color, val border: Color)

@Composable
internal fun flightStatusColors(status: FlightCardReportStatus): FlightStatusColors {
    val dark = MaterialTheme.colorScheme.surface.luminance() < 0.5f
    return when (status) {
        FlightCardReportStatus.Scheduled -> if (dark)
            FlightStatusColors(Color(0xFF343434), Color(0xFFD6D6D6), Color(0xFFA3A3A3), Color(0xFF505050))
        else FlightStatusColors(Color(0xFFF2F2F2), Color(0xFF545454), Color(0xFF757575), Color(0xFFD9D9D9))
        FlightCardReportStatus.DueSoon -> if (dark)
            FlightStatusColors(Color(0xFF49361D), Color(0xFFFFD18E), Color(0xFFE7B767), Color(0xFF715937))
        else FlightStatusColors(Color(0xFFFFF3D6), Color(0xFF8B4B00), Color(0xFFD48A0B), Color(0xFFEDD4A7))
        FlightCardReportStatus.ArrivalNotRecorded -> if (dark)
            FlightStatusColors(Color(0xFF4D2B1D), Color(0xFFFFC39F), Color(0xFFFFA271), Color(0xFF79462F))
        else FlightStatusColors(Color(0xFFFFEDD9), Color(0xFF963D0A), Color(0xFFC65C1C), Color(0xFFEFBA91))
        FlightCardReportStatus.OnGround -> if (dark)
            FlightStatusColors(Color(0xFF21334B), Color(0xFFA4CBFF), Color(0xFF76B1FF), Color(0xFF365677))
        else FlightStatusColors(Color(0xFFE8F2FF), Color(0xFF155EAB), Color(0xFF007AFF), Color(0xFFC7DDFA))
        FlightCardReportStatus.ReturnedToRamp -> if (dark)
            FlightStatusColors(Color(0xFF39274F), Color(0xFFDFC7FF), Color(0xFFB78CE9), Color(0xFF5C457A))
        else FlightStatusColors(Color(0xFFF1E9FC), Color(0xFF633398), Color(0xFF8954C1), Color(0xFFDAC6F1))
        FlightCardReportStatus.Departed -> if (dark)
            FlightStatusColors(Color(0xFF30394A), Color(0xFFC8D4E6), Color(0xFF8193AB), Color(0xFF47576D))
        else FlightStatusColors(Color(0xFFE8EDF3), Color(0xFF46566D), Color(0xFF64748B), Color(0xFFCBD5E1))
        FlightCardReportStatus.Canceled -> if (dark)
            FlightStatusColors(Color(0xFF482A2B), Color(0xFFFFB4AE), Color(0xFFEF928C), Color(0xFF744744))
        else FlightStatusColors(Color(0xFFFDECEB), Color(0xFFA12622), Color(0xFFD9443D), Color(0xFFECC2BF))
        FlightCardReportStatus.Conflict -> if (dark)
            FlightStatusColors(Color(0xFF58172A), Color(0xFFFFD2DB), Color(0xFFF7758E), Color(0xFFA44759))
        else FlightStatusColors(Color(0xFFFCE0E4), Color(0xFF86152D), Color(0xFFA91D39), Color(0xFFD68191))
    }
}
