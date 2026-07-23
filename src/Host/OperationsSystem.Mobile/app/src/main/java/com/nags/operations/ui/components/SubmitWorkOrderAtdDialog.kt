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
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import java.time.Clock
import java.time.OffsetDateTime
import java.time.ZoneOffset
import java.time.temporal.ChronoUnit

internal fun initialSubmitAtdIso(
    restoredAtdIso: String?,
    flightOffset: ZoneOffset,
    clock: Clock,
): String = restoredAtdIso?.takeIf { it.isNotBlank() }
    ?: OffsetDateTime.now(clock)
        .withOffsetSameInstant(flightOffset)
        .truncatedTo(ChronoUnit.MINUTES)
        .toString()

/**
 * Final work-order step for confirming actual time of departure (ATD).
 *
 * A restored ATD is preserved. Otherwise the dialog freezes the flight-local current time,
 * truncated to the minute, as its candidate for this dialog opening. The candidate survives
 * configuration changes, while dismissing the dialog still does not commit it.
 *
 * @param defaultAtdIso Current form ATD, when restored from an existing work order or draft.
 * @param flightOffset Zone offset used to create and edit flight-local timestamps.
 * @param isBusy True while either action is being persisted; editing and repeat actions are disabled.
 * @param atdValidationError Final-submission validation error shown with the ATD field.
 * @param onAtdIsoChanged Called after the employee picks another ATD so stale validation can be cleared.
 * @param onDismiss Called for back/outside dismissal while the dialog is idle.
 * @param onSaveDraft Commits the displayed ATD to a draft without requiring submission validity.
 * @param onConfirm Commits the displayed ATD for final validation and submission.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun SubmitWorkOrderAtdDialog(
    defaultAtdIso: String?,
    flightOffset: ZoneOffset,
    isBusy: Boolean = false,
    atdValidationError: String? = null,
    onAtdIsoChanged: () -> Unit = {},
    onDismiss: () -> Unit,
    onSaveDraft: (String) -> Unit,
    onConfirm: (String) -> Unit,
) {
    var atd by rememberSaveable(defaultAtdIso, flightOffset) {
        mutableStateOf(
            initialSubmitAtdIso(
                restoredAtdIso = defaultAtdIso,
                flightOffset = flightOffset,
                clock = Clock.systemUTC(),
            ),
        )
    }
    val visibleAtdError = atdValidationError?.takeIf { it.isNotBlank() }

    AlertDialog(
        onDismissRequest = { if (!isBusy) onDismiss() },
        title = {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Icon(
                    Icons.Default.CheckCircle,
                    contentDescription = null,
                    tint = MaterialTheme.colorScheme.primary,
                )
                Text(
                    "Set actual departure time",
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
                    "ATD is required to submit this work order. Confirm when the aircraft " +
                        "actually departed, or save as draft to finish later.",
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
                    defaultInitialIso = atd,
                    onIsoConfirmed = {
                        onAtdIsoChanged()
                        atd = it
                    },
                    modifier = Modifier.fillMaxWidth(),
                    isError = visibleAtdError != null,
                    enabled = !isBusy,
                    supportingText = visibleAtdError?.let { msg ->
                        {
                            Text(
                                msg,
                                color = MaterialTheme.colorScheme.error,
                                style = MaterialTheme.typography.bodySmall,
                            )
                        }
                    },
                )
                if (isBusy) {
                    LinearProgressIndicator(modifier = Modifier.fillMaxWidth())
                }
            }
        },
        confirmButton = {
            Button(
                onClick = { onConfirm(atd) },
                enabled = !isBusy,
            ) {
                Text("Submit", fontWeight = FontWeight.SemiBold)
            }
        },
        dismissButton = {
            TextButton(
                onClick = { onSaveDraft(atd) },
                enabled = !isBusy,
            ) {
                Text("Save as draft")
            }
        },
    )
}
