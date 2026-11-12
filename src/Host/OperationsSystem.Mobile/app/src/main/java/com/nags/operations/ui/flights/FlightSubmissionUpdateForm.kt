package com.nags.operations.ui.flights

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.FlightLand
import androidx.compose.material.icons.filled.FlightTakeoff
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.runtime.withFrameNanos
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalLocale
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.LinkAnnotation
import androidx.compose.ui.text.SpanStyle
import androidx.compose.ui.text.TextLinkStyles
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.text.withLink
import androidx.compose.ui.text.withStyle
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.nags.operations.R
import com.nags.operations.data.AircraftTypeCatalogItem
import com.nags.operations.data.ArrivalInputError
import com.nags.operations.data.FlightArrivalSubmission
import com.nags.operations.data.FlightDetail
import com.nags.operations.data.SaveFlightArrivalRequest
import com.nags.operations.data.SaveFlightDepartureRequest
import com.nags.operations.data.arrivalInputTime
import com.nags.operations.data.canUpdateArrivalSubmission
import com.nags.operations.data.canUpdateDepartureSubmission
import com.nags.operations.data.db.entities.FlightArrivalPendingEntity
import com.nags.operations.data.db.entities.FlightDeparturePendingEntity
import com.nags.operations.data.userMessage
import com.nags.operations.data.validationErrors
import com.nags.operations.ui.theme.AccentBlue
import com.nags.operations.ui.util.operationLocalTime
import com.nags.operations.ui.util.stationTimeZone
import java.time.Instant
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.OffsetDateTime
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.time.format.FormatStyle
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json

/** Attribution describes the original submission, independently of its operational ATA/ATD. */
@Composable
internal fun FlightSubmissionNote(name: String, submittedAt: String, zone: ZoneId, onUpdate: (() -> Unit)? = null, updateLabel: String? = null) {
    val locale = LocalLocale.current.platformLocale
    val timestampTemplate = stringResource(R.string.flight_submission_timestamp)
    val timestamp = remember(submittedAt, zone, locale, timestampTemplate) {
        runCatching {
            val value = OffsetDateTime.parse(submittedAt).atZoneSameInstant(zone)
            String.format(locale, timestampTemplate,
                value.format(DateTimeFormatter.ofLocalizedDate(FormatStyle.MEDIUM).withLocale(locale)),
                value.format(DateTimeFormatter.ofPattern("HH:mm", locale)))
        }.getOrDefault(submittedAt)
    }
    val prefix = stringResource(R.string.flight_submission_note_prefix)
    val on = stringResource(R.string.flight_submission_note_on)
    val update = updateLabel ?: stringResource(R.string.flight_submission_update)
    Text(buildAnnotatedString {
        append(prefix)
        withStyle(SpanStyle(fontWeight = FontWeight.SemiBold)) { append(name) }
        append(on)
        withStyle(SpanStyle(fontWeight = FontWeight.SemiBold)) { append(timestamp) }
        append(".")
        if (onUpdate != null) {
            append(" ")
            withLink(LinkAnnotation.Clickable("update-submission", TextLinkStyles(style = SpanStyle(color = AccentBlue))) {
                onUpdate()
            }) { append(update) }
        }
    }, Modifier.fillMaxWidth().padding(horizontal = 14.dp),
        style = MaterialTheme.typography.bodySmall.copy(fontSize = 12.sp, lineHeight = 18.sp),
        color = MaterialTheme.colorScheme.onSurfaceVariant)
}

/** A correction saves only this explicit target; the parent work draft remains mounted underneath. */
@Composable
internal fun FlightSubmissionUpdateForm(
    flight: FlightDetail,
    submissionId: String,
    isArrival: Boolean,
    aircraftTypes: List<AircraftTypeCatalogItem>,
    loadCustomerLogo: (String) -> Flow<ByteArray?>,
    pendingArrival: FlightArrivalPendingEntity?,
    pendingDeparture: FlightDeparturePendingEntity?,
    loadSignature: suspend (FlightDetail, FlightDeparturePendingEntity?) -> ByteArray?,
    onReview: suspend () -> FlightDetail,
    onSaveArrival: suspend (FlightDetail, SaveFlightArrivalRequest) -> Unit,
    onSaveDeparture: suspend (FlightDetail, SaveFlightDepartureRequest) -> Unit,
    onClose: () -> Unit,
) {
    val arrival = flight.arrivalSubmissions.firstOrNull { it.id.equals(submissionId, true) }
    val departure = flight.departureSubmissions.firstOrNull { it.id.equals(submissionId, true) }
    val arrivalInput = remember(pendingArrival?.inputJson) { pendingArrival?.let {
        runCatching { Json.decodeFromString<SaveFlightArrivalRequest>(it.inputJson) }.getOrNull()
    }?.takeIf { it.targetSubmissionId.equals(submissionId, true) } }
    val departureInput = remember(pendingDeparture?.inputJson) { pendingDeparture?.let {
        runCatching { Json.decodeFromString<SaveFlightDepartureRequest>(it.inputJson) }.getOrNull()
    }?.takeIf { it.targetSubmissionId.equals(submissionId, true) } }
    val arrivalPending = pendingArrival?.takeIf { arrivalInput != null }
    val departurePending = pendingDeparture?.takeIf { departureInput != null }
    val zone = remember(flight.stationTimeZoneId) { stationTimeZone(flight.stationTimeZoneId) }
    val initialTime = if (isArrival) arrivalInput?.ata ?: arrival?.ata else departureInput?.atd ?: departure?.atd
    var originalTime by rememberSaveable { mutableStateOf(initialTime) }
    var time by rememberSaveable { mutableStateOf(initialTime?.let { operationLocalTime(it, zone) }.orEmpty()) }
    var aircraftId by rememberSaveable { mutableStateOf(arrivalInput?.aircraftTypeId ?: arrival?.aircraftTypeId.orEmpty()) }
    var tail by rememberSaveable { mutableStateOf(arrivalInput?.aircraftTailNumber ?: arrival?.aircraftTailNumber.orEmpty()) }
    var reference by rememberSaveable { mutableStateOf(if (arrivalInput != null) arrivalInput.customerWorkReferenceNumber.orEmpty()
        else arrival?.customerWorkReferenceNumber.orEmpty()) }
    var remarks by rememberSaveable { mutableStateOf(if (isArrival) {
        if (arrivalInput != null) arrivalInput.remarks.orEmpty() else arrival?.remarks.orEmpty()
    } else if (departureInput != null) departureInput.remarks.orEmpty() else departure?.remarks.orEmpty()) }
    var signature by rememberSaveable { mutableStateOf("") }
    var signedAt by rememberSaveable { mutableStateOf<String?>(null) }
    var reviewedJson by rememberSaveable { mutableStateOf(Json.encodeToString(flight.let { original ->
        if (isArrival && arrivalPending != null) {
            val reviewed = arrivalPending.reviewedArrivalJson?.let { runCatching { Json.decodeFromString<FlightArrivalSubmission?>(it) }.getOrNull() }
            original.copy(rowVersion = arrivalPending.rowVersion, arrivalSubmissions = original.arrivalSubmissions.map {
                if (it.id.equals(submissionId, true)) reviewed ?: it else it
            })
        } else if (!isArrival && departurePending != null) original.copy(rowVersion = departurePending.rowVersion,
            departureSubmissions = Json.decodeFromString(departurePending.reviewedDepartureJson))
        else original
    })) }
    val reviewed = remember(reviewedJson) { Json.decodeFromString<FlightDetail>(reviewedJson) }
    val inputTime = arrivalInputTime(time, originalTime, zone)
    fun arrivalValue() = SaveFlightArrivalRequest(inputTime, aircraftId, tail, reference, remarks,
        targetSubmissionId = submissionId).normalized()
    fun departureValue() = SaveFlightDepartureRequest(inputTime, remarks = remarks, targetSubmissionId = submissionId).normalized()
    var baseline by rememberSaveable { mutableStateOf(if (isArrival) Json.encodeToString(arrivalValue()) else Json.encodeToString(departureValue())) }
    val changed = (if (isArrival) Json.encodeToString(arrivalValue()) else Json.encodeToString(departureValue())) != baseline || signature.isNotBlank()
    var saving by remember { mutableStateOf(false) }
    var saveError by rememberSaveable { mutableStateOf<String?>(null) }
    var attempted by rememberSaveable { mutableStateOf(false) }
    var reviewedChangeId by rememberSaveable { mutableStateOf<String?>(null) }
    val pendingChangeId = if (isArrival) arrivalPending?.changeId else departurePending?.changeId
    val pendingStatus = if (isArrival) arrivalPending?.status else departurePending?.status
    val needsReview = pendingStatus != null && pendingStatus != "pending" && reviewedChangeId != pendingChangeId
    val eligible = if (isArrival) reviewed.canUpdateArrivalSubmission(submissionId) else reviewed.canUpdateDepartureSubmission(submissionId)
    val enabled = eligible && !saving && !needsReview
    var picker by rememberSaveable { mutableStateOf<String?>(null) }
    var confirmDiscard by rememberSaveable { mutableStateOf(false) }
    var confirmCanceled by rememberSaveable { mutableStateOf(false) }
    val scope = rememberCoroutineScope()
    val validationState = rememberFlightFormValidationState()
    var validationRequest by remember { mutableIntStateOf(0) }
    LaunchedEffect(validationRequest) {
        if (validationRequest > 0) {
            withFrameNanos { }
            if (!validationState.revealFirstError()) validationState.revealTarget("submission-save-error")
        }
    }
    val errors = if (attempted && isArrival) arrivalValue().validationErrors(reviewed.rowVersion) else emptySet()
    val aircraftAvailable = aircraftTypes.any { it.id.equals(aircraftId, true) }
    var savedPreview by remember { mutableStateOf<ByteArray?>(null) }
    var previewLoading by remember { mutableStateOf(false) }
    var previewRetry by remember { mutableIntStateOf(0) }
    val reviewedDeparture = reviewed.departureSubmissions.firstOrNull { it.id.equals(submissionId, true) }
    LaunchedEffect(reviewedDeparture, departurePending?.signatureFile, previewRetry) {
        if (!isArrival && reviewedDeparture != null) {
            previewLoading = true
            savedPreview = null
            try { savedPreview = loadSignature(reviewed.copy(departureSubmissions = listOf(reviewedDeparture)), departurePending) }
            catch (cancelled: CancellationException) { throw cancelled }
            catch (_: Exception) { /* A missing preview never removes the saved signature. */ }
            finally { previewLoading = false }
        }
    }
    val close = { if (!saving) { if (changed) confirmDiscard = true else onClose() }; Unit }
    BackHandler { close() }
    fun save(acknowledge: Boolean) {
        attempted = true
        if (!enabled) return
        if (inputTime.isBlank() || isArrival && (errors.isNotEmpty() || !aircraftAvailable || arrivalValue().validationErrors(reviewed.rowVersion).isNotEmpty())) {
            validationRequest++
            return
        }
        if (!acknowledge && reviewed.cancellationSubmissions.any {
            !it.submittedByEmployee.staffMemberId.equals(reviewed.currentStaffMemberId, true)
        }) { confirmCanceled = true; return }
        saving = true; saveError = null
        val arrivalSave = arrivalValue().copy(acknowledgeConflictingSubmission = acknowledge)
        val departureSave = departureValue().copy(acknowledgeConflictingSubmission = acknowledge)
        val ink = signature
        val inkSignedAt = signedAt
        scope.launch {
            try {
                if (isArrival) onSaveArrival(reviewed, arrivalSave)
                else {
                    val png = if (ink.isNotBlank()) withContext(Dispatchers.Default) { signaturePngContent(ink) } else null
                    onSaveDeparture(reviewed, departureSave.copy(signatureContent = png, signatureSignedAtUtc = if (png != null) inkSignedAt else null))
                }
                baseline = if (isArrival) Json.encodeToString(arrivalValue()) else Json.encodeToString(departureValue())
                onClose()
            } catch (cancelled: CancellationException) { throw cancelled }
            catch (error: Exception) { saveError = error.userMessage(); validationRequest++ }
            finally { saving = false }
        }
    }
    val title = stringResource(if (isArrival) R.string.flight_submission_update_arrival else R.string.flight_submission_update_departure)
    FlightRecordingFormScaffold(flight, title, stringResource(R.string.flight_submission_save), loadCustomerLogo,
        onRequestClose = close, saveEnabled = enabled && (changed || reviewedChangeId != null), saveLoading = saving,
        onSave = { save(false) }, compact = true, validationState = validationState, identityContent = {},
        navigationIcon = Icons.AutoMirrored.Filled.ArrowBack, navigationDescription = stringResource(R.string.account_back), headerTitle = title) {
        saveError?.let { FlightFormValidation(it, modifier = Modifier.flightFormNavigationTarget("submission-save-error"), registerError = false) {} }
        if (needsReview) {
            Text(stringResource(if (isArrival) R.string.arrival_sync_review else R.string.departure_sync_review),
                Modifier.padding(horizontal = 14.dp), style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.error)
            (if (isArrival) arrivalPending?.error else departurePending?.error)?.let {
                Text(it, Modifier.padding(horizontal = 14.dp), style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.error)
            }
            TextButton(enabled = !saving, onClick = {
                saving = true
                scope.launch {
                    try {
                        reviewedJson = Json.encodeToString(onReview())
                        reviewedChangeId = pendingChangeId
                        saveError = null
                    } catch (cancelled: CancellationException) { throw cancelled }
                    catch (error: Exception) { saveError = error.userMessage(); validationRequest++ }
                    finally { saving = false }
                }
            }) { Text(stringResource(if (isArrival) R.string.arrival_reload_review else R.string.departure_reload_review)) }
        }
        FlightFormSectionTitle(stringResource(if (isArrival) R.string.operations_arrival else R.string.operations_departure))
        FlightFormFieldGroup {
            Column {
                FlightFormDateTimeField(time, stringResource(if (isArrival) R.string.operations_actual_arrival else R.string.operations_actual_departure),
                    stringResource(if (isArrival) R.string.arrival_choose_ata else R.string.operations_choose_atd), enabled = enabled,
                    errorMessage = if (attempted && inputTime.isBlank()) stringResource(if (isArrival) R.string.arrival_ata_required else R.string.departure_atd_required) else null,
                    leadingPadding = 0.dp, displayPattern = "dd/MM/yy HH:mm", prominentLabel = true,
                    leadingContent = { FlightActualTimeIcon(if (isArrival) Icons.Filled.FlightLand else Icons.Filled.FlightTakeoff) }, onClick = { picker = "time" })
                FlightFormDivider()
                if (isArrival) {
                    val selected = aircraftTypes.firstOrNull { it.id.equals(aircraftId, true) }?.model
                        ?: arrival?.takeIf { it.aircraftTypeId.equals(aircraftId, true) }?.aircraftTypeModel.orEmpty()
                    FlightFormChoiceRow(stringResource(R.string.arrival_aircraft), selected.ifBlank { stringResource(R.string.flight_form_required_field) },
                        stringResource(R.string.arrival_aircraft_type), isPlaceholder = selected.isBlank(), enabled = enabled,
                        errorMessage = if (attempted && !aircraftAvailable) stringResource(R.string.arrival_aircraft_unavailable) else null,
                        onClick = { picker = "aircraft" })
                    FlightFormDivider()
                    FlightFormTextRow(stringResource(R.string.arrival_tail_number), tail, { tail = it }, stringResource(R.string.flight_form_required_field),
                        keyboardOptions = KeyboardOptions(capitalization = KeyboardCapitalization.Characters, imeAction = ImeAction.Next), enabled = enabled,
                        errorMessage = if (ArrivalInputError.TailRequired in errors) stringResource(R.string.arrival_tail_required) else null)
                    FlightFormDivider()
                    FlightFormTextRow(stringResource(R.string.arrival_reference), reference, { reference = it }, "", enabled = enabled)
                } else FlightSignatureField(signature, hasSavedSignature = true, savedPreview = savedPreview, loading = previewLoading,
                    enabled = enabled, onOpen = { picker = "signature" })
                FlightFormDivider()
                FlightFormRemarks(remarks, { remarks = it }, stringResource(if (isArrival) R.string.operations_arrival_remarks else R.string.operations_departure_remarks), enabled = enabled,
                    heading = stringResource(if (isArrival) R.string.operations_arrival_remarks_heading else R.string.operations_departure_remarks_heading), inGroup = true)
            }
        }
        if (ArrivalInputError.VersionRequired in errors) FlightFormValidation(stringResource(R.string.arrival_reload_required)) {}
    }
    if (picker == "time") FlightFormDateTimeSheet(LocalDateTime.parse(time), LocalDate.now(zone).year,
        onValueChange = { time = it.toString() }, onDismiss = { picker = null },
        title = stringResource(if (isArrival) R.string.arrival_ata else R.string.operations_atd), showTitle = false)
    if (picker == "aircraft") FlightWorkPicker(title = stringResource(R.string.arrival_aircraft_type), searchLabel = stringResource(R.string.arrival_search_aircraft),
        emptyLabel = stringResource(R.string.arrival_no_aircraft), options = aircraftTypes.sortedBy { it.model }.map { FlightWorkChoice(it.id, it.model, it.manufacturer) },
        selectedId = aircraftId, onDismiss = { picker = null }, onSelect = { aircraftId = it })
    if (picker == "signature") FlightSignatureSheet(signature, true, savedPreview, previewLoading, onRetry = { previewRetry++ },
        onValueChange = { signature = it; signedAt = if (it.isBlank()) null else Instant.now().toString() }, onDismiss = { picker = null })
    if (confirmDiscard) FlightFormDiscardDialog(stringResource(R.string.operations_discard_title), onDismiss = { confirmDiscard = false }, onDiscard = onClose)
    if (confirmCanceled) FlightSubmissionConfirmation(stringResource(R.string.submission_canceled_warning_title), stringResource(R.string.submission_canceled_warning),
        stringResource(R.string.submission_save_anyway), onConfirm = { confirmCanceled = false; save(true) }, onDismiss = { confirmCanceled = false })
}
