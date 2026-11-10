package com.nags.operations.ui.flights

import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.platform.LocalSoftwareKeyboardController
import androidx.compose.ui.res.stringResource
import com.nags.operations.R
import com.nags.operations.data.FlightDetail
import com.nags.operations.data.SaveFlightCancellationRequest
import com.nags.operations.data.arrivalInputTime
import com.nags.operations.data.cancellationEditable
import com.nags.operations.data.db.entities.FlightCancellationPendingEntity
import com.nags.operations.data.userMessage
import com.nags.operations.ui.util.stationTimeZone
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.OffsetDateTime
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.launch
import kotlinx.serialization.json.Json

/** Cancellation input is retained locally before the form closes, just like arrival/departure. */
@Composable
internal fun FlightCancellationForm(
    flight: FlightDetail,
    loadCustomerLogo: (String) -> Flow<ByteArray?>,
    onClose: () -> Unit,
    progressStatus: FlightProgressStatus? = null,
    pending: FlightCancellationPendingEntity? = null,
    onSave: suspend (FlightDetail, SaveFlightCancellationRequest) -> Unit,
    onReview: suspend () -> FlightDetail,
) {
    val entryTimeZoneId = rememberSaveable { flight.stationTimeZoneId }
    val entryZone = remember(entryTimeZoneId) { stationTimeZone(entryTimeZoneId) }
    val pendingInput = remember(pending?.inputJson) {
        pending?.inputJson?.let { runCatching { Json.decodeFromString<SaveFlightCancellationRequest>(it) }.getOrNull() }
    }
    val initialCanceledAt = rememberSaveable {
        pendingInput?.canceledAtUtc?.let { OffsetDateTime.parse(it).atZoneSameInstant(entryZone).toLocalDateTime().toString() }
            ?: LocalDateTime.now(entryZone).withSecond(0).withNano(0).toString()
    }
    val referenceYear = rememberSaveable { LocalDate.now(entryZone).year }
    var canceledAt by rememberSaveable { mutableStateOf(initialCanceledAt) }
    var remarks by rememberSaveable { mutableStateOf(pendingInput?.remarks.orEmpty()) }
    var version by rememberSaveable { mutableStateOf(pending?.rowVersion ?: flight.rowVersion) }
    var reviewedChangeId by rememberSaveable { mutableStateOf<String?>(null) }
    var pickerOpen by rememberSaveable { mutableStateOf(false) }
    var confirmDiscard by rememberSaveable { mutableStateOf(false) }
    var confirmWorkReport by rememberSaveable { mutableStateOf(false) }
    var saveError by rememberSaveable { mutableStateOf<String?>(null) }
    var validationAttempted by rememberSaveable { mutableStateOf(false) }
    var saving by remember { mutableStateOf(false) }
    val scope = rememberCoroutineScope()
    val needsReview = pending != null && pending.status != "pending" && reviewedChangeId != pending.changeId
    val enabled = flight.cancellationEditable() && !saving && !needsReview
    val utcTime = arrivalInputTime(canceledAt, pendingInput?.canceledAtUtc, entryZone)
    val hasChanges = canceledAt != initialCanceledAt || remarks != pendingInput?.remarks.orEmpty()
    val requestClose = {
        if (saving) Unit else when {
            pickerOpen -> pickerOpen = false
            hasChanges -> confirmDiscard = true
            else -> onClose()
        }
    }
    val focus = LocalFocusManager.current
    val keyboard = LocalSoftwareKeyboardController.current
    val save: (Boolean) -> Unit = { acknowledgeConflict ->
        validationAttempted = true
        if (enabled && utcTime.isNotBlank()) {
            val otherWork = flight.arrivalSubmissions.any { !it.submittedByEmployee.staffMemberId.equals(flight.currentStaffMemberId, true) } ||
                flight.departureSubmissions.any { !it.submittedByEmployee.staffMemberId.equals(flight.currentStaffMemberId, true) }
            if (otherWork && !acknowledgeConflict) confirmWorkReport = true
            else {
                saving = true; saveError = null
                scope.launch {
                    try {
                        onSave(flight.copy(rowVersion = version), SaveFlightCancellationRequest(utcTime, remarks, acknowledgeConflict).normalized())
                        onClose()
                    } catch (cancelled: CancellationException) { throw cancelled }
                    catch (error: Exception) { saveError = error.userMessage() }
                    finally { saving = false }
                }
            }
        }
    }
    FlightRecordingFormScaffold(flight, stringResource(R.string.cancellation_title), stringResource(R.string.cancellation_save),
        loadCustomerLogo, requestClose, saveEnabled = enabled, saveLoading = saving, onSave = { save(false) }, progressStatus = progressStatus) {
        saveError?.let { Text(it, color = MaterialTheme.colorScheme.error, style = MaterialTheme.typography.bodySmall) }
        if (needsReview) {
            Text(stringResource(R.string.cancellation_sync_review), color = MaterialTheme.colorScheme.error, style = MaterialTheme.typography.bodySmall)
            pending?.error?.let { Text(it, color = MaterialTheme.colorScheme.error, style = MaterialTheme.typography.bodySmall) }
            TextButton(enabled = !saving, onClick = {
                saving = true
                scope.launch {
                    try {
                        version = onReview().rowVersion; reviewedChangeId = pending?.changeId; saveError = null
                    } catch (cancelled: CancellationException) { throw cancelled }
                    catch (error: Exception) { saveError = error.userMessage() }
                    finally { saving = false }
                }
            }) { Text(stringResource(R.string.cancellation_reload_review)) }
        }
        FlightFormFieldGroup {
            FlightFormDateTimeField(canceledAt, stringResource(R.string.cancellation_time), stringResource(R.string.cancellation_choose_time),
                enabled = enabled, errorMessage = if (validationAttempted && utcTime.isBlank()) stringResource(R.string.cancellation_time_required) else null,
                onClick = { focus.clearFocus(); keyboard?.hide(); pickerOpen = true })
        }
        FlightFormRemarks(remarks, { remarks = it }, stringResource(R.string.cancellation_remarks), enabled = enabled)
    }
    if (pickerOpen) FlightFormDateTimeSheet(LocalDateTime.parse(canceledAt), referenceYear,
        onValueChange = { canceledAt = it.toString() }, onDismiss = { pickerOpen = false })
    if (confirmWorkReport) FlightSubmissionConfirmation(stringResource(R.string.cancellation_work_warning_title),
        stringResource(if (flight.departureSubmissions.isNotEmpty()) R.string.cancellation_departed_warning else R.string.cancellation_arrived_warning),
        stringResource(R.string.submission_save_anyway), onConfirm = { confirmWorkReport = false; save(true) }, onDismiss = { confirmWorkReport = false })
    if (confirmDiscard) FlightFormDiscardDialog(stringResource(R.string.cancellation_discard_title),
        onDismiss = { confirmDiscard = false }, onDiscard = onClose)
}
