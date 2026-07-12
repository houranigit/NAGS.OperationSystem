package com.nags.operations.ui.screens

import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilterChip
import androidx.compose.material3.FilterChipDefaults
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.nags.operations.data.FlightStatusKind
import com.nags.operations.ui.common.color
import com.nags.operations.ui.common.label
import com.nags.operations.ui.flights.MyFlightsViewModel

/** Status filter chips for My flights and Per Landing — excludes [FlightStatusKind.Completed]. */
internal val StandardFlightStatusFilterKinds: List<FlightStatusKind> =
    FlightStatusKind.entries.filter { it != FlightStatusKind.Completed }

/** Ad Hoc tab: same as [StandardFlightStatusFilterKinds] but without [FlightStatusKind.Scheduled]. */
internal val AdHocFlightStatusFilterKinds: List<FlightStatusKind> =
    StandardFlightStatusFilterKinds.filter { it != FlightStatusKind.Scheduled }

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun StatusChipFilter(
    label: String,
    selected: Boolean,
    color: Color,
    onClick: () -> Unit,
) {
    FilterChip(
        selected = selected,
        onClick = onClick,
        label = { Text(label, fontWeight = if (selected) FontWeight.SemiBold else FontWeight.Normal) },
        colors = FilterChipDefaults.filterChipColors(
            selectedContainerColor = color.copy(alpha = 0.15f),
            selectedLabelColor = color,
        ),
        border = FilterChipDefaults.filterChipBorder(
            enabled = true,
            selected = selected,
            borderColor = color.copy(alpha = 0.35f),
            selectedBorderColor = color,
            borderWidth = 1.dp,
            selectedBorderWidth = 1.dp,
        ),
    )
}

@Composable
fun MyFlightsStatusFilterRow(
    selected: FlightStatusKind?,
    quickFilter: MyFlightsViewModel.QuickFilter?,
    onStatusSelected: (FlightStatusKind?) -> Unit,
    onQuickFilterSelected: (MyFlightsViewModel.QuickFilter?) -> Unit,
) {
    val scrollState = rememberScrollState()
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .horizontalScroll(scrollState)
            .padding(horizontal = 16.dp, vertical = 4.dp),
        horizontalArrangement = Arrangement.spacedBy(8.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        StatusChipFilter(
            label = "All",
            selected = selected == null && quickFilter == null,
            color = MaterialTheme.colorScheme.primary,
            onClick = {
                onStatusSelected(null)
                onQuickFilterSelected(null)
            },
        )
        StatusChipFilter(
            label = "Pending",
            selected = quickFilter == MyFlightsViewModel.QuickFilter.Pending,
            color = Color(0xFFFFA000),
            onClick = {
                if (quickFilter == MyFlightsViewModel.QuickFilter.Pending) {
                    onQuickFilterSelected(null)
                } else {
                    onQuickFilterSelected(MyFlightsViewModel.QuickFilter.Pending)
                    onStatusSelected(null)
                }
            },
        )
        StandardFlightStatusFilterKinds.forEach { kind ->
            StatusChipFilter(
                label = kind.label(),
                selected = selected == kind,
                color = kind.color(),
                onClick = {
                    onStatusSelected(if (selected == kind) null else kind)
                },
            )
        }
    }
}

@Composable
fun PerLandingFlightsStatusFilterRow(
    selected: FlightStatusKind?,
    onStatusSelected: (FlightStatusKind?) -> Unit,
    filterKinds: List<FlightStatusKind> = StandardFlightStatusFilterKinds,
) {
    val scrollState = rememberScrollState()
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .horizontalScroll(scrollState)
            .padding(horizontal = 16.dp, vertical = 4.dp),
        horizontalArrangement = Arrangement.spacedBy(8.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        StatusChipFilter(
            label = "All",
            selected = selected == null,
            color = MaterialTheme.colorScheme.primary,
            onClick = { onStatusSelected(null) },
        )
        filterKinds.forEach { kind ->
            StatusChipFilter(
                label = kind.label(),
                selected = selected == kind,
                color = kind.color(),
                onClick = { onStatusSelected(if (selected == kind) null else kind) },
            )
        }
    }
}
