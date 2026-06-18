package com.nags.operations.ui.components

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.NoteAdd
import androidx.compose.material.icons.automirrored.filled.Undo
import androidx.compose.material.icons.filled.Cancel
import androidx.compose.material.icons.filled.CloudOff
import androidx.compose.material.icons.filled.EditNote
import androidx.compose.material.icons.filled.GroupAdd
import androidx.compose.material.icons.filled.Lock
import androidx.compose.material3.Button
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.font.FontWeight
import com.nags.operations.data.FlightStatusKind
import com.nags.operations.data.MobileFlightSummaryDto
import com.nags.operations.data.WorkOrderStatusKind
import com.nags.operations.ui.flights.FlightSummaryActionsDecision
import com.nags.operations.ui.flights.deriveFlightSummaryActions
import com.nags.operations.ui.util.formatIsoForDisplay
import com.nags.operations.ui.util.offsetSameAsFlight

data class FlightSheetCallbacks(
    val onCreateWorkOrder: (flightId: String) -> Unit = {},
    /** Resume locally saved draft (routes to draft screen, not blank create flow). */
    val onCompleteWorkOrderDraft: (draftId: String) -> Unit = {},
    val onOpenWorkOrder: (flightId: String) -> Unit = {},
    val onInviteTeammate: (flightId: String) -> Unit = {},
    /** Queue a flight cancellation with the employee-chosen cancellation time (ISO-8601). */
    val onCancelFlight: (flightId: String, canceledAtIso: String) -> Unit = { _, _ -> },
    val onReturnToRamp: (flightId: String) -> Unit = {},
)

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun FlightDetailsActionsSheet(
    flight: MobileFlightSummaryDto,
    localDraftId: String? = null,
    isOnline: Boolean = true,
    showInvite: Boolean = false,
    callbacks: FlightSheetCallbacks,
    onDismiss: () -> Unit,
) {
    val sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true)
    val decision = deriveFlightSummaryActions(flight)
    var showCancelDialog by remember { mutableStateOf(false) }
    // True when the employee's under-review work order is itself a cancellation: the
    // "update" action edits the cancellation time (dialog) instead of opening the regular
    // work-order form, and return-to-ramp is meaningless for a cancelled flight.
    val myWorkOrderIsCancellation = flight.myWorkOrder?.isCanceled == true

    ModalBottomSheet(
        onDismissRequest = onDismiss,
        sheetState = sheetState,
    ) {
        Column(
            Modifier
                .fillMaxWidth()
                .verticalScroll(rememberScrollState())
                .padding(start = 20.dp, end = 20.dp, bottom = 32.dp),
            verticalArrangement = Arrangement.spacedBy(16.dp),
        ) {
            FlightOverviewBanner(
                customerIataCode = flight.customerIataCode,
                customerName = flight.customerName,
                stationCode = flight.stationCode,
                operationTypeCode = flight.operationTypeCode,
                flightNumber = flight.flightNumber.takeIf { it.isNotBlank() },
                aircraftModel = flight.aircraftModel,
                staDisplay = formatIsoForDisplay(flight.sta),
                stdDisplay = formatIsoForDisplay(flight.std),
            )

            when {
                localDraftId != null -> {
                    SheetActionButton(
                        icon = Icons.Default.EditNote,
                        label = "Complete work order",
                        onClick = { callbacks.onCompleteWorkOrderDraft(localDraftId) },
                        primary = true,
                    )
                }
                decision == FlightSummaryActionsDecision.ReadOnly -> {
                    SectionHeader(
                        title = "This flight is closed",
                        subtitle = "No further actions are available from the mobile app.",
                        icon = Icons.Default.Lock,
                    )
                }
                decision == FlightSummaryActionsDecision.UpdateOrReturnToRamp -> {
                    if (myWorkOrderIsCancellation) {
                        SheetActionButton(
                            icon = Icons.Default.Cancel,
                            label = "Update cancellation",
                            onClick = { showCancelDialog = true },
                            primary = true,
                        )
                    } else {
                        SheetActionButton(
                            icon = Icons.Default.EditNote,
                            label = "Update work order",
                            onClick = {
                                callbacks.onCreateWorkOrder(flight.id)
                            },
                            primary = true,
                        )
                    }
                }
                else -> {
                    SheetActionButton(
                        icon = Icons.AutoMirrored.Filled.NoteAdd,
                        label = "Create work order",
                        onClick = {
                            callbacks.onCreateWorkOrder(flight.id)
                        },
                        primary = true,
                    )
                }
            }
            val flightInProgress = FlightStatusKind.fromValue(flight.status) == FlightStatusKind.InProgress
            val workOrderUnderReview =
                WorkOrderStatusKind.fromValue(flight.myWorkOrder?.status) == WorkOrderStatusKind.UnderReview
            if (flightInProgress && workOrderUnderReview && !myWorkOrderIsCancellation) {
                SheetActionButton(
                    icon = Icons.AutoMirrored.Filled.Undo,
                    label = "Return to ramp",
                    onClick = {
                        callbacks.onReturnToRamp(flight.id)
                        onDismiss()
                    },
                    primary = false,
                )
            }

            val flightStatus = FlightStatusKind.fromValue(flight.status)
            val canInvite = showInvite &&
                decision != FlightSummaryActionsDecision.ReadOnly &&
                (flightStatus == FlightStatusKind.Scheduled || flightStatus == FlightStatusKind.InProgress)
            if (canInvite) {
                SheetActionButton(
                    icon = Icons.Default.GroupAdd,
                    label = "Invite teammates",
                    onClick = {
                        callbacks.onInviteTeammate(flight.id)
                        onDismiss()
                    },
                    primary = false,
                    enabled = isOnline,
                )
                if (!isOnline) {
                    Row(
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(8.dp),
                    ) {
                        Icon(
                            Icons.Default.CloudOff,
                            contentDescription = null,
                            tint = MaterialTheme.colorScheme.onSurfaceVariant,
                            modifier = Modifier.size(18.dp),
                        )
                        Text(
                            "Inviting teammates needs a connection. Reconnect to invite.",
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                    }
                }
            }

            // Cancel is offered only for flights that can still be cancelled (Scheduled, or
            // InProgress with no under-review / other work order) — mirrors the server, which
            // rejects cancelling a flight that already has an accepted work order. A pending
            // local draft means the employee is mid-create, so cancel is hidden there too.
            if (localDraftId == null && decision == FlightSummaryActionsDecision.CreateOrCancel) {
                SheetActionButton(
                    icon = Icons.Default.Cancel,
                    label = "Cancel work order",
                    onClick = { showCancelDialog = true },
                    primary = false,
                )
            }
            Spacer(Modifier.height(4.dp))
        }
    }

    if (showCancelDialog) {
        CancelFlightDialog(
            flightStdIso = flight.std,
            flightOffset = offsetSameAsFlight(flight.std),
            initialCanceledAtIso = flight.myWorkOrder?.canceledAt,
            isUpdate = myWorkOrderIsCancellation,
            onDismiss = { showCancelDialog = false },
            onConfirm = { canceledAtIso ->
                showCancelDialog = false
                callbacks.onCancelFlight(flight.id, canceledAtIso)
                onDismiss()
            },
        )
    }
}

@Composable
private fun SectionHeader(
    title: String,
    subtitle: String,
    icon: ImageVector? = null,
) {
    Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
        if (icon != null) {
            Icon(
                icon,
                contentDescription = null,
                tint = MaterialTheme.colorScheme.primary,
                modifier = Modifier
                    .padding(bottom = 2.dp)
                    .size(28.dp),
            )
        }
        Text(
            title,
            style = MaterialTheme.typography.titleMedium,
            fontWeight = FontWeight.SemiBold,
        )
        Text(
            subtitle,
            style = MaterialTheme.typography.bodyMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
    }
}

@Composable
private fun SheetActionButton(
    icon: ImageVector,
    label: String,
    onClick: () -> Unit,
    primary: Boolean = false,
    enabled: Boolean = true,
) {
    if (primary) {
        Button(
            onClick = onClick,
            enabled = enabled,
            modifier = Modifier
                .fillMaxWidth()
                .height(54.dp),
            shape = RoundedCornerShape(14.dp),
        ) {
            Row(
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(10.dp),
            ) {
                Icon(icon, contentDescription = null)
                Text(label, fontWeight = FontWeight.SemiBold)
            }
        }
    } else {
        OutlinedButton(
            onClick = onClick,
            enabled = enabled,
            modifier = Modifier
                .fillMaxWidth()
                .height(54.dp),
            shape = RoundedCornerShape(14.dp),
        ) {
            Row(
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(10.dp),
            ) {
                Icon(icon, contentDescription = null)
                Text(label, fontWeight = FontWeight.Medium)
            }
        }
    }
}
