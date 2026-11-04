package com.nags.operations.ui.components

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.VerticalDivider
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.SpanStyle
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.text.withStyle
import androidx.compose.ui.unit.LayoutDirection
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.nags.operations.R
import com.nags.operations.data.FlightListItem
import com.nags.operations.ui.util.formatFlightDateRangeForDisplay
import com.nags.operations.ui.util.formatIsoTimeForDisplay

private val ScheduledBlue = Color(0xFF007AFF)

@Composable
fun FlightCard(
    flight: FlightListItem,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    loadCustomerLogo: suspend (String) -> ByteArray? = { null },
    logoRevision: Long = 0,
) {
    Card(
        onClick = onClick,
        modifier = modifier.fillMaxWidth(),
        shape = RoundedCornerShape(16.dp),
        border = BorderStroke(1.dp, MaterialTheme.colorScheme.tertiary.copy(alpha = 0.22f)),
        elevation = CardDefaults.cardElevation(defaultElevation = 1.dp, pressedElevation = 4.dp),
        colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surface),
    ) {
        Column(
            modifier = Modifier.fillMaxWidth()
                .drawBehind {
                    val stripeWidth = 3.dp.toPx()
                    drawRect(
                        color = ScheduledBlue,
                        topLeft = Offset(if (layoutDirection == LayoutDirection.Rtl) size.width - stripeWidth else 0f, 0f),
                        size = Size(stripeWidth, size.height),
                    )
                }
                .padding(start = 16.dp, end = 12.dp, top = 10.dp, bottom = 10.dp),
            verticalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            Row(
                horizontalArrangement = Arrangement.spacedBy(8.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                CustomerLogo(flight.customerId, logoRevision, loadCustomerLogo, Modifier.size(36.dp))
                Column(Modifier.weight(1f)) {
                    Row(horizontalArrangement = Arrangement.spacedBy(6.dp), verticalAlignment = Alignment.CenterVertically) {
                        Text(
                            flight.flightNumber.ifBlank {
                                flight.customerIataCode?.takeIf { it.isNotBlank() } ?: "—"
                            },
                            modifier = Modifier.weight(1f, fill = false),
                            style = MaterialTheme.typography.titleMedium.copy(fontSize = 18.sp),
                            fontWeight = FontWeight.Bold,
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis,
                        )
                        if (flight.isPerLanding) {
                            Surface(
                                modifier = Modifier.widthIn(max = 76.dp),
                                shape = RoundedCornerShape(6.dp),
                                color = MaterialTheme.colorScheme.primaryContainer,
                            ) {
                                Text(
                                    stringResource(R.string.flights_per_landing),
                                    modifier = Modifier.padding(horizontal = 6.dp, vertical = 3.dp),
                                    style = MaterialTheme.typography.labelSmall.copy(fontSize = 10.sp),
                                    maxLines = 1,
                                    overflow = TextOverflow.Ellipsis,
                                )
                            }
                        }
                    }
                    Text(
                        flight.customerName,
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                    )
                }
                Surface(
                    modifier = Modifier.widthIn(max = 92.dp),
                    shape = RoundedCornerShape(50),
                    color = ScheduledBlue.copy(alpha = 0.11f),
                ) {
                    Text(
                        stringResource(R.string.flights_scheduled),
                        modifier = Modifier.padding(horizontal = 9.dp, vertical = 4.dp),
                        style = MaterialTheme.typography.labelSmall,
                        fontWeight = FontWeight.SemiBold,
                        color = MaterialTheme.colorScheme.tertiary,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                    )
                }
            }
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(8.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                FlightCardField(
                    stringResource(R.string.flights_card_aircraft),
                    flight.aircraftTypeModel?.takeIf { it.isNotBlank() }
                        ?: stringResource(R.string.flights_card_aircraft_unavailable),
                    Modifier.weight(1f, fill = false),
                )
                VerticalDivider(Modifier.height(14.dp), color = MaterialTheme.colorScheme.tertiary.copy(alpha = 0.3f))
                FlightCardField(stringResource(R.string.flights_operation), flight.operationTypeName, Modifier.weight(1.5f, fill = false))
            }
            HorizontalDivider(color = MaterialTheme.colorScheme.tertiary.copy(alpha = 0.14f))
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(8.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Text(
                    formatFlightDateRangeForDisplay(flight.scheduledArrivalUtc, flight.scheduledDepartureUtc),
                    modifier = Modifier.weight(1f),
                    style = MaterialTheme.typography.bodySmall.copy(fontSize = 11.sp),
                    fontWeight = FontWeight.Medium,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                Row(
                    modifier = Modifier.weight(1.4f),
                    horizontalArrangement = Arrangement.spacedBy(8.dp, Alignment.End),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    FlightCardField(stringResource(R.string.flights_card_sta), formatIsoTimeForDisplay(flight.scheduledArrivalUtc),
                        Modifier.weight(1f, fill = false))
                    VerticalDivider(Modifier.height(14.dp), color = MaterialTheme.colorScheme.tertiary.copy(alpha = 0.3f))
                    FlightCardField(stringResource(R.string.flights_card_std), formatIsoTimeForDisplay(flight.scheduledDepartureUtc),
                        Modifier.weight(1f, fill = false))
                }
            }
        }
    }
}

@Composable
private fun FlightCardField(label: String, value: String, modifier: Modifier = Modifier) {
    Text(
        buildAnnotatedString {
            append(label)
            append(" ")
            withStyle(SpanStyle(fontWeight = FontWeight.Bold, color = MaterialTheme.colorScheme.onSurface)) {
                append(value)
            }
        },
        modifier = modifier,
        style = MaterialTheme.typography.bodySmall,
        color = MaterialTheme.colorScheme.onSurfaceVariant,
        maxLines = 1,
        overflow = TextOverflow.Ellipsis,
    )
}
