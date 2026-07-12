package com.nags.operations.ui.components

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.material.icons.filled.Schedule
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
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

/**
 * Shown after inline validation passes — mirrors the reference Operations mobile app:
 * confirm actual time of departure (ATD) via date/time pickers before the work order is posted.
 * Posting to the server is wired separately.
 *
 * @param defaultAtdIso Optional seed when reopening the same session (not read from the work-order form).
 * @param flightStdIso Default anchor when opening the picker if [iso] is blank (scheduled departure).
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun SubmitWorkOrderAtdDialog(
    defaultAtdIso: String?,
    flightStdIso: String,
    flightOffset: ZoneOffset,
    submitLabel: String = "Submit work order",
    /** Shown under the ATD field when server-side validation failed (e.g. ATD before ATA). */
    atdValidationError: String? = null,
    /** Called when the user picks a new ATD so the parent can clear a stale ATD error. */
    onAtdIsoChanged: () -> Unit = {},
    onDismiss: () -> Unit,
    /** Non-blank ISO string, or null if the employee cleared ATD before confirming. */
    onConfirm: (String?) -> Unit,
) {
    var atd by remember(defaultAtdIso) {
        mutableStateOf(
            defaultAtdIso?.takeIf { it.isNotBlank() }
                ?: OffsetDateTime.now(flightOffset).toString(),
        )
    }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Icon(
                    Icons.Default.CheckCircle,
                    contentDescription = null,
                    tint = MaterialTheme.colorScheme.primary,
                )
                Text(
                    "Submit work order?",
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
                    "Confirm the actual time of departure (ATD) before submitting. " +
                        "Leave it as-is if the aircraft has not departed yet — you can edit it later.",
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
                        "Actual time of departure",
                        style = MaterialTheme.typography.labelLarge,
                        fontWeight = FontWeight.SemiBold,
                    )
                }
                WorkOrderDateTimePickerField(
                    iso = atd,
                    label = "ATD",
                    placeholder = "Tap to set departure date & time",
                    flightOffset = flightOffset,
                    defaultInitialIso = flightStdIso,
                    onIsoConfirmed = {
                        onAtdIsoChanged()
                        atd = it
                    },
                    modifier = Modifier.fillMaxWidth(),
                    isError = atdValidationError != null,
                    supportingText = atdValidationError?.takeIf { it.isNotBlank() }?.let { msg ->
                        {
                            Text(
                                msg,
                                color = MaterialTheme.colorScheme.error,
                                style = MaterialTheme.typography.bodySmall,
                            )
                        }
                    },
                )
            }
        },
        confirmButton = {
            Button(
                onClick = {
                    onConfirm(atd.takeIf { it.isNotBlank() })
                },
            ) {
                Text(submitLabel, fontWeight = FontWeight.SemiBold)
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text("Cancel")
            }
        },
    )
}
