package com.nags.operations.ui.components

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.FlightTakeoff
import androidx.compose.material.icons.filled.LocationOn
import androidx.compose.material.icons.filled.Schedule
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.nags.operations.ui.theme.BrandRedDark
import com.nags.operations.ui.theme.BrandRedLight

/**
 * Mirrors [com.operations.mobile.ui.components.FlightInfoBanner] from the sibling app,
 * using the NAGS brand gradient instead of portal sky blues.
 */
@Composable
fun FlightOverviewBanner(
    customerIataCode: String,
    customerName: String,
    stationCode: String,
    operationTypeCode: String,
    flightNumber: String?,
    aircraftModel: String?,
    staDisplay: String,
    stdDisplay: String,
    modifier: Modifier = Modifier,
) {
    val customerLine = customerNameLine(customerName, customerIataCode)
    val flightLine = flightIdentifierLine(customerIataCode, flightNumber)
    val gradient = Brush.linearGradient(
        listOf(
            BrandRedDark,
            BrandRedLight.copy(alpha = 0.92f),
        ),
    )
    Card(
        modifier = modifier.fillMaxWidth(),
        shape = RoundedCornerShape(20.dp),
        elevation = CardDefaults.cardElevation(defaultElevation = 6.dp),
        colors = CardDefaults.cardColors(containerColor = Color.Transparent),
    ) {
        Box(
            modifier = Modifier
                .background(gradient)
                .fillMaxWidth()
                .padding(20.dp),
        ) {
            Column(
                Modifier.fillMaxWidth(),
                verticalArrangement = Arrangement.spacedBy(14.dp),
            ) {
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.spacedBy(10.dp),
                    verticalAlignment = Alignment.Top,
                ) {
                    Surface(
                        shape = RoundedCornerShape(12.dp),
                        color = Color.White.copy(alpha = 0.2f),
                    ) {
                        Icon(
                            Icons.Default.FlightTakeoff,
                            contentDescription = null,
                            tint = Color.White,
                            modifier = Modifier.padding(10.dp),
                        )
                    }
                    Column(modifier = Modifier.weight(1f)) {
                        Text(
                            customerLine,
                            style = MaterialTheme.typography.labelLarge,
                            color = Color.White.copy(alpha = 0.85f),
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis,
                        )
                        Text(
                            flightLine,
                            style = MaterialTheme.typography.headlineSmall,
                            fontWeight = FontWeight.Bold,
                            color = Color.White,
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis,
                        )
                    }

                    Surface(
                        shape = RoundedCornerShape(10.dp),
                        color = Color.White.copy(alpha = 0.22f),
                    ) {
                        Row(
                            modifier = Modifier.padding(horizontal = 12.dp, vertical = 8.dp),
                            verticalAlignment = Alignment.CenterVertically,
                            horizontalArrangement = Arrangement.spacedBy(6.dp),
                        ) {
                            Icon(
                                Icons.Default.LocationOn,
                                contentDescription = null,
                                tint = Color.White,
                                modifier = Modifier.size(20.dp),
                            )
                            Text(
                                stationCode.ifBlank { "—" },
                                style = MaterialTheme.typography.titleMedium,
                                fontWeight = FontWeight.Bold,
                                color = Color.White,
                                maxLines = 1,
                                overflow = TextOverflow.Ellipsis,
                            )
                        }
                    }
                }

                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.spacedBy(10.dp),
                ) {
                    OverviewMetaChip(
                        label = "Aircraft type",
                        value = aircraftModel?.takeIf { it.isNotBlank() } ?: "—",
                        modifier = Modifier.weight(1f),
                    )
                    OverviewMetaChip(
                        label = "Operation",
                        value = operationTypeCode.ifBlank { "—" },
                        modifier = Modifier.weight(1f),
                    )
                }

                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.spacedBy(12.dp),
                ) {
                    OverviewTimeTile(
                        label = "Arrival",
                        value = staDisplay,
                        modifier = Modifier.weight(1f),
                    )
                    OverviewTimeTile(
                        label = "Departure",
                        value = stdDisplay,
                        modifier = Modifier.weight(1f),
                    )
                }
            }
        }
    }
}

/** Customer display name only; falls back to IATA code when name is absent. */
private fun customerNameLine(name: String, fallbackIata: String): String {
    val customer = name.trim()
    val code = fallbackIata.trim()
    return when {
        customer.isNotEmpty() -> customer
        code.isNotEmpty() -> code
        else -> "—"
    }
}

/** E.g. `EK-HAJJ4`; falls back gracefully if one part is missing. */
private fun flightIdentifierLine(iata: String, flightNumber: String?): String {
    val code = iata.trim()
    val fn = flightNumber?.trim()?.takeIf { it.isNotEmpty() }
    return when {
        code.isNotEmpty() && fn != null -> "$code-$fn"
        fn != null -> fn
        code.isNotEmpty() -> code
        else -> "—"
    }
}

@Composable
private fun OverviewMetaChip(label: String, value: String, modifier: Modifier = Modifier) {
    Surface(
        shape = RoundedCornerShape(12.dp),
        color = Color.White.copy(alpha = 0.15f),
        modifier = modifier,
    ) {
        Column(modifier = Modifier.padding(horizontal = 12.dp, vertical = 10.dp)) {
            Text(
                label.uppercase(),
                style = MaterialTheme.typography.labelSmall,
                color = Color.White.copy(alpha = 0.75f),
            )
            Spacer(Modifier.height(2.dp))
            Text(
                value,
                style = MaterialTheme.typography.bodyLarge,
                fontWeight = FontWeight.SemiBold,
                color = Color.White,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }
    }
}

@Composable
private fun OverviewTimeTile(label: String, value: String, modifier: Modifier = Modifier) {
    Surface(
        modifier = modifier,
        shape = RoundedCornerShape(14.dp),
        color = Color.White.copy(alpha = 0.18f),
    ) {
        Row(
            modifier = Modifier.padding(12.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            Icon(
                Icons.Default.Schedule,
                contentDescription = null,
                tint = Color.White.copy(alpha = 0.9f),
            )
            Column {
                Text(
                    label,
                    style = MaterialTheme.typography.labelSmall,
                    color = Color.White.copy(alpha = 0.8f),
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                Text(
                    value,
                    style = MaterialTheme.typography.bodyMedium,
                    fontWeight = FontWeight.Medium,
                    color = Color.White,
                )
            }
        }
    }
}
