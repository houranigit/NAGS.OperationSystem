package com.nags.operations.ui.flights

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.layout.Column
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
import com.nags.operations.ui.components.InlineNotice
import com.nags.operations.ui.components.InlineNoticeKind
import com.nags.operations.data.FlightOperationReviewTarget
import com.nags.operations.data.mergeArrivalReview
import com.nags.operations.data.mergeDepartureReview
import com.nags.operations.data.arrivalEditable
import com.nags.operations.data.departureEditable
import com.nags.operations.data.visibleArrival
import com.nags.operations.data.visibleDeparture
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
import com.nags.operations.data.requiresArrivalRemarks
import com.nags.operations.ui.theme.AccentBlue
import com.nags.operations.ui.util.operationLocalTime
import com.nags.operations.ui.util.stationTimeZone
import java.util.Base64
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
internal fun FlightSubmissionNote(name: String, submittedAt: String, zone: ZoneId, onUpdate: (() -> Unit)? = null, updateLabel: String? = null,
    needsReview: Boolean = false, updatedByDisplayName: String? = null, updatedAtUtc: String? = null) {
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
    val updateTimestamp = remember(updatedAtUtc, zone, locale, timestampTemplate) {
        updatedAtUtc?.let { raw -> runCatching {
            val value = OffsetDateTime.parse(raw).atZoneSameInstant(zone)
            String.format(locale, timestampTemplate,
                value.format(DateTimeFormatter.ofLocalizedDate(FormatStyle.MEDIUM).withLocale(locale)),
                value.format(DateTimeFormatter.ofPattern("HH:mm", locale)))
        }.getOrNull() }
    }
    val updatedBy = stringResource(R.string.flight_submission_note_updated_by)
    val prefix = stringResource(R.string.flight_submission_note_prefix)
    val on = stringResource(R.string.flight_submission_note_on)
    val update = updateLabel ?: stringResource(R.string.flight_submission_update)
    val updateColor = if (needsReview) MaterialTheme.colorScheme.error else AccentBlue
    Text(buildAnnotatedString {
        append(prefix)
        withStyle(SpanStyle(fontWeight = FontWeight.SemiBold)) { append(name) }
        append(on)
        withStyle(SpanStyle(fontWeight = FontWeight.SemiBold)) { append(timestamp) }
        if (!updatedByDisplayName.isNullOrBlank() && updateTimestamp != null) {
            append(updatedBy)
            withStyle(SpanStyle(fontWeight = FontWeight.SemiBold)) { append(updatedByDisplayName) }
            append(on)
            withStyle(SpanStyle(fontWeight = FontWeight.SemiBold)) { append(updateTimestamp) }
        }
        append(".")
        if (onUpdate != null) {
            append(" ")
            withLink(LinkAnnotation.Clickable("update-submission", TextLinkStyles(style = SpanStyle(color = updateColor))) {
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
    reviewMode: Boolean = false,
    cachedReview: Boolean = false,
    onSaveReviewed: suspend (FlightDetail, FlightOperationReviewTarget, suspend () -> Unit) -> Unit = { _, _, _ -> error("Review saving is unavailable.") },
    onDiscardReviewed: suspend (FlightOperationReviewTarget) -> Unit = { error("Review discard is unavailable.") },
) {
    val arrival = if (submissionId.isBlank()) flight.visibleArrival() else flight.arrivalSubmissions.firstOrNull { it.id.equals(submissionId, true) }
    val departure = if (submissionId.isBlank()) flight.visibleDeparture() else flight.departureSubmissions.firstOrNull { it.id.equals(submissionId, true) }
    val arrivalInput = remember(pendingArrival?.inputJson, pendingArrival?.reviewSourceInputJson) { pendingArrival?.let {
        runCatching { Json.decodeFromString<SaveFlightArrivalRequest>(if (reviewMode || it.status != "pending") it.reviewSourceInputJson ?: it.inputJson else it.inputJson) }.getOrNull()
    }?.takeIf { reviewMode || it.targetSubmissionId.equals(submissionId, true) } }
    val departureInput = remember(pendingDeparture?.inputJson, pendingDeparture?.reviewSourceInputJson) { pendingDeparture?.let {
        runCatching { Json.decodeFromString<SaveFlightDepartureRequest>(if (reviewMode || it.status != "pending") it.reviewSourceInputJson ?: it.inputJson else it.inputJson) }.getOrNull()
    }?.takeIf { reviewMode || it.targetSubmissionId.equals(submissionId, true) } }
    val arrivalPending = pendingArrival?.takeIf { arrivalInput != null }
    val departurePending = pendingDeparture?.takeIf { departureInput != null }
    val zone = remember(flight.stationTimeZoneId) { stationTimeZone(flight.stationTimeZoneId) }
    val reviewing = reviewMode || (if (isArrival) arrivalPending?.status else departurePending?.status)?.let { it != "pending" } == true
    var localDiscarded by rememberSaveable { mutableStateOf(false) }
    var localApplied by rememberSaveable { mutableStateOf(reviewing) }
    val onlineArrivalInput = arrival?.let { SaveFlightArrivalRequest(it.ata, it.aircraftTypeId, it.aircraftTailNumber,
        it.customerWorkReferenceNumber, it.remarks, targetSubmissionId = submissionId.takeIf(String::isNotBlank)) }
        ?: SaveFlightArrivalRequest("", "", "", targetSubmissionId = submissionId.takeIf(String::isNotBlank))
    val onlineDepartureInput = departure?.let { SaveFlightDepartureRequest(it.atd, remarks = it.remarks,
        targetSubmissionId = submissionId.takeIf(String::isNotBlank)) }
        ?: SaveFlightDepartureRequest("", targetSubmissionId = submissionId.takeIf(String::isNotBlank))
    val initialArrival = if (reviewing && arrivalInput != null) mergeArrivalReview(onlineArrivalInput, arrivalInput)
        else arrivalInput ?: onlineArrivalInput
    val initialDeparture = if (reviewing && departureInput != null) mergeDepartureReview(onlineDepartureInput, departureInput)
        else departureInput ?: onlineDepartureInput
    var applyLocalSignature by rememberSaveable { mutableStateOf(!isArrival && departurePending?.signatureFile != null) }
    val initialTime = if (isArrival) initialArrival.ata else initialDeparture.atd
    var originalTime by rememberSaveable { mutableStateOf(initialTime) }
    var time by rememberSaveable { mutableStateOf(initialTime.takeIf(String::isNotBlank)?.let { operationLocalTime(it, zone) }.orEmpty()) }
    var aircraftId by rememberSaveable { mutableStateOf(initialArrival.aircraftTypeId) }
    var tail by rememberSaveable { mutableStateOf(initialArrival.aircraftTailNumber.uppercase(java.util.Locale.ROOT)) }
    var reference by rememberSaveable { mutableStateOf(initialArrival.customerWorkReferenceNumber.orEmpty().uppercase(java.util.Locale.ROOT)) }
    var remarks by rememberSaveable { mutableStateOf(if (isArrival) initialArrival.remarks.orEmpty() else initialDeparture.remarks.orEmpty()) }
    var signature by rememberSaveable { mutableStateOf("") }
    var signedAt by rememberSaveable { mutableStateOf(departureInput?.signatureSignedAtUtc.takeIf { applyLocalSignature }) }
    var reviewedJson by rememberSaveable { mutableStateOf(Json.encodeToString(flight.let { original ->
        if (!reviewing && isArrival && arrivalPending != null) {
            val reviewed = arrivalPending.reviewedArrivalJson?.let { runCatching { Json.decodeFromString<FlightArrivalSubmission?>(it) }.getOrNull() }
            original.copy(rowVersion = arrivalPending.rowVersion, arrivalSubmissions = original.arrivalSubmissions.map {
                if (it.id.equals(submissionId, true)) reviewed ?: it else it
            })
        } else if (!reviewing && !isArrival && departurePending != null) original.copy(rowVersion = departurePending.rowVersion,
            departureSubmissions = Json.decodeFromString(departurePending.reviewedDepartureJson))
        else original
    })) }
    val reviewed = remember(reviewedJson) { Json.decodeFromString<FlightDetail>(reviewedJson) }
    val inputTime = arrivalInputTime(time, originalTime, zone)
    val queuedArrivalTime = remember(pendingArrival?.inputJson, pendingArrival?.status, pendingArrival?.rowVersion) {
        pendingArrival?.takeIf { it.status == "pending" }?.let { row -> runCatching {
            Json.decodeFromString<SaveFlightArrivalRequest>(row.inputJson).takeIf { it.validationErrors(row.rowVersion).isEmpty() }?.ata
        }.getOrNull() }
    }
    val actualArrivalTime = queuedArrivalTime ?: reviewed.visibleArrival()?.ata
    val departureBeforeArrival = !isArrival && runCatching {
        OffsetDateTime.parse(inputTime).toInstant() < OffsetDateTime.parse(actualArrivalTime).toInstant()
    }.getOrDefault(false)
    val departureMissingArrival = !isArrival && actualArrivalTime.isNullOrBlank()
    fun arrivalValue() = SaveFlightArrivalRequest(inputTime, aircraftId, tail, reference, remarks,
        targetSubmissionId = submissionId.takeIf { it.isNotBlank() }).normalized()
    fun departureValue() = SaveFlightDepartureRequest(inputTime, remarks = remarks, targetSubmissionId = submissionId.takeIf { it.isNotBlank() }).normalized()
    var baseline by rememberSaveable { mutableStateOf(if (isArrival) Json.encodeToString(arrivalValue()) else Json.encodeToString(departureValue())) }
    val changed = (if (isArrival) Json.encodeToString(arrivalValue()) else Json.encodeToString(departureValue())) != baseline || signature.isNotBlank()
    var saving by remember { mutableStateOf(false) }
    var saveError by rememberSaveable { mutableStateOf<String?>(null) }
    var attempted by rememberSaveable { mutableStateOf(false) }
    var reviewedChangeId by rememberSaveable { mutableStateOf<String?>(null) }
    val pendingChangeId = if (isArrival) arrivalPending?.changeId else departurePending?.changeId
    val reviewCardChangeId = rememberSaveable { mutableStateOf(pendingChangeId) }.value
    val pendingStatus = if (isArrival) arrivalPending?.status else departurePending?.status
    fun reviewTarget(): FlightOperationReviewTarget {
        val staged = if (isArrival) {
            val online = Json.decodeFromString<SaveFlightArrivalRequest>(baseline)
            Json.encodeToString(if (localApplied && arrivalInput != null) mergeArrivalReview(online, arrivalInput) else online)
        } else {
            val online = Json.decodeFromString<SaveFlightDepartureRequest>(baseline)
            val expected = if (localApplied && departureInput != null) mergeDepartureReview(online,
                departureInput.copy(signatureContent = if (departurePending?.signatureFile != null) "provided" else null)) else online
            Json.encodeToString(expected.copy(signatureContent = null))
        }
        return FlightOperationReviewTarget(if (isArrival) "arrival" else "departure", changeId = checkNotNull(reviewCardChangeId),
            useLocalSource = localApplied, stagedInputJson = staged)
    }

    val needsReview = pendingStatus != null && pendingStatus != "pending" && reviewedChangeId != pendingChangeId
    val eligible = if (isArrival) {
        if (submissionId.isBlank()) reviewed.arrivalEditable() else reviewed.canUpdateArrivalSubmission(submissionId)
    } else if (submissionId.isBlank()) reviewed.departureEditable() else reviewed.canUpdateDepartureSubmission(submissionId)
    val enabled = eligible && !saving
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
    val requiredRemarksMissing = isArrival && reviewed.requiresArrivalRemarks() && remarks.isBlank()
    var savedPreview by remember { mutableStateOf<ByteArray?>(null) }
    var previewLoading by remember { mutableStateOf(false) }
    var onlineSignaturePreview by remember { mutableStateOf<ByteArray?>(null) }
    var previewRetry by remember { mutableIntStateOf(0) }
    val reviewedDeparture = if (submissionId.isBlank()) reviewed.visibleDeparture() else reviewed.departureSubmissions.firstOrNull { it.id.equals(submissionId, true) }
    LaunchedEffect(reviewedDeparture, previewRetry) {
        if (reviewing && !isArrival && reviewedDeparture?.hasSignature == true) {
            try { onlineSignaturePreview = loadSignature(reviewed.copy(departureSubmissions = listOf(reviewedDeparture)), null) }
            catch (cancelled: CancellationException) { throw cancelled }
            catch (_: Exception) { onlineSignaturePreview = null }
        }
    }
    LaunchedEffect(reviewedDeparture, departurePending?.signatureFile, previewRetry, applyLocalSignature) {
        if (!isArrival && (reviewedDeparture?.hasSignature == true || applyLocalSignature)) {
            previewLoading = true
            savedPreview = null
            try { savedPreview = loadSignature(reviewed.copy(departureSubmissions = listOfNotNull(reviewedDeparture)), if (reviewing && !applyLocalSignature) null else departurePending) }
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
        if (inputTime.isBlank() || departureBeforeArrival || departureMissingArrival || requiredRemarksMissing ||
            isArrival && (errors.isNotEmpty() || !aircraftAvailable || arrivalValue().validationErrors(reviewed.rowVersion).isNotEmpty())) {
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
        val retainedSignaturePreview = savedPreview.takeIf { applyLocalSignature }
        scope.launch {
            try {
                // Review may replace the old queue row inside its transaction. Load its private PNG first,
                // including after recreation when only the saved local-signature choice remains in UI state.
                val preparedDeparture = if (!isArrival) {
                    val png = if (ink.isNotBlank()) withContext(Dispatchers.Default) { signaturePngContent(ink) } else if (applyLocalSignature) {
                        (retainedSignaturePreview ?: loadSignature(reviewed, departurePending))?.let { Base64.getEncoder().encodeToString(it) }
                            ?: error("The saved signature is unavailable. Select it again before saving.")
                    } else null
                    departureSave.copy(signatureContent = png, signatureSignedAtUtc = if (png != null) inkSignedAt else null)
                } else null
                val write: suspend () -> Unit = {
                    if (isArrival) onSaveArrival(reviewed, arrivalSave)
                    else onSaveDeparture(reviewed, checkNotNull(preparedDeparture))
                }
                if (reviewing && !localDiscarded && reviewCardChangeId != null)
                    onSaveReviewed(reviewed, reviewTarget(), write)
                else write()
                baseline = if (isArrival) Json.encodeToString(arrivalValue()) else Json.encodeToString(departureValue())
                onClose()
            } catch (cancelled: CancellationException) { throw cancelled }
            catch (error: Exception) { saveError = error.userMessage(); validationRequest++ }
            finally { saving = false }
        }
    }
    val title = if (reviewing) stringResource(R.string.flight_review_action) else stringResource(if (isArrival) R.string.flight_submission_update_arrival else R.string.flight_submission_update_departure)
    FlightRecordingFormScaffold(flight, title, stringResource(R.string.flight_submission_save), loadCustomerLogo,
        onRequestClose = close, saveEnabled = enabled && (changed || localApplied || reviewing && !localDiscarded || reviewedChangeId != null), saveLoading = saving,
        onSave = { save(false) }, compact = true, validationState = validationState, identityContent = {},
        navigationIcon = Icons.AutoMirrored.Filled.ArrowBack, navigationDescription = stringResource(R.string.account_back), headerTitle = title) {
        saveError?.let { InlineNotice(it, modifier = Modifier.flightFormNavigationTarget("submission-save-error"), kind = InlineNoticeKind.Error) }
        if (needsReview && !reviewing) {
            InlineNotice(stringResource(if (isArrival) R.string.arrival_sync_review else R.string.departure_sync_review), kind = InlineNoticeKind.Warning)
            (if (isArrival) arrivalPending?.error else departurePending?.error)?.let {
                InlineNotice(it, kind = InlineNoticeKind.Error)
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
        if (reviewing) {
            FlightReviewOnlineHeading(cachedReview || flight.fromCache)
            if (isArrival && arrival != null) {
                FlightSavedDetailsCard { FlightArrivalSummary(arrival, zone) }
                FlightSubmissionNote(arrival.createdByDisplayName ?: arrival.submittedByEmployee.fullName, arrival.createdAtUtc, zone,
                    updatedByDisplayName = arrival.updatedByDisplayName, updatedAtUtc = arrival.updatedAtUtc)
            } else if (!isArrival && departure != null) {
                FlightSavedDetailsCard { FlightDepartureSummary(departure, zone, onlineSignaturePreview) }
                FlightSubmissionNote(departure.createdByDisplayName ?: departure.submittedByEmployee.fullName, departure.createdAtUtc, zone,
                    updatedByDisplayName = departure.updatedByDisplayName, updatedAtUtc = departure.updatedAtUtc)
            } else Text(stringResource(R.string.flight_review_empty_online), Modifier.padding(horizontal = 14.dp),
                style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            if (!localDiscarded) FlightReviewLocalHeading()
        }
        FlightFormSectionTitle(stringResource(if (isArrival) R.string.operations_arrival else R.string.operations_departure))
        FlightFormFieldGroup {
            Column {
                FlightFormDateTimeField(time, stringResource(if (isArrival) R.string.operations_actual_arrival else R.string.operations_actual_departure),
                    stringResource(if (isArrival) R.string.arrival_choose_ata else R.string.operations_choose_atd), enabled = enabled,
                    errorMessage = if (attempted) when {
                        inputTime.isBlank() -> stringResource(if (isArrival) R.string.arrival_ata_required else R.string.departure_atd_required)
                        departureMissingArrival -> stringResource(R.string.flight_arrival_before_work)
                        departureBeforeArrival -> stringResource(R.string.flight_departure_before_arrival)
                        else -> null
                    } else null,
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
                    FlightFormTextRow(stringResource(R.string.arrival_tail_number), tail.uppercase(java.util.Locale.ROOT), { tail = it.uppercase(java.util.Locale.ROOT) }, stringResource(R.string.flight_form_required_field),
                        keyboardOptions = KeyboardOptions(capitalization = KeyboardCapitalization.Characters, imeAction = ImeAction.Next), enabled = enabled,
                        errorMessage = if (ArrivalInputError.TailRequired in errors) stringResource(R.string.arrival_tail_required) else null)
                    FlightFormDivider()
                    FlightFormTextRow(stringResource(R.string.arrival_reference), reference.uppercase(java.util.Locale.ROOT), { reference = it.uppercase(java.util.Locale.ROOT) }, "", enabled = enabled)
                } else FlightSignatureField(signature, hasSavedSignature = reviewedDeparture?.hasSignature == true || applyLocalSignature, savedPreview = savedPreview, loading = previewLoading,
                    enabled = enabled, onOpen = { picker = "signature" })
                FlightFormDivider()
                FlightFormValidation(if (attempted && requiredRemarksMissing) stringResource(R.string.flight_unknown_customer_arrival_remarks) else null) {
                    FlightFormRemarks(remarks, { remarks = it }, stringResource(if (isArrival) R.string.operations_arrival_remarks else R.string.operations_departure_remarks), enabled = enabled,
                        heading = stringResource(if (isArrival) R.string.operations_arrival_remarks_heading else R.string.operations_departure_remarks_heading), inGroup = true,
                        required = isArrival && reviewed.requiresArrivalRemarks())
                }
            }
        }
        if (isArrival && reviewed.requiresArrivalRemarks()) Text(stringResource(R.string.flight_unknown_customer_arrival_remarks),
            Modifier.padding(horizontal = 14.dp), style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant)
        if (reviewing && !localDiscarded && reviewCardChangeId != null) {
            FlightReviewActions(!saving, onDiscard = {
                saving = true; saveError = null
                scope.launch {
                    try {
                        onDiscardReviewed(FlightOperationReviewTarget(if (isArrival) "arrival" else "departure", changeId = reviewCardChangeId))
                        localDiscarded = true; localApplied = false; applyLocalSignature = false; signature = ""; signedAt = null
                        val onlineArrival = reviewed.arrivalSubmissions.firstOrNull { it.id == submissionId } ?: reviewed.visibleArrival()
                        val onlineDeparture = reviewed.departureSubmissions.firstOrNull { it.id == submissionId } ?: reviewed.visibleDeparture()
                        originalTime = if (isArrival) onlineArrival?.ata.orEmpty() else onlineDeparture?.atd.orEmpty()
                        time = originalTime.takeIf(String::isNotBlank)?.let { operationLocalTime(it, zone) }.orEmpty()
                        aircraftId = onlineArrival?.aircraftTypeId.orEmpty(); tail = onlineArrival?.aircraftTailNumber.orEmpty()
                        reference = onlineArrival?.customerWorkReferenceNumber.orEmpty()
                        remarks = if (isArrival) onlineArrival?.remarks.orEmpty() else onlineDeparture?.remarks.orEmpty()
                        baseline = if (isArrival) Json.encodeToString(arrivalValue()) else Json.encodeToString(departureValue())
                    } catch (cancelled: CancellationException) { throw cancelled }
                    catch (error: Exception) { saveError = error.userMessage(); validationRequest++ }
                    finally { saving = false }
                }
            })
        }
        if (ArrivalInputError.VersionRequired in errors) FlightFormValidation(stringResource(R.string.arrival_reload_required)) {}
    }
    if (picker == "time") FlightFormDateTimeSheet(LocalDateTime.parse(time), LocalDate.now(zone).year,
        onValueChange = { time = it.toString() }, onDismiss = { picker = null },
        title = stringResource(if (isArrival) R.string.arrival_ata else R.string.operations_atd), showTitle = false)
    if (picker == "aircraft") FlightWorkPicker(title = stringResource(R.string.arrival_aircraft_type), searchLabel = stringResource(R.string.arrival_search_aircraft),
        emptyLabel = stringResource(R.string.arrival_no_aircraft), options = aircraftTypes.sortedBy { it.model }.map { FlightWorkChoice(it.id, it.model, it.manufacturer) },
        selectedId = aircraftId, onDismiss = { picker = null }, onSelect = { aircraftId = it })
    if (picker == "signature") FlightSignatureSheet(signature, reviewedDeparture?.hasSignature == true || applyLocalSignature, savedPreview, previewLoading, onRetry = { previewRetry++ },
        onValueChange = { signature = it; applyLocalSignature = false; signedAt = if (it.isBlank()) null else Instant.now().toString() }, onDismiss = { picker = null })
    if (confirmDiscard) FlightFormDiscardDialog(stringResource(R.string.operations_discard_title), onDismiss = { confirmDiscard = false }, onDiscard = onClose)
    if (confirmCanceled) FlightSubmissionConfirmation(stringResource(R.string.submission_canceled_warning_title), stringResource(R.string.submission_canceled_warning),
        stringResource(R.string.submission_save_anyway), onConfirm = { confirmCanceled = false; save(true) }, onDismiss = { confirmCanceled = false })
}
