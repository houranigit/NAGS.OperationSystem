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
import com.nags.operations.data.MobileFlightDto
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
    /**
     * Durably queue a flight cancellation, then report whether the local outbox write succeeded.
     * The sheet remains open until [onFinished] reports success so an enqueue failure cannot be
     * mistaken for a submitted cancellation.
     */
    val onCancelFlight: (
        flightId: String,
        canceledAtIso: String,
        reason: String,
        onFinished: (success: Boolean, message: String?) -> Unit,
    ) -> Unit = { _, _, _, onFinished ->
        onFinished(false, "Cancellation is unavailable.")
    },
    val onReturnToRamp: (flightId: String) -> Unit = {},
)

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun FlightDetailsActionsSheet(
    flight: MobileFlightDto,
    localDraftId: String? = null,
    isOnline: Boolean = true,
    showInvite: Boolean = false,
    callbacks: FlightSheetCallbacks,
    onDismiss: () -> Unit,
) {
    val sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true)
    val decision = deriveFlightSummaryActions(flight)
    var showCancelDialog by remember(flight.id) { mutableStateOf(false) }
    var cancelSubmitting by remember(flight.id) { mutableStateOf(false) }
    var cancelError by remember(flight.id) { mutableStateOf<String?>(null) }
    // True when the caller's work order is itself a cancellation: the "update" action edits
    // the cancellation details (dialog) instead of opening the regular work-order form, and
    // return-to-ramp is meaningless for a cancelled flight.
    val myWorkOrderIsCancellation = flight.myWorkOrder?.type == "Cancellation"

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
                customerIataCode = flight.customerIataCode.orEmpty(),
                customerName = flight.customerName,
                stationCode = flight.stationIata,
                operationTypeCode = flight.operationTypeName,
                flightNumber = flight.flightNumber.takeIf { it.isNotBlank() },
                aircraftModel = flight.aircraftTypeModel,
                staDisplay = formatIsoForDisplay(flight.scheduledArrivalUtc),
                stdDisplay = formatIsoForDisplay(flight.scheduledDepartureUtc),
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
                            onClick = {
                                cancelError = null
                                showCancelDialog = true
                            },
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
            val flightInProgress = FlightStatusKind.fromWire(flight.status) == FlightStatusKind.InProgress
            val workOrderEditable =
                WorkOrderStatusKind.fromWire(flight.myWorkOrder?.status)?.isEditable == true
            if (flightInProgress && workOrderEditable && !myWorkOrderIsCancellation) {
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

            val flightStatus = FlightStatusKind.fromWire(flight.status)
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
                    onClick = {
                        cancelError = null
                        showCancelDialog = true
                    },
                    primary = false,
                )
            }
            Spacer(Modifier.height(4.dp))
        }
    }

    if (showCancelDialog) {
        CancelFlightDialog(
            flightStdIso = flight.scheduledDepartureUtc,
            flightOffset = offsetSameAsFlight(flight.scheduledDepartureUtc),
            initialCanceledAtIso = flight.myWorkOrder?.canceledAtUtc,
            initialReason = flight.myWorkOrder?.cancellationReason,
            isUpdate = myWorkOrderIsCancellation,
            isSubmitting = cancelSubmitting,
            errorMessage = cancelError,
            onDismiss = {
                if (!cancelSubmitting) {
                    showCancelDialog = false
                    cancelError = null
                }
            },
            onConfirm = { canceledAtIso, reason ->
                cancelSubmitting = true
                cancelError = null
                callbacks.onCancelFlight(flight.id, canceledAtIso, reason) { success, message ->
                    cancelSubmitting = false
                    if (success) {
                        showCancelDialog = false
                        onDismiss()
                    } else {
                        cancelError = message ?: "Could not save the cancellation. Please try again."
                    }
                }
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
