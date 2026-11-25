package com.nags.operations.ui.flights

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
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
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.platform.LocalSoftwareKeyboardController
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.rememberTextMeasurer
import androidx.compose.ui.unit.dp
import com.nags.operations.R
import com.nags.operations.data.CancellationType
import com.nags.operations.data.FlightDetail
import com.nags.operations.data.FlightListItem
import com.nags.operations.data.SaveFlightCancellationRequest
import com.nags.operations.data.arrivalInputTime
import com.nags.operations.data.cancellationEditable
import com.nags.operations.data.db.entities.FlightCancellationPendingEntity
import com.nags.operations.data.ownCancellation
import com.nags.operations.data.userMessage
import com.nags.operations.ui.components.FlightCard
import com.nags.operations.ui.components.InlineNotice
import com.nags.operations.ui.components.InlineNoticeKind
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
    reportStatus: FlightCardReportStatus,
    listItem: FlightListItem? = null,
) {
    val entryTimeZoneId = rememberSaveable { flight.stationTimeZoneId }
    val entryZone = remember(entryTimeZoneId) { stationTimeZone(entryTimeZoneId) }
    val pendingInput = remember(pending?.inputJson) {
        pending?.inputJson?.let { runCatching { Json.decodeFromString<SaveFlightCancellationRequest>(it) }.getOrNull() }
    }
    val savedReport = flight.ownCancellation()
    val originalCanceledAtUtc = rememberSaveable { (pendingInput?.canceledAtUtc ?: savedReport?.canceledAtUtc).orEmpty() }.takeIf(String::isNotEmpty)
    val initialCanceledAt = rememberSaveable {
        originalCanceledAtUtc?.let { OffsetDateTime.parse(it).atZoneSameInstant(entryZone).toLocalDateTime().toString() }
            ?: LocalDateTime.now(entryZone).withSecond(0).withNano(0).toString()
    }
    val initialRemarks = rememberSaveable { (if (pendingInput != null) pendingInput.remarks else savedReport?.remarks).orEmpty() }
    val initialType = rememberSaveable { pendingInput?.cancellationType ?: savedReport?.cancellationType ?: CancellationType.Unplanned }
    val referenceYear = rememberSaveable { LocalDate.now(entryZone).year }
    var canceledAt by rememberSaveable { mutableStateOf(initialCanceledAt) }
    var remarks by rememberSaveable { mutableStateOf(initialRemarks) }
    var cancellationType by rememberSaveable { mutableStateOf(initialType) }
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
    val utcTime = arrivalInputTime(canceledAt, originalCanceledAtUtc, entryZone)
    val remarksRequired = cancellationType == CancellationType.Planned
    val remarksMissing = remarksRequired && remarks.isBlank()
    val hasChanges = canceledAt != initialCanceledAt || remarks != initialRemarks || cancellationType != initialType
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
        if (enabled && utcTime.isNotBlank() && !remarksMissing) {
            val otherWork = flight.arrivalSubmissions.any { !it.submittedByEmployee.staffMemberId.equals(flight.currentStaffMemberId, true) } ||
                flight.departureSubmissions.any { !it.submittedByEmployee.staffMemberId.equals(flight.currentStaffMemberId, true) }
            if (otherWork && !acknowledgeConflict) confirmWorkReport = true
            else {
                saving = true; saveError = null
                scope.launch {
                    try {
                        val input = SaveFlightCancellationRequest(utcTime, remarks, acknowledgeConflict, cancellationType).normalized()
                        onSave(flight.copy(rowVersion = version), input)
                        onClose()
                    } catch (cancelled: CancellationException) { throw cancelled }
                    catch (error: Exception) { saveError = error.userMessage() }
                    finally { saving = false }
                }
            }
        }
    }
    val cardFlight = flight.formCardSummary(listItem, emptyList()).copy(
        stationTimeZoneId = entryTimeZoneId,
        activeWorkCount = listItem?.activeWorkCount,
        completedWorkCount = listItem?.completedWorkCount,
    )
    val cardNeedsReview = progressStatus in listOf(FlightProgressStatus.ArrivalNeedsReview,
        FlightProgressStatus.DepartureNeedsReview, FlightProgressStatus.CancellationNeedsReview) || needsReview
    val pendingLabel = when {
        cardNeedsReview -> stringResource(R.string.flight_card_needs_review)
        progressStatus == FlightProgressStatus.CancellationRevertPending -> stringResource(R.string.flight_card_revert_pending)
        pending != null || progressStatus in listOf(FlightProgressStatus.ArrivalPendingSync,
            FlightProgressStatus.DeparturePendingSync, FlightProgressStatus.CancellationPendingSync) ->
            stringResource(R.string.flight_card_pending_sync)
        else -> null
    }
    FlightRecordingFormScaffold(flight, stringResource(R.string.cancellation_title), stringResource(R.string.cancellation_save),
        loadCustomerLogo, requestClose, saveEnabled = enabled, saveLoading = saving, onSave = { save(false) },
        progressStatus = progressStatus, compact = true,
        identityContent = { positioned ->
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                FlightCard(cardFlight, onClick = null, loadCustomerLogo = loadCustomerLogo,
                    reportStatus = reportStatus,
                    pendingSyncLabel = pendingLabel, hasSyncError = cardNeedsReview,
                    cancellationAtUtc = pendingInput?.canceledAtUtc ?: cardFlight.latestCancellationAtUtc,
                    onFlightNumberPositioned = positioned)
                FlightApprovalNote(flight, entryZone)
            }
        }) {
        saveError?.let { InlineNotice(it, kind = InlineNoticeKind.Error) }
        if (needsReview) {
            InlineNotice(pending?.error ?: stringResource(R.string.cancellation_sync_review), kind = InlineNoticeKind.Warning)
            TextButton(enabled = !saving, onClick = {
                saving = true
                scope.launch {
                    try {
                        val reviewed = onReview()
                        version = reviewed.rowVersion
                        if (pendingInput?.cancellationType == null)
                            cancellationType = reviewed.ownCancellation()?.cancellationType ?: initialType
                        reviewedChangeId = pending?.changeId
                        saveError = null
                    } catch (cancelled: CancellationException) { throw cancelled }
                    catch (error: Exception) { saveError = error.userMessage() }
                    finally { saving = false }
                }
            }) { Text(stringResource(R.string.cancellation_reload_review)) }
        }
        FlightFormSectionTitle(stringResource(R.string.cancellation_title))
        FlightFormFieldGroup {
            val dateField: @Composable () -> Unit = {
                FlightFormDateTimeField(canceledAt, stringResource(R.string.cancellation_time), stringResource(R.string.cancellation_choose_time),
                    enabled = enabled, errorMessage = if (validationAttempted && utcTime.isBlank()) stringResource(R.string.cancellation_time_required) else null,
                    onClick = { focus.clearFocus(); keyboard?.hide(); pickerOpen = true })
            }
            val typeField: @Composable () -> Unit = {
                FlightCancellationTypeField(cancellationType, enabled, onChange = {
                    cancellationType = it
                    saveError = null
                })
            }
            val fontScale = LocalDensity.current.fontScale.coerceAtLeast(1f)
            BoxWithConstraints(Modifier.fillMaxWidth()) {
                if (maxWidth >= 580.dp * fontScale) Row(verticalAlignment = Alignment.CenterVertically) {
                    Box(Modifier.weight(1f)) { dateField() }
                    Box(Modifier.weight(1f)) { typeField() }
                } else Column {
                    dateField()
                    FlightFormDivider()
                    typeField()
                }
            }
            FlightFormDivider()
            val remarksError = if (validationAttempted && remarksMissing) stringResource(R.string.cancellation_remarks_required) else null
            FlightFormValidation(remarksError, registerError = enabled) {
                FlightFormRemarks(remarks, { remarks = it }, stringResource(R.string.cancellation_remarks), enabled = enabled,
                    heading = stringResource(if (remarksRequired) R.string.cancellation_remarks_required_label else R.string.flight_form_remarks),
                    inGroup = true, placeholder = if (remarksRequired) stringResource(R.string.cancellation_remarks_placeholder) else "")
            }
        }
    }
    if (pickerOpen) FlightFormDateTimeSheet(LocalDateTime.parse(canceledAt), referenceYear,
        onValueChange = { canceledAt = it.toString() }, onDismiss = { pickerOpen = false })
    if (confirmWorkReport) FlightSubmissionConfirmation(stringResource(R.string.cancellation_work_warning_title),
        stringResource(if (flight.departureSubmissions.isNotEmpty()) R.string.cancellation_departed_warning else R.string.cancellation_arrived_warning),
        stringResource(R.string.submission_save_anyway), onConfirm = { confirmWorkReport = false; save(true) }, onDismiss = { confirmWorkReport = false })
    if (confirmDiscard) FlightFormDiscardDialog(stringResource(R.string.cancellation_discard_title),
        onDismiss = { confirmDiscard = false }, onDiscard = onClose)
}

internal val CancellationType.labelResource: Int
    get() = when (this) {
        CancellationType.Unplanned -> R.string.cancellation_unplanned
        CancellationType.Planned -> R.string.cancellation_planned
    }

@Composable
private fun FlightCancellationTypeField(value: CancellationType, enabled: Boolean, onChange: (CancellationType) -> Unit) {
    val label = stringResource(R.string.cancellation_type)
    val options = CancellationType.entries.map { it to stringResource(it.labelResource) }
    val style = MaterialTheme.typography.bodyMedium
    val measurer = rememberTextMeasurer()
    val density = LocalDensity.current
    val selectorWidth = with(density) {
        options.maxOf { measurer.measure(it.second, style.copy(fontWeight = FontWeight.SemiBold)).size.width }.toDp() * 2 + 28.dp
    }.coerceAtLeast(140.dp)
    val labelWidth = with(density) { measurer.measure(label, style.copy(fontWeight = FontWeight.Bold)).size.width.toDp() }
    val labelColor = if (LocalFlightFormCompact.current) MaterialTheme.colorScheme.onSurfaceVariant else MaterialTheme.colorScheme.onSurface
    val title: @Composable () -> Unit = { Text(label, style = style, fontWeight = FontWeight.Bold, color = labelColor) }
    BoxWithConstraints(Modifier.fillMaxWidth().alpha(if (enabled) 1f else 0.45f).padding(horizontal = 14.dp)) {
        val availableSelectorWidth = minOf(selectorWidth, maxWidth)
        if (labelWidth + availableSelectorWidth + 8.dp > maxWidth) {
            Column(Modifier.fillMaxWidth().padding(top = 8.dp)) {
                title()
                Box(Modifier.align(Alignment.End)) { FlightFormSegmentedSelector(value, options, availableSelectorWidth, enabled, onChange) }
            }
        } else Row(Modifier.fillMaxWidth().heightIn(min = 48.dp), horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically) {
            title()
            FlightFormSegmentedSelector(value, options, availableSelectorWidth, enabled, onChange)
        }
    }
}
