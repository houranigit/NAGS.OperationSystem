package com.nags.operations.ui.flights

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalLayoutDirection
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.SpanStyle
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.text.withStyle
import androidx.compose.ui.unit.LayoutDirection
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.nags.operations.R

@Composable
internal fun FlightProgressBadge(status: FlightProgressStatus, modifier: Modifier = Modifier) {
    val label = stringResource(when (status) {
        FlightProgressStatus.Draft -> R.string.flight_progress_draft
        FlightProgressStatus.ArrivalSubmitted -> R.string.flight_progress_arrival_submitted
        FlightProgressStatus.ArrivalPendingSync -> R.string.flight_progress_arrival_pending
        FlightProgressStatus.ArrivalNeedsReview -> R.string.flight_progress_arrival_review
        FlightProgressStatus.DepartureSubmitted -> R.string.flight_progress_departure_submitted
        FlightProgressStatus.DeparturePendingSync -> R.string.flight_progress_departure_pending
        FlightProgressStatus.DepartureNeedsReview -> R.string.flight_progress_departure_review
    })
    val (background, foreground) = when (status) {
        FlightProgressStatus.Draft -> Color(0xFFF1F3F5) to Color(0xFF4B5563)
        FlightProgressStatus.ArrivalSubmitted -> Color(0xFFE8F2FF) to Color(0xFF155EAB)
        FlightProgressStatus.DepartureSubmitted -> Color(0xFFE8F5EC) to Color(0xFF176A3A)
        FlightProgressStatus.ArrivalPendingSync, FlightProgressStatus.DeparturePendingSync -> Color(0xFFFFF3D6) to Color(0xFF8B4B00)
        FlightProgressStatus.ArrivalNeedsReview, FlightProgressStatus.DepartureNeedsReview -> Color(0xFFFDEDEB) to Color(0xFFA12622)
    }
    Surface(modifier, shape = RoundedCornerShape(50), color = background, contentColor = foreground) {
        Text(label, Modifier.padding(horizontal = 7.dp, vertical = 4.dp),
            style = MaterialTheme.typography.labelSmall.copy(fontSize = 10.sp, lineHeight = 12.sp, letterSpacing = 0.1.sp),
            fontWeight = FontWeight.SemiBold, maxLines = 1, softWrap = false, overflow = TextOverflow.Ellipsis)
    }
}

/** One shared operation/progress row keeps the form and sheet aligned in every locale. */
@Composable
internal fun FlightOperationStatusRow(operationType: String, status: FlightProgressStatus, modifier: Modifier = Modifier) {
    CompositionLocalProvider(LocalLayoutDirection provides LayoutDirection.Ltr) {
        BoxWithConstraints(modifier.fillMaxWidth()) {
            val badgeMaxWidth = maxWidth * 0.5f
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(12.dp), verticalAlignment = Alignment.CenterVertically) {
                val label = stringResource(R.string.flight_operation_type)
                Text(buildAnnotatedString {
                    append(label); append(" ")
                    withStyle(SpanStyle(fontWeight = FontWeight.Bold, color = MaterialTheme.colorScheme.onSurface)) { append(operationType) }
                }, Modifier.weight(1f), style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant, textAlign = TextAlign.Left, maxLines = 2, overflow = TextOverflow.Ellipsis)
                FlightProgressBadge(status, Modifier.widthIn(max = badgeMaxWidth))
            }
        }
    }
}
