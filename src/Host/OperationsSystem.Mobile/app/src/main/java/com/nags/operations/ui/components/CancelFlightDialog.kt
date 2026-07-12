package com.nags.operations.ui.components

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Cancel
import androidx.compose.material.icons.filled.Schedule
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import java.time.OffsetDateTime
import java.time.ZoneOffset
import com.nags.operations.ui.workorder.WorkOrderFormLimits

/**
 * Confirmation dialog for cancelling a flight from the actions sheet. Filing a
 * cancellation creates an empty cancel work order (no service / task lines) that
 * follows the normal approval flow; the employee only sets the time the flight was
 * canceled. Mirrors the web portal's CancelFlightDialog.
 *
 * Reused for both filing a new cancellation and updating the time on an existing
 * under-review cancel work order (see [isUpdate] / [initialCanceledAtIso]).
 *
 * @param flightStdIso Scheduled departure, used to seed the picker when no initial time is given.
 * @param flightOffset Zone offset of the flight so the picker shows local time.
 * @param initialCanceledAtIso Existing cancellation time when editing; null seeds the picker to now.
 * @param isUpdate When true, the copy reflects editing an existing cancellation rather than filing a new one.
 * @param isSubmitting True while the cancellation is being written durably to the local outbox.
 * @param errorMessage Enqueue failure shown without closing the dialog, allowing the user to retry.
 * @param onConfirm Invoked with the chosen cancellation time as an ISO-8601 string.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun CancelFlightDialog(
    flightStdIso: String,
    flightOffset: ZoneOffset,
    onDismiss: () -> Unit,
    onConfirm: (canceledAtIso: String, reason: String) -> Unit,
    initialCanceledAtIso: String? = null,
    initialReason: String? = null,
    isUpdate: Boolean = false,
    isSubmitting: Boolean = false,
    errorMessage: String? = null,
) {
    var canceledAt by remember(initialCanceledAtIso) {
        mutableStateOf(
            initialCanceledAtIso?.takeIf { it.isNotBlank() }
                ?: OffsetDateTime.now(flightOffset).toString(),
        )
    }
    var reason by remember(initialReason) { mutableStateOf(initialReason.orEmpty()) }

    AlertDialog(
        onDismissRequest = { if (!isSubmitting) onDismiss() },
        title = {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Icon(
                    Icons.Default.Cancel,
                    contentDescription = null,
                    tint = MaterialTheme.colorScheme.error,
                )
                Text(
                    if (isUpdate) "Update cancellation?" else "Cancel flight?",
                    modifier = Modifier.padding(start = 12.dp),
                    fontWeight = FontWeight.SemiBold,
                )
            }
        },
        text = {
            Column(
                modifier = Modifier.fillMaxWidth(),
                verticalArrangement = Arrangement.spacedBy(12.dp),
            ) {
                Text(
                    if (isUpdate) {
                        "Update the time this flight was canceled. The pending cancellation " +
                            "work order will be re-submitted for approval when you're back online."
                    } else {
                        "This files a cancellation work order for approval. Set the time the " +
                            "flight was canceled — it will sync when you're back online."
                    },
                    style = MaterialTheme.typography.bodyMedium,
                )
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Icon(
                        Icons.Default.Schedule,
                        contentDescription = null,
                        tint = MaterialTheme.colorScheme.primary,
                        modifier = Modifier.padding(end = 8.dp),
                    )
                    Text(
                        "Cancellation time",
                        style = MaterialTheme.typography.labelLarge,
                        fontWeight = FontWeight.SemiBold,
                    )
                }
                WorkOrderDateTimePickerField(
                    iso = canceledAt,
                    label = "Canceled at",
                    placeholder = "Tap to set cancellation date & time",
                    flightOffset = flightOffset,
                    defaultInitialIso = flightStdIso,
                    onIsoConfirmed = { canceledAt = it },
                    modifier = Modifier.fillMaxWidth(),
                    enabled = !isSubmitting,
                )
                // Cancellation work orders require a reason on the server.
                androidx.compose.material3.OutlinedTextField(
                    value = reason,
                    onValueChange = { reason = it.take(WorkOrderFormLimits.CancellationReason) },
                    label = { Text("Reason") },
                    placeholder = { Text("Why was this flight canceled?") },
                    minLines = 2,
                    enabled = !isSubmitting,
                    modifier = Modifier.fillMaxWidth(),
                )
                if (errorMessage != null) {
                    Text(
                        errorMessage,
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.error,
                    )
                }
            }
        },
        confirmButton = {
            Button(
                enabled = reason.isNotBlank() && !isSubmitting,
                onClick = {
                    canceledAt.takeIf { it.isNotBlank() }?.let { onConfirm(it, reason.trim()) }
                },
                colors = ButtonDefaults.buttonColors(
                    containerColor = MaterialTheme.colorScheme.error,
                ),
            ) {
                Text(
                    when {
                        isSubmitting -> "Saving…"
                        isUpdate -> "Update cancellation"
                        else -> "Cancel flight"
                    },
                    fontWeight = FontWeight.SemiBold,
                )
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss, enabled = !isSubmitting) {
                Text(if (isUpdate) "Discard" else "Keep flight")
            }
        },
    )
}
