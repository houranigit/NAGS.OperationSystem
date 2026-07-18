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
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.font.FontWeight
import com.nags.operations.data.ApiException
import com.nags.operations.data.FlightStatusKind
import com.nags.operations.data.MobileFlightDto
import com.nags.operations.data.MobileFlightWindowPhase
import com.nags.operations.data.WorkOrderStatusKind
import com.nags.operations.data.areMobileActionsAvailable
import com.nags.operations.data.evaluateMobileWindow
import com.nags.operations.ui.flights.FlightSummaryActionsDecision
import com.nags.operations.ui.flights.deriveFlightSummaryActions
import com.nags.operations.ui.util.formatIsoForDisplay
import com.nags.operations.ui.util.offsetSameAsFlight
import java.time.Duration
import java.time.Instant
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.delay

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
    /** Re-fetches notification detail before this sheet may transition from information to action. */
    onRevalidateFlight: (suspend (flightId: String) -> MobileFlightDto?)? = null,
    callbacks: FlightSheetCallbacks,
    onDismiss: () -> Unit,
) {
    val sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true)
    var effectiveFlight by remember(flight.id) { mutableStateOf(flight) }
    LaunchedEffect(flight) { effectiveFlight = flight }
    val decision = deriveFlightSummaryActions(effectiveFlight)
    var showCancelDialog by remember(flight.id) { mutableStateOf(false) }
    var cancelSubmitting by remember(flight.id) { mutableStateOf(false) }
    var cancelError by remember(flight.id) { mutableStateOf<String?>(null) }
    var windowPhase by remember(flight.id) {
        mutableStateOf(effectiveFlight.evaluateMobileWindow().phase)
    }
    var actionsInMobileWindow by remember(flight.id) {
        mutableStateOf(effectiveFlight.areMobileActionsAvailable())
    }
    // True when the caller's work order is itself a cancellation: the "update" action edits
    // the cancellation details (dialog) instead of opening the regular work-order form, and
    // return-to-ramp is meaningless for a cancelled flight.
    val myWorkOrderIsCancellation = effectiveFlight.myWorkOrder?.type == "Cancellation"

    // A notification may be opened before the flight enters the mobile window. That flight is not
    // cached, so this sheet must stay informational until a fresh open/refresh confirms admission;
    // enabling from the device clock alone would route to forms that cannot hydrate from Room.
    // An already-actionable sheet is still disabled after the trailing boundary.
    LaunchedEffect(
        flight,
        isOnline,
        onRevalidateFlight != null,
    ) {
        var evaluatedFlight = effectiveFlight
        var evaluation = evaluatedFlight.evaluateMobileWindow()
        windowPhase = evaluation.phase
        actionsInMobileWindow = evaluatedFlight.areMobileActionsAvailable()

        disabled@ while (!actionsInMobileWindow) {
            evaluation = evaluatedFlight.evaluateMobileWindow()
            windowPhase = evaluation.phase
            when (evaluation.phase) {
                MobileFlightWindowPhase.Before -> {
                    val startsAt = evaluation.startsAt ?: break@disabled
                    delayUntil(startsAt)
                    // The refresh below may itself return a later rescheduled STA. Looping through
                    // Before again follows that new authoritative leading boundary.
                }

                MobileFlightWindowPhase.Within -> {
                    // Never enable from the device clock alone. Repeatedly fetch the authoritative
                    // row while this sheet is composed and online. Transient failures and modest
                    // clock skew retry without requiring the employee to close the sheet.
                    if (!isOnline || onRevalidateFlight == null) break@disabled
                    val refreshed = try {
                        onRevalidateFlight(evaluatedFlight.id)
                    } catch (error: CancellationException) {
                        throw error
                    } catch (error: ApiException) {
                        if (error.statusCode in 400..499 &&
                            error.statusCode != 408 &&
                            error.statusCode != 429
                        ) {
                            break@disabled
                        }
                        null
                    } catch (_: Exception) {
                        null
                    }
                    if (refreshed != null) {
                        evaluatedFlight = refreshed
                        effectiveFlight = refreshed
                        evaluation = refreshed.evaluateMobileWindow()
                        windowPhase = evaluation.phase
                        actionsInMobileWindow = refreshed.areMobileActionsAvailable()
                    }

                    if (actionsInMobileWindow) break@disabled
                    if (evaluation.phase != MobileFlightWindowPhase.Within) continue@disabled
                    val endsAt = evaluation.endsAt ?: break@disabled
                    val retryAt = minOf(
                        Instant.now().plusMillis(WINDOW_REVALIDATION_RETRY_MILLIS),
                        endsAt.plusMillis(1),
                    )
                    delayUntil(retryAt)
                }

                MobileFlightWindowPhase.After,
                MobileFlightWindowPhase.Unknown -> break@disabled
            }
        }

        if (actionsInMobileWindow) {
            val endsAt = evaluation.endsAt ?: return@LaunchedEffect
            // Server policy includes the exact trailing boundary.
            delayUntil(endsAt.plusMillis(1))
            windowPhase = MobileFlightWindowPhase.After
            actionsInMobileWindow = false
            showCancelDialog = false
        }
    }

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
                customerIataCode = effectiveFlight.customerIataCode.orEmpty(),
                customerName = effectiveFlight.customerName,
                stationCode = effectiveFlight.stationIata,
                operationTypeCode = effectiveFlight.operationTypeName,
                flightNumber = effectiveFlight.flightNumber.takeIf { it.isNotBlank() },
                aircraftModel = effectiveFlight.aircraftTypeModel,
                staDisplay = formatIsoForDisplay(effectiveFlight.scheduledArrivalUtc),
                stdDisplay = formatIsoForDisplay(effectiveFlight.scheduledDepartureUtc),
            )

            if (!actionsInMobileWindow) {
                SectionHeader(
                    title = "Flight details only",
                    subtitle = informationOnlyMessage(effectiveFlight, windowPhase),
                    icon = Icons.Default.Lock,
                )
            }

            when {
                localDraftId != null -> {
                    SheetActionButton(
                        icon = Icons.Default.EditNote,
                        label = "Complete work order",
                        onClick = { callbacks.onCompleteWorkOrderDraft(localDraftId) },
                        primary = true,
                        enabled = actionsInMobileWindow,
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
                            enabled = actionsInMobileWindow,
                        )
                    } else {
                        SheetActionButton(
                            icon = Icons.Default.EditNote,
                            label = "Update work order",
                            onClick = {
                                callbacks.onCreateWorkOrder(effectiveFlight.id)
                            },
                            primary = true,
                            enabled = actionsInMobileWindow,
                        )
                    }
                }
                else -> {
                    SheetActionButton(
                        icon = Icons.AutoMirrored.Filled.NoteAdd,
                        label = "Create work order",
                        onClick = {
                            callbacks.onCreateWorkOrder(effectiveFlight.id)
                        },
                        primary = true,
                        enabled = actionsInMobileWindow,
                    )
                }
            }
            val flightInProgress =
                FlightStatusKind.fromWire(effectiveFlight.status) == FlightStatusKind.InProgress
            val workOrderEditable =
                WorkOrderStatusKind.fromWire(effectiveFlight.myWorkOrder?.status)?.isEditable == true
            if (flightInProgress && workOrderEditable && !myWorkOrderIsCancellation) {
                SheetActionButton(
                    icon = Icons.AutoMirrored.Filled.Undo,
                    label = "Return to ramp",
                    onClick = {
                        callbacks.onReturnToRamp(effectiveFlight.id)
                        onDismiss()
                    },
                    primary = false,
                    enabled = actionsInMobileWindow,
                )
            }

            val flightStatus = FlightStatusKind.fromWire(effectiveFlight.status)
            val canInvite = showInvite &&
                decision != FlightSummaryActionsDecision.ReadOnly &&
                (flightStatus == FlightStatusKind.Scheduled || flightStatus == FlightStatusKind.InProgress)
            if (canInvite) {
                SheetActionButton(
                    icon = Icons.Default.GroupAdd,
                    label = "Invite teammates",
                    onClick = {
                        callbacks.onInviteTeammate(effectiveFlight.id)
                        onDismiss()
                    },
                    primary = false,
                    enabled = actionsInMobileWindow && isOnline,
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
                    enabled = actionsInMobileWindow,
                )
            }
            Spacer(Modifier.height(4.dp))
        }
    }

    if (showCancelDialog) {
        CancelFlightDialog(
            flightStdIso = effectiveFlight.scheduledDepartureUtc,
            flightOffset = offsetSameAsFlight(effectiveFlight.scheduledDepartureUtc),
            initialCanceledAtIso = effectiveFlight.myWorkOrder?.canceledAtUtc,
            initialReason = effectiveFlight.myWorkOrder?.cancellationReason,
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
                callbacks.onCancelFlight(effectiveFlight.id, canceledAtIso, reason) { success, message ->
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

private suspend fun delayUntil(target: Instant) {
    val now = Instant.now()
    if (target.isAfter(now)) {
        delay(Duration.between(now, target).toMillis().coerceAtLeast(1))
    }
}

private const val WINDOW_REVALIDATION_RETRY_MILLIS = 30_000L

private fun informationOnlyMessage(
    flight: MobileFlightDto,
    phase: MobileFlightWindowPhase,
): String = when (phase) {
    MobileFlightWindowPhase.Before -> {
        val availableAt = flight.mobileWindowStartsAtUtc?.let(::formatIsoForDisplay)
        if (availableAt == null) {
            "Actions become available when the flight enters the 12-hour mobile window."
        } else {
            "Actions become available at $availableAt. Until then, this assignment is for information only."
        }
    }
    MobileFlightWindowPhase.After ->
        "This flight is outside the 12-hour mobile window. Its details are available for reference only."
    MobileFlightWindowPhase.Within ->
        "Actions stay disabled until the latest availability is verified. Reconnect or close and reopen to retry."
    MobileFlightWindowPhase.Unknown ->
        "Actions are unavailable because the flight's mobile work window could not be verified."
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
