package com.nags.operations.ui.components

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.layout.wrapContentWidth
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.outlined.Assignment
import androidx.compose.material.icons.outlined.CheckCircleOutline
import androidx.compose.material.icons.outlined.EventBusy
import androidx.compose.material.icons.outlined.Schedule
import androidx.compose.material.icons.outlined.Sync
import androidx.compose.material.icons.outlined.WarningAmber
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.VerticalDivider
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.AbsoluteAlignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.luminance
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.layout.onGloballyPositioned
import androidx.compose.ui.layout.positionInWindow
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.SpanStyle
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.rememberTextMeasurer
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.text.style.TextDirection
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.withStyle
import androidx.compose.ui.unit.LayoutDirection
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.nags.operations.R
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.flowOf
import com.nags.operations.data.FlightListItem
import com.nags.operations.ui.flights.FlightCardReportStatus
import com.nags.operations.ui.flights.cardReportStatus
import com.nags.operations.ui.flights.cardStaMinutes
import com.nags.operations.ui.util.formatFlightDateRangeForDisplay
import com.nags.operations.ui.util.stationTimeZone
import com.nags.operations.ui.util.formatIsoTimeForDisplay
import com.nags.operations.ui.util.parseOffsetDateTime
import java.time.Instant

@Composable
fun FlightCard(
    flight: FlightListItem,
    onClick: (() -> Unit)?,
    modifier: Modifier = Modifier,
    loadCustomerLogo: (String) -> Flow<ByteArray?> = { flowOf(null) },
    now: Instant = Instant.now(),
    reportStatus: FlightCardReportStatus = flight.cardReportStatus(now = now),
    pendingSyncLabel: String? = null,
    hasSyncError: Boolean = false,
    cancellationAtUtc: String? = flight.latestCancellationAtUtc,
    onActiveWorkClick: (() -> Unit)? = null,
    onCompletedWorkClick: (() -> Unit)? = null,
    onFlightNumberPositioned: ((Float, Int) -> Unit)? = null,
) {
    val statusColors = flightStatusColors(reportStatus)
    val content: @Composable ColumnScope.() -> Unit = {
        Column(
            modifier = Modifier.fillMaxWidth()
                .drawBehind {
                    val stripeWidth = 3.dp.toPx()
                    drawRect(
                        color = statusColors.stripe,
                        topLeft = Offset(if (layoutDirection == LayoutDirection.Rtl) size.width - stripeWidth else 0f, 0f),
                        size = Size(stripeWidth, size.height),
                    )
                }
                .padding(horizontal = 12.dp, vertical = 10.dp),
            verticalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            Row(
                horizontalArrangement = Arrangement.spacedBy(8.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                CustomerLogo(flight.customerId, loadCustomerLogo, Modifier.size(32.dp))
                Column(Modifier.weight(1f)) {
                    BoxWithConstraints(Modifier.fillMaxWidth()) {
                        val badgeWidth = maxWidth * 0.64f
                        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp),
                            verticalAlignment = Alignment.CenterVertically) {
                            Text(
                                flight.flightNumber.ifBlank {
                                    flight.customerIataCode?.takeIf { it.isNotBlank() } ?: "—"
                                },
                                modifier = Modifier.weight(1f).onGloballyPositioned {
                                    onFlightNumberPositioned?.invoke(it.positionInWindow().y, it.size.height)
                                },
                                style = MaterialTheme.typography.titleMedium.copy(fontSize = 18.sp),
                                fontWeight = FontWeight.Bold,
                                maxLines = 1,
                                overflow = TextOverflow.Ellipsis,
                            )
                            FlightReportBadge(reportStatus, Modifier.widthIn(max = badgeWidth))
                        }
                    }
                    Text(
                        flight.customerName,
                        modifier = Modifier.fillMaxWidth(),
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                    )
                }
            }
            FlightAircraftOperationRow(flight.aircraftTypeModel, flight.operationTypeName,
                flight.isPerLanding, flight.latestReturnToRampSequence, flight.hasOpenReturnToRamp == true)
            FlightCardSummaryRow(flight, reportStatus, now, pendingSyncLabel, hasSyncError, cancellationAtUtc,
                onActiveWorkClick, onCompletedWorkClick)
            HorizontalDivider(color = flightCardDividerColor())
            FlightCardScheduleFooter(flight)
        }
    }
    if (onClick == null) Card(
        modifier = modifier.fillMaxWidth(), shape = RoundedCornerShape(16.dp),
        border = BorderStroke(1.dp, statusColors.border),
        elevation = CardDefaults.cardElevation(defaultElevation = 0.dp),
        colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surface), content = content,
    ) else Card(
        onClick = onClick, modifier = modifier.fillMaxWidth(), shape = RoundedCornerShape(16.dp),
        border = BorderStroke(1.dp, statusColors.border),
        elevation = CardDefaults.cardElevation(defaultElevation = 1.dp, pressedElevation = 4.dp),
        colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surface), content = content,
    )
}

@Composable
private fun FlightCardScheduleFooter(flight: FlightListItem) {
    val zone = stationTimeZone(flight.stationTimeZoneId)
    val date = formatFlightDateRangeForDisplay(flight.scheduledArrivalUtc, flight.scheduledDepartureUtc, zone)
    val sta = formatIsoTimeForDisplay(flight.scheduledArrivalUtc, zone)
    val std = formatIsoTimeForDisplay(flight.scheduledDepartureUtc, zone)
    val staLabel = stringResource(R.string.flights_card_sta)
    val stdLabel = stringResource(R.string.flights_card_std)
    val dateStyle = MaterialTheme.typography.bodySmall.copy(fontSize = 11.sp, fontWeight = FontWeight.Medium,
        textDirection = TextDirection.Content)
    val timeStyle = MaterialTheme.typography.bodySmall.copy(fontWeight = FontWeight.Bold)
    val measurer = rememberTextMeasurer()
    val density = LocalDensity.current
    val requiredWidth = measurer.measure(date, dateStyle).size.width +
        measurer.measure("$staLabel $sta", timeStyle).size.width +
        measurer.measure("$stdLabel $std", timeStyle).size.width + with(density) { 25.dp.toPx() }
    BoxWithConstraints(Modifier.fillMaxWidth()) {
        if (requiredWidth > constraints.maxWidth) {
            Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                Text(date, style = dateStyle, maxLines = 2, overflow = TextOverflow.Ellipsis)
                FlightCardScheduledTimes(staLabel, sta, stdLabel, std, Modifier.fillMaxWidth())
            }
        } else Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp),
            verticalAlignment = Alignment.CenterVertically) {
            Text(date, Modifier.weight(1f).wrapContentWidth(Alignment.Start), style = dateStyle, maxLines = 1, overflow = TextOverflow.Ellipsis)
            FlightCardScheduledTimes(staLabel, sta, stdLabel, std)
        }
    }
}

@Composable
private fun FlightCardScheduledTimes(staLabel: String, sta: String, stdLabel: String, std: String,
    modifier: Modifier = Modifier) {
    Row(modifier, horizontalArrangement = Arrangement.spacedBy(8.dp, Alignment.End), verticalAlignment = Alignment.CenterVertically) {
        FlightCardField(staLabel, sta)
        VerticalDivider(Modifier.height(14.dp), color = flightCardDividerColor())
        FlightCardField(stdLabel, std)
    }
}

@Composable
internal fun FlightAircraftOperationRow(aircraftModel: String?, operationName: String,
    isPerLanding: Boolean = false, returnToRampSequence: Int? = null, openReturnToRamp: Boolean = false) {
    val labelColor = if (MaterialTheme.colorScheme.surface.luminance() < 0.5f) Color(0xFFB2B7C2) else Color(0xFF626873)
    val aircraftLabel = stringResource(R.string.flights_card_aircraft)
    val operationLabel = stringResource(R.string.flights_operation)
    val aircraft = aircraftModel?.takeIf { it.isNotBlank() } ?: stringResource(R.string.flights_card_aircraft_unavailable)
    val rtrLabel = returnToRampSequence?.let {
        stringResource(if (openReturnToRamp) R.string.flight_card_rtr_number else R.string.flight_card_after_rtr, it)
    }
    val measurer = rememberTextMeasurer()
    val density = LocalDensity.current
    val measureStyle = MaterialTheme.typography.bodySmall.copy(fontWeight = FontWeight.Bold)
    val perLandingStyle = MaterialTheme.typography.labelSmall.copy(fontSize = 10.sp, lineHeight = 12.sp,
        letterSpacing = 0.1.sp, fontWeight = FontWeight.SemiBold)
    val designationWidth = if (isPerLanding) minOf(with(density) { 72.dp.toPx() },
        measurer.measure(stringResource(R.string.flights_per_landing), perLandingStyle).size.width + with(density) { 10.dp.toPx() }) else 0f
    val itemCount = 3 + (if (rtrLabel != null) 1 else 0) + (if (isPerLanding) 1 else 0)
    val requiredWidth = measurer.measure("$aircraftLabel $aircraft", measureStyle).size.width +
        measurer.measure("$operationLabel $operationName", measureStyle).size.width +
        (rtrLabel?.let { measurer.measure(it, measureStyle).size.width } ?: 0) + designationWidth +
        with(density) { (1.dp + 8.dp * (itemCount - 1)).toPx() }
    BoxWithConstraints(Modifier.fillMaxWidth()) {
        if (requiredWidth > constraints.maxWidth) {
            Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
                    FlightCardField(aircraftLabel, aircraft, Modifier.weight(1f).wrapContentWidth(Alignment.Start), labelColor)
                    if (isPerLanding) PerLandingBadge()
                }
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
                    FlightCardField(operationLabel, operationName, Modifier.weight(1f).wrapContentWidth(Alignment.Start), labelColor)
                    if (rtrLabel != null) FlightCardRtrLabel(rtrLabel)
                }
            }
        } else Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
            FlightCardField(aircraftLabel, aircraft, labelColor = labelColor)
            VerticalDivider(Modifier.height(14.dp), color = flightCardDividerColor())
            FlightCardField(operationLabel, operationName, labelColor = labelColor)
            if (rtrLabel != null) FlightCardRtrLabel(rtrLabel)
            if (isPerLanding) PerLandingBadge()
        }
    }
}

@Composable
private fun FlightCardRtrLabel(label: String) {
    Text(label, style = MaterialTheme.typography.bodySmall.copy(textDirection = TextDirection.Content),
        fontWeight = FontWeight.Medium, maxLines = 1, overflow = TextOverflow.Ellipsis)
}

@Composable
private fun flightCardDividerColor(): Color =
    if (MaterialTheme.colorScheme.surface.luminance() < 0.5f) Color(0xFF494949) else Color(0xFFE0E0E0)

@Composable
private fun FlightCardField(label: String, value: String, modifier: Modifier = Modifier, labelColor: Color = Color.Unspecified) {
    Text(
        buildAnnotatedString {
            if (label.isNotBlank()) withStyle(SpanStyle(color = labelColor)) { append(label); append(" ") }
            withStyle(SpanStyle(fontWeight = FontWeight.Bold, color = MaterialTheme.colorScheme.onSurface)) {
                append(value)
            }
        },
        modifier = modifier,
        style = MaterialTheme.typography.bodySmall.copy(textDirection = TextDirection.Content),
        color = MaterialTheme.colorScheme.onSurfaceVariant,
        maxLines = 1,
        overflow = TextOverflow.Ellipsis,
    )
}

@Composable
private fun FlightCardSummaryRow(flight: FlightListItem, status: FlightCardReportStatus, now: Instant,
    pendingSyncLabel: String?, hasSyncError: Boolean, cancellationAtUtc: String?,
    onActiveWorkClick: (() -> Unit)?, onCompletedWorkClick: (() -> Unit)?) {
    val active = flight.activeWorkCount?.takeIf { it >= 0 }
    val done = flight.completedWorkCount?.takeIf { it >= 0 }
    val minutes = flight.cardStaMinutes(now)
    val workVisible = status == FlightCardReportStatus.OnGround || status == FlightCardReportStatus.ReturnedToRamp ||
        status == FlightCardReportStatus.Departed
    val text = when (status) {
        FlightCardReportStatus.DueSoon -> minutes?.let { stringResource(R.string.flight_card_sta_in, cardDuration(it.coerceAtLeast(1))) }
        FlightCardReportStatus.ArrivalNotRecorded -> minutes?.let {
            if (it >= 0) stringResource(R.string.flight_card_sta_now)
            else stringResource(R.string.flight_card_sta_ago, cardDuration(-it))
        }
        FlightCardReportStatus.Canceled -> cancellationAtUtc?.takeIf {
            runCatching { parseOffsetDateTime(it) }.isSuccess
        }?.let { stringResource(R.string.flight_card_canceled_at, formatIsoTimeForDisplay(it, stationTimeZone(flight.stationTimeZoneId))) }
        FlightCardReportStatus.Conflict -> stringResource(R.string.flight_card_conflicting_reports)
        else -> if (workVisible && active != null && done != null) when {
            active > 0 && done > 0 -> stringResource(R.string.flight_card_work_active_done, active, done)
            active > 0 -> stringResource(R.string.flight_card_work_active, active)
            done > 0 && status != FlightCardReportStatus.Departed -> stringResource(R.string.flight_card_work_done_no_active, done)
            done > 0 -> stringResource(R.string.flight_card_work_done, done)
            else -> if (pendingSyncLabel == null) stringResource(R.string.flight_card_no_work) else null
        } else null
    }
    if (text == null && pendingSyncLabel == null) return
    val staTiming = status == FlightCardReportStatus.DueSoon || status == FlightCardReportStatus.ArrivalNotRecorded
    val summaryStyle = if (staTiming) MaterialTheme.typography.bodySmall.copy(
        fontSize = 11.sp, lineHeight = 14.sp, fontWeight = FontWeight.Bold, textDirection = TextDirection.Content)
    else MaterialTheme.typography.bodySmall.copy(textDirection = TextDirection.Content)
    val color = when {
        status == FlightCardReportStatus.Canceled || status == FlightCardReportStatus.DueSoon ||
            status == FlightCardReportStatus.ArrivalNotRecorded || status == FlightCardReportStatus.Conflict -> flightStatusColors(status).foreground
        workVisible && (active ?: 0) > 0 -> flightStatusColors(FlightCardReportStatus.OnGround).foreground
        else -> MaterialTheme.colorScheme.onSurfaceVariant
    }
    val icon = when {
        status == FlightCardReportStatus.Canceled -> Icons.Outlined.EventBusy
        status == FlightCardReportStatus.Conflict -> Icons.Outlined.WarningAmber
        workVisible && (active ?: 0) == 0 && (done ?: 0) == 0 -> Icons.AutoMirrored.Outlined.Assignment
        workVisible && (active ?: 0) == 0 -> Icons.Outlined.CheckCircleOutline
        else -> Icons.Outlined.Schedule
    }
    BoxWithConstraints(Modifier.fillMaxWidth()) {
        val noticeWidth = if (text != null) maxWidth * 0.5f else maxWidth
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
            if (text != null) Row(Modifier.weight(1f),
                horizontalArrangement = Arrangement.spacedBy(4.dp, if (staTiming) AbsoluteAlignment.Left else Alignment.Start),
                verticalAlignment = Alignment.CenterVertically) {
                Icon(icon, null, Modifier.size(if (staTiming) 12.dp else 13.dp), tint = color)
                if (workVisible && (onActiveWorkClick != null || onCompletedWorkClick != null) &&
                    ((active ?: 0) > 0 || (done ?: 0) > 0)) {
                    Row(Modifier.weight(1f), horizontalArrangement = Arrangement.spacedBy(4.dp), verticalAlignment = Alignment.CenterVertically) {
                        if ((active ?: 0) > 0) Text(stringResource(R.string.flight_card_work_active, active!!),
                            Modifier.weight(1f, fill = false).then(if (onActiveWorkClick != null) Modifier.clickable(role = Role.Button, onClick = onActiveWorkClick) else Modifier),
                            style = summaryStyle, color = color, maxLines = 1, overflow = TextOverflow.Ellipsis)
                        if ((active ?: 0) > 0 && (done ?: 0) > 0) Text("·", style = summaryStyle, color = color)
                        if ((done ?: 0) > 0) Text(stringResource(R.string.flight_card_work_done, done!!),
                            Modifier.weight(1f, fill = false).then(if (onCompletedWorkClick != null) Modifier.clickable(role = Role.Button, onClick = onCompletedWorkClick) else Modifier),
                            style = summaryStyle, color = color, maxLines = 1, overflow = TextOverflow.Ellipsis)
                    }
                } else Text(text, style = summaryStyle, color = color, maxLines = 1, overflow = TextOverflow.Ellipsis)
            } else Spacer(Modifier.weight(1f))
            if (pendingSyncLabel != null) {
                val syncColor = if (hasSyncError) MaterialTheme.colorScheme.error
                    else flightStatusColors(FlightCardReportStatus.DueSoon).foreground
                Row(Modifier.widthIn(max = noticeWidth),
                    horizontalArrangement = Arrangement.spacedBy(4.dp), verticalAlignment = Alignment.CenterVertically) {
                    Icon(if (hasSyncError) Icons.Outlined.WarningAmber else Icons.Outlined.Sync, null, Modifier.size(13.dp), tint = syncColor)
                    Text(pendingSyncLabel, style = MaterialTheme.typography.bodySmall.copy(textDirection = TextDirection.Content), color = syncColor,
                        maxLines = 1, overflow = TextOverflow.Ellipsis)
                }
            }
        }
    }
}

@Composable
private fun cardDuration(minutes: Long): String = when {
    minutes < 60 -> stringResource(R.string.flight_card_duration_minutes, minutes)
    minutes % 60 == 0L -> stringResource(R.string.flight_card_duration_hours, minutes / 60)
    else -> stringResource(R.string.flight_card_duration_hours_minutes, minutes / 60, minutes % 60)
}
