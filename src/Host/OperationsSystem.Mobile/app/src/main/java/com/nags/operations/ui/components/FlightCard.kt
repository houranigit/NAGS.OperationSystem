package com.nags.operations.ui.components

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.CloudQueue
import androidx.compose.material.icons.filled.ErrorOutline
import androidx.compose.material.icons.filled.FlightTakeoff
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.nags.operations.data.FlightStatusKind
import com.nags.operations.data.MobileFlightDto
import com.nags.operations.data.WorkOrderStatusKind
import com.nags.operations.data.sync.OutboxOpStatus
import com.nags.operations.data.sync.PendingDisplayItem
import com.nags.operations.ui.common.color
import com.nags.operations.ui.common.label
import com.nags.operations.ui.util.formatIsoForDisplay

@Composable
fun FlightCard(
    flight: MobileFlightDto,
    pending: PendingDisplayItem?,
    onClick: () -> Unit,
    hasLocalDraft: Boolean = false,
    modifier: Modifier = Modifier,
) {
    val flightStatus = FlightStatusKind.fromWire(flight.status)
    val woStatus = WorkOrderStatusKind.fromWire(flight.myWorkOrder?.status)
    val stripeColor = when (pending?.status) {
        OutboxOpStatus.Failed -> MaterialTheme.colorScheme.error
        OutboxOpStatus.Pending,
        OutboxOpStatus.Sending -> Color(0xFFFFA000)
        else -> flightStatus.color()
    }

    Card(
        modifier = modifier
            .fillMaxWidth()
            .clickable(onClick = onClick),
        elevation = CardDefaults.cardElevation(defaultElevation = 1.dp, pressedElevation = 4.dp),
        shape = RoundedCornerShape(18.dp),
        colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surface),
    ) {
        Row(modifier = Modifier.fillMaxWidth().heightIn(min = 140.dp)) {
            Box(
                modifier = Modifier
                    .width(6.dp)
                    .background(stripeColor)
                    .height(160.dp),
            )
            Column(modifier = Modifier.padding(16.dp).fillMaxWidth()) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Box(
                        modifier = Modifier
                            .size(36.dp)
                            .clip(RoundedCornerShape(10.dp))
                            .background(MaterialTheme.colorScheme.primaryContainer.copy(alpha = 0.5f)),
                        contentAlignment = Alignment.Center,
                    ) {
                        Icon(
                            Icons.Default.FlightTakeoff,
                            contentDescription = null,
                            tint = MaterialTheme.colorScheme.primary,
                            modifier = Modifier.size(20.dp),
                        )
                    }
                    Spacer(Modifier.width(12.dp))
                    Column(modifier = Modifier.weight(1f)) {
                        Text(
                            flight.flightNumber.ifBlank { "—" },
                            style = MaterialTheme.typography.titleMedium,
                            fontWeight = FontWeight.SemiBold,
                            color = MaterialTheme.colorScheme.onSurface,
                        )
                        Text(
                            flight.customerName,
                            style = MaterialTheme.typography.bodyMedium,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                    }
                    Spacer(Modifier.width(8.dp))
                    Column(
                        horizontalAlignment = Alignment.End,
                        verticalArrangement = Arrangement.spacedBy(6.dp),
                    ) {
                        StatusChip(text = flightStatus.label(), color = flightStatus.color())
                        if (hasLocalDraft) {
                            StatusChip(
                                text = "Draft",
                                color = MaterialTheme.colorScheme.tertiary,
                            )
                        }
                    }
                }

                if (pending != null) {
                    Spacer(Modifier.height(10.dp))
                    PendingChip(pending)
                } else if (woStatus != null) {
                    Spacer(Modifier.height(10.dp))
                    Row(
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(8.dp),
                    ) {
                        val color = woStatus.color() ?: stripeColor
                        StatusChip(text = woStatus.label() ?: "", color = color, outlined = true)
                    }
                }

                Spacer(Modifier.height(12.dp))
                Row(
                    horizontalArrangement = Arrangement.spacedBy(16.dp),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    InfoColumn(label = "Station", value = flight.stationIata.ifBlank { "—" })
                    InfoColumn(label = "Operation", value = flight.operationTypeName.ifBlank { "—" })
                    InfoColumn(
                        label = "Aircraft",
                        value = flight.aircraftTypeModel?.takeIf { it.isNotBlank() } ?: "—",
                    )
                }
                Spacer(Modifier.height(8.dp))
                Text(
                    "STA ${formatIsoForDisplay(flight.scheduledArrivalUtc)}  →  STD ${formatIsoForDisplay(flight.scheduledDepartureUtc)}",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        }
    }
}

@Composable
fun PendingChip(pending: PendingDisplayItem) {
    val (label, color) = when (pending.status) {
        OutboxOpStatus.Pending -> "Pending sync" to Color(0xFFFFA000)
        OutboxOpStatus.Sending -> "Syncing…" to Color(0xFF1976D2)
        OutboxOpStatus.Failed -> "Sync failed — tap to review" to Color(0xFFC62828)
    }
    Surface(
        shape = RoundedCornerShape(50),
        color = color.copy(alpha = 0.14f),
        contentColor = color,
        border = BorderStroke(1.dp, color.copy(alpha = 0.45f)),
    ) {
        Row(
            modifier = Modifier.padding(horizontal = 12.dp, vertical = 6.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(6.dp),
        ) {
            if (pending.status == OutboxOpStatus.Sending) {
                CircularProgressIndicator(
                    modifier = Modifier.size(12.dp),
                    strokeWidth = 1.5.dp,
                    color = color,
                )
            } else {
                Icon(
                    if (pending.status == OutboxOpStatus.Failed)
                        Icons.Default.ErrorOutline else Icons.Default.CloudQueue,
                    contentDescription = null,
                    modifier = Modifier.size(14.dp),
                )
            }
            Text(
                text = label,
                style = MaterialTheme.typography.labelMedium,
                fontWeight = FontWeight.Medium,
            )
        }
    }
}

@Composable
fun InfoColumn(label: String, value: String) {
    Column {
        Text(
            label.uppercase(),
            style = MaterialTheme.typography.labelSmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
        Text(
            value,
            style = MaterialTheme.typography.bodyMedium,
            fontWeight = FontWeight.Medium,
            color = MaterialTheme.colorScheme.onSurface,
        )
    }
}

@Composable
fun StatusChip(text: String, color: Color, outlined: Boolean = false) {
    Surface(
        shape = RoundedCornerShape(50),
        color = if (outlined) Color.Transparent else color.copy(alpha = 0.14f),
        contentColor = color,
        border = if (outlined) BorderStroke(1.dp, color.copy(alpha = 0.6f)) else null,
    ) {
        Text(
            text = text,
            modifier = Modifier.padding(horizontal = 10.dp, vertical = 4.dp),
            style = MaterialTheme.typography.labelMedium,
            fontWeight = FontWeight.Medium,
        )
    }
}
