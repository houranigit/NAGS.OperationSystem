package com.nags.operations.ui.flights

import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.ui.res.stringResource
import com.nags.operations.R

/** Confirm a conflicting employee report before retaining a new submission on this device. */
@Composable
internal fun FlightSubmissionConfirmation(title: String, message: String, confirmLabel: String,
    onConfirm: () -> Unit, onDismiss: () -> Unit) {
    AlertDialog(onDismissRequest = onDismiss,
        title = { Text(title) }, text = { Text(message) },
        confirmButton = { TextButton(onClick = onConfirm) { Text(confirmLabel) } },
        dismissButton = { TextButton(onClick = onDismiss) { Text(stringResource(R.string.flights_cancel)) } })
}
