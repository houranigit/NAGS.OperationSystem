package com.nags.operations.ui.flights

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.FlightLand
import androidx.compose.material.icons.filled.FlightTakeoff
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.font.FontStyle
import androidx.compose.ui.text.SpanStyle
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.withStyle
import androidx.compose.ui.unit.dp
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.luminance
import androidx.compose.ui.Alignment
import com.nags.operations.R
import com.nags.operations.data.*
import com.nags.operations.ui.util.operationLocalTime
import com.nags.operations.ui.util.stationTimeZone
import java.time.Instant
import java.time.LocalDateTime
import java.time.ZoneId
import java.util.UUID
import kotlinx.serialization.Serializable
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import kotlinx.coroutines.launch
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.flow.Flow
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import com.nags.operations.ui.components.InlineNotice
import com.nags.operations.ui.components.InlineNoticeKind

/** Each occurrence owns its work and local picker state; no normal-flight entry is reused. */
@Serializable
internal data class FlightRtrDraft(
    val id: String = UUID.randomUUID().toString(), val sequence: Int,
    val arrival: FlightWorkPeriod = FlightWorkPeriod(), val arrivalRemarks: String = "",
    val departure: FlightWorkPeriod = FlightWorkPeriod(), val departureRemarks: String = "",
    val work: FlightWorkDrafts = FlightWorkDrafts(), val signature: String = "", val signedAtUtc: String? = null,
) {
    fun input(zone: ZoneId) = SaveFlightReturnToRampRequest(
        FlightReturnToRampArrivalInput(arrivalInputTime(arrival.start, arrival.originalStartUtc, zone), arrivalRemarks),
        departure.start.takeIf(String::isNotBlank)?.let {
            FlightReturnToRampDepartureInput(arrivalInputTime(it, departure.originalStartUtc, zone), departureRemarks,
                signatureSignedAtUtc = signedAtUtc)
        }, work.services.map { FlightReturnToRampServiceInput(it.id, it.serviceInput(zone)) },
        work.tasks.map { FlightReturnToRampTaskInput(it.id, it.taskInput(zone)) }).normalized()

    fun fingerprint() = Json.encodeToString(copy(work = FlightWorkDrafts())) +
        serviceEditorFingerprint(work.services, work.tasks, emptyList())
}

internal fun FlightReturnToRampRecord.rtrDraft(catalogs: FlightOperationsCatalogs, zone: ZoneId,
    input: SaveFlightReturnToRampRequest = input()) = FlightRtrDraft(id, sequence,
    FlightWorkPeriod(operationLocalTime(input.arrival.arrivedAtUtc, zone), originalStartUtc = input.arrival.arrivedAtUtc),
    input.arrival.remarks.orEmpty(),
    FlightWorkPeriod(input.departure?.let { operationLocalTime(it.departedAtUtc, zone) }.orEmpty(),
        originalStartUtc = input.departure?.departedAtUtc), input.departure?.remarks.orEmpty(),
    FlightWorkDrafts(input.services.map { entry ->
        val old = services.firstOrNull { it.id == entry.recordId }
        entry.input.toEditorDraft(entry.recordId, old?.serviceName ?: catalogs.services.firstOrNull { it.id == entry.input.serviceId }?.name.orEmpty(), old, catalogs, zone)
    }, input.tasks.map { it.input.toTaskEditorDraft(it.recordId, tasks.firstOrNull { old -> old.id == it.recordId }, catalogs, zone) }),
    signedAtUtc = input.departure?.signatureSignedAtUtc ?: signatureSignedAtUtc)

/** Validate through the same service/task editors with the occurrence's own actual-time bounds. */
internal fun FlightRtrDraft.errors(flight: FlightDetail, catalogs: FlightOperationsCatalogs, zone: ZoneId,
    baseline: FlightReturnToRampRecord?): Map<String, Map<String, Int>> {
    val input = input(zone)
    val synthetic = flight.copy(services = baseline?.services.orEmpty(), tasks = baseline?.tasks.orEmpty())
    val result = linkedMapOf<String, Map<String, Int>>()
    input.validationErrors(flight.rowVersion, flight.allowedServiceIds?.toSet(), taskResourceCatalogs(catalogs), baseline)
        .takeIf { it.isNotEmpty() }?.let { fields ->
            result["actuals"] = fields.filter { it.startsWith("arrival") || it.startsWith("departure") || it in setOf("work", "rowVersion") }
                .associateWith { if (it == "work") R.string.rtr_form_work_required else R.string.rtr_form_invalid_actuals }
        }
    work.services.forEach { item ->
        val previous = baseline?.services?.firstOrNull { it.id == item.id }
        val unchanged = previous != null && serviceInputFingerprint(previous.input()) == serviceInputFingerprint(item.serviceInput(zone))
        result[item.id] = if (unchanged) emptyMap() else serviceEditorValidation(item, item.serviceInput(zone), synthetic,
            catalogs, flight.rowVersion, input.arrival.arrivedAtUtc, input.departure?.departedAtUtc)
    }
    serviceActualBoundsValidation(work.services, zone, input.arrival.arrivedAtUtc, input.departure?.departedAtUtc)
        .forEach { (id, fields) -> result[id] = result[id].orEmpty() + fields }
    work.tasks.forEach { item -> result[item.id] = taskEditorValidation(item, item.taskInput(zone), synthetic,
        catalogs, flight.rowVersion, input.arrival.arrivedAtUtc, input.departure?.departedAtUtc) }
    val overlaps = serviceOverlapValidation(work.services, zone, work.services.map { it.id }.toSet())
    overlaps.forEach { (id, fields) -> result[id] = result[id].orEmpty() + fields }
    return result.filterValues { it.isNotEmpty() }
}

@Composable
internal fun FlightRtrFields(draft: FlightRtrDraft, flight: FlightDetail, catalogs: FlightOperationsCatalogs,
    enabled: Boolean, attempted: Boolean, baseline: FlightReturnToRampRecord? = null,
    savedSignature: ByteArray? = null, onChange: (FlightRtrDraft) -> Unit, onRemove: (() -> Unit)? = null,
    openArrivalImmediately: Boolean = false,
) {
    val zone = stationTimeZone(flight.stationTimeZoneId)
    val year = LocalDateTime.now(zone).year
    var picker by rememberSaveable(draft.id) { mutableStateOf<String?>(if (openArrivalImmediately) "arrival" else null) }
    val errors = if (attempted) draft.errors(flight, catalogs, zone, baseline) else emptyMap()
    fun open(kind: String) {
        val value = LocalDateTime.now(zone).withSecond(0).withNano(0).toString()
        if (kind == "departure" && draft.departure.start.isBlank())
            onChange(draft.copy(departure = FlightWorkPeriod(start = value)))
        picker = kind
    }
    Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
        FlightFormSectionTitle(stringResource(R.string.rtr_form_number, draft.sequence))
        FlightFormValidation(if (attempted && draft.arrival.start.isBlank()) stringResource(R.string.rtr_form_invalid_actuals) else null) {
        FlightFormFieldGroup {
            if (onRemove != null) FlightWorkDraftLabel(stringResource(R.string.rtr_form_unsaved))
            if (onRemove != null) FlightWorkRemovableEntry(draft.id,
                stringResource(R.string.rtr_form_remove), onRemove = onRemove, compactDelete = true, enabled = enabled) {
                FlightFormDateTimeField(draft.arrival.start, stringResource(R.string.operations_actual_arrival),
                    stringResource(R.string.arrival_choose_ata), enabled = enabled, leadingPadding = 0.dp,
                    displayPattern = "dd/MM/yy HH:mm", prominentLabel = true, onClick = { open("arrival") })
            } else FlightFormDateTimeField(draft.arrival.start, stringResource(R.string.operations_actual_arrival),
                stringResource(R.string.arrival_choose_ata), enabled = enabled, leadingPadding = 0.dp,
                displayPattern = "dd/MM/yy HH:mm", prominentLabel = true,
                leadingContent = { FlightActualTimeIcon(Icons.Filled.FlightLand) }, onClick = { open("arrival") })
            FlightFormDivider()
            FlightFormRemarks(draft.arrivalRemarks, { onChange(draft.copy(arrivalRemarks = it)) },
                stringResource(R.string.operations_arrival_remarks), enabled = enabled,
                heading = stringResource(R.string.operations_arrival_remarks_heading), inGroup = true)
        }
        }
        errors["actuals"]?.values?.distinct()?.forEach { message -> FlightFormValidation(stringResource(message)) { } }
        val bounds = flightWorkTimeBounds(draft.input(zone).arrival.arrivedAtUtc, draft.input(zone).departure?.departedAtUtc)
        FlightServiceSection(draft.work.services, catalogs, year, zone,
            onChange = { change -> onChange(draft.copy(work = draft.work.copy(services = change(draft.work.services)))) },
            enabled = enabled, errors = errors, defaultEmployee = defaultServiceEmployee(flight, catalogs), timeBounds = bounds)
        FlightTaskSection(draft.work.tasks, catalogs, year, zone,
            onChange = { change -> onChange(draft.copy(work = draft.work.copy(tasks = change(draft.work.tasks)))) },
            enabled = enabled, errors = errors, defaultEmployee = defaultServiceEmployee(flight, catalogs), timeBounds = bounds)
        FlightFormSectionTitle(stringResource(R.string.operations_departure))
        FlightFormFieldGroup {
            if (draft.departure.start.isBlank()) FlightWorkAddRow(stringResource(R.string.operations_actual_departure),
                enabled = enabled, onClick = { open("departure") })
            else {
                FlightWorkOptionalRemoval(baseline?.departedAtUtc == null, "${draft.id}:departure",
                    stringResource(R.string.work_remove_item, stringResource(R.string.operations_actual_departure)),
                    onRemove = { onChange(draft.copy(departure = FlightWorkPeriod(), departureRemarks = "", signature = "", signedAtUtc = null)) },
                    enabled = enabled, compactDelete = true) {
                    FlightFormDateTimeField(draft.departure.start, stringResource(R.string.operations_actual_departure),
                        stringResource(R.string.operations_choose_atd), enabled = enabled, leadingPadding = 0.dp,
                        displayPattern = "dd/MM/yy HH:mm", prominentLabel = true, onClick = { open("departure") })
                }
                FlightFormDivider()
                FlightSignatureField(draft.signature, baseline?.hasSignature == true, savedSignature,
                    enabled = enabled, onOpen = { open("signature") })
                FlightFormDivider()
                FlightFormRemarks(draft.departureRemarks, { onChange(draft.copy(departureRemarks = it)) },
                    stringResource(R.string.operations_departure_remarks), enabled = enabled,
                    heading = stringResource(R.string.operations_departure_remarks_heading), inGroup = true)
            }
        }
    }
    if (picker == "arrival" || picker == "departure") {
        val arrival = picker == "arrival"
        val time = if (arrival) draft.arrival.start else draft.departure.start
        FlightFormDateTimeSheet(runCatching { LocalDateTime.parse(time) }.getOrElse { LocalDateTime.now(zone).withSecond(0).withNano(0) }, year,
            onValueChange = { if (arrival) onChange(draft.copy(arrival = draft.arrival.copy(start = it.toString())))
                else onChange(draft.copy(departure = draft.departure.copy(start = it.toString()))) },
            onDismiss = { picker = null }, title = stringResource(if (arrival) R.string.arrival_ata else R.string.operations_atd), showTitle = false)
    }
    if (picker == "signature") FlightSignatureSheet(draft.signature, baseline?.hasSignature == true, savedSignature, loading = false, onRetry = {},
        onValueChange = { onChange(draft.copy(signature = it, signedAtUtc = Instant.now().toString())) }, onDismiss = { picker = null })
}

@Composable
internal fun FlightRtrUpdateForm(flight: FlightDetail, record: FlightReturnToRampRecord,
    catalogs: FlightOperationsCatalogs, loadLogo: (String) -> Flow<ByteArray?>,
    loadInput: suspend () -> SaveFlightReturnToRampRequest?, loadSignature: suspend () -> ByteArray?,
    loadAttachment: suspend (String, String, String, String) -> String?,
    onSave: suspend (FlightDetail, String, SaveFlightReturnToRampRequest, FlightReturnToRampRecord) -> Unit,
    onReload: suspend () -> FlightDetail, onClose: () -> Unit,
) {
    val zone = stationTimeZone(flight.stationTimeZoneId)
    var draftJson by rememberSaveable { mutableStateOf(Json.encodeToString(record.rtrDraft(catalogs, zone))) }
    var baseline by rememberSaveable { mutableStateOf(Json.decodeFromString<FlightRtrDraft>(draftJson).fingerprint()) }
    var reviewedJson by rememberSaveable { mutableStateOf(Json.encodeToString(flight)) }
    val reviewed = remember(reviewedJson) { Json.decodeFromString<FlightDetail>(reviewedJson) }
    val draft = remember(draftJson) { Json.decodeFromString<FlightRtrDraft>(draftJson) }
    var saving by remember { mutableStateOf(false) }
    var ready by remember { mutableStateOf(false) }
    var attempted by rememberSaveable { mutableStateOf(false) }
    var error by rememberSaveable { mutableStateOf<String?>(null) }
    var signature by remember { mutableStateOf<ByteArray?>(null) }
    var discard by rememberSaveable { mutableStateOf(false) }
    var reviewConfirmed by rememberSaveable { mutableStateOf(record.localStatus == null || record.localStatus == "pending") }
    val scope = rememberCoroutineScope()
    val validation = rememberFlightFormValidationState()
    val media = rememberFlightMediaSession()
    val errors = if (attempted) draft.errors(reviewed, catalogs, zone, record) else emptyMap()
    fun update(value: FlightRtrDraft) { draftJson = Json.encodeToString(value) }
    LaunchedEffect(record.id) {
        try {
            val pending = loadInput()
            if (pending != null && draft.fingerprint() == baseline) {
                val restored = record.rtrDraft(catalogs, zone, pending)
                update(restored); baseline = restored.fingerprint()
            }
            signature = loadSignature()
        } catch (cancelled: CancellationException) { throw cancelled }
        catch (failure: Exception) { error = failure.userMessage() }
        finally { ready = true }
    }
    val attachmentKeys = draft.work.services.flatMap { row -> row.attachments.map { Triple("services", row.id, it) } } +
        draft.work.tasks.flatMap { row -> row.attachments.map { Triple("tasks", row.id, it) } }
    LaunchedEffect(attachmentKeys.map { it.third.id }) {
        for ((kind, recordId, attachment) in attachmentKeys) {
            if (attachment.uri.isNotBlank()) continue
            try {
                val uri = loadAttachment(kind, recordId, attachment.id, attachment.uploadedAtUtc.orEmpty()) ?: continue
                val current = Json.decodeFromString<FlightRtrDraft>(draftJson)
                fun media(items: List<FlightAttachmentDraft>) = items.map { if (it.id == attachment.id) it.copy(uri = uri) else it }
                update(current.copy(work = current.work.copy(
                    services = current.work.services.map { if (it.id == recordId) it.copy(attachments = media(it.attachments)) else it },
                    tasks = current.work.tasks.map { if (it.id == recordId) it.copy(attachments = media(it.attachments)) else it })))
            } catch (cancelled: CancellationException) { throw cancelled }
            catch (_: Exception) { /* Recorded metadata and retained files remain available for retry. */ }
        }
    }
    val changed = draft.fingerprint() != baseline
    val close = { media.clear(); onClose() }
    FlightRecordingFormScaffold(reviewed, stringResource(R.string.rtr_form_title), stringResource(R.string.operations_save),
        loadLogo, onRequestClose = { if (!saving) { if (changed) discard = true else close() } },
        saveEnabled = ready && !saving && reviewConfirmed && (changed || record.localStatus?.let { it != "pending" } == true),
        saveLoading = saving, compact = true, navigationIcon = Icons.AutoMirrored.Filled.ArrowBack,
        headerTitle = stringResource(R.string.rtr_form_number, record.sequence), validationState = validation, identityContent = {},
        onSave = {
            attempted = true
            val fields = draft.errors(reviewed, catalogs, zone, record)
            if (fields.isEmpty() && !media.hasActiveRecording) {
                saving = true; error = null
                scope.launch {
                    try {
                        val png = draft.signature.takeIf(String::isNotBlank)?.let { withContext(Dispatchers.Default) { signaturePngContent(it) } }
                        val input = draft.input(zone).let { it.copy(departure = it.departure?.copy(signatureContent = png)) }
                        onSave(reviewed, draft.id, input, draft.preview(reviewed, catalogs, record)); close()
                    } catch (cancelled: CancellationException) { throw cancelled }
                    catch (failure: Exception) { error = failure.userMessage() }
                    finally { saving = false }
                }
            } else scope.launch { androidx.compose.runtime.withFrameNanos { }; validation.revealFirstError() }
        }) {
        error?.let { InlineNotice(it, kind = InlineNoticeKind.Error) }
        if (record.localStatus?.let { it != "pending" } == true) {
            FlightSavedDetailsCard { FlightRtrSummary(reviewed.returnToRamps.firstOrNull { it.id == record.id } ?: record, reviewed) }
        }
        if (!reviewConfirmed) {
            androidx.compose.material3.TextButton(enabled = !saving, onClick = {
                saving = true
                scope.launch {
                    try { reviewedJson = Json.encodeToString(onReload()); reviewConfirmed = true; error = null }
                    catch (cancelled: CancellationException) { throw cancelled }
                    catch (failure: Exception) { error = failure.userMessage() }
                    finally { saving = false }
                }
            }) { Text(stringResource(R.string.flight_review_action)) }
        }
        CompositionLocalProvider(LocalFlightMediaSession provides media) {
            FlightRtrFields(draft, reviewed, catalogs, enabled = ready && !saving && reviewConfirmed,
                attempted = attempted, baseline = record, savedSignature = signature, onChange = ::update)
        }
    }
    if (discard) FlightFormDiscardDialog(stringResource(R.string.operations_discard_title),
        onDismiss = { discard = false }, onDiscard = close)
}

@Composable
internal fun FlightRtrSummary(record: FlightReturnToRampRecord, flight: FlightDetail) {
    val zone = stationTimeZone(flight.stationTimeZoneId)
    val green = if (MaterialTheme.colorScheme.surface.luminance() < 0.5f) Color(0xFF81C995) else Color(0xFF248A3D)
    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
        FlightRtrSummaryHeader(record.arrivedAtUtc, record.departedAtUtc, zone) {
            Text(stringResource(R.string.rtr_form_number, record.sequence), style = MaterialTheme.typography.labelLarge,
                fontWeight = FontWeight.SemiBold, color = MaterialTheme.colorScheme.onSurfaceVariant)
            Text(stringResource(if (record.isOpen) R.string.rtr_sheet_open else R.string.rtr_sheet_closed),
                style = MaterialTheme.typography.labelMedium, color = if (record.isOpen) MaterialTheme.colorScheme.primary else green)
            Text(stringResource(R.string.rtr_form_work_summary, record.services.size, record.tasks.size),
                style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
        listOf(R.string.operations_arrival_remarks_heading to record.arrivalRemarks,
            R.string.operations_departure_remarks_heading to record.departureRemarks).forEach { (label, remarks) ->
            remarks?.takeIf(String::isNotBlank)?.let {
                val heading = stringResource(label)
                val quoted = stringResource(R.string.arrival_quoted_remarks, it)
                Text(buildAnnotatedString { append(heading); append(": ")
                    withStyle(SpanStyle(fontStyle = FontStyle.Italic)) { append(quoted) }
                }, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
        }
    }
}

/** Private local summary uses catalog names and preserves published authors when correcting work. */
internal fun FlightRtrDraft.preview(flight: FlightDetail, catalogs: FlightOperationsCatalogs,
    baseline: FlightReturnToRampRecord? = null): FlightReturnToRampRecord {
    val input = input(stationTimeZone(flight.stationTimeZoneId))
    val now = Instant.now().toString()
    val actor = catalogs.stationStaff.firstOrNull { it.id.equals(flight.currentStaffMemberId, true) }
    val actorId = ""
    fun attachments(values: List<FlightServiceAttachmentInput>?) = values.orEmpty().map {
        FlightServiceAttachmentRecord(it.id, it.kind, it.fileName, it.contentType, 0, actorId, now)
    }
    val services = input.services.map { entry ->
        val old = baseline?.services?.firstOrNull { it.id == entry.recordId }; val value = entry.input
        val draft = work.services.first { it.id == entry.recordId }
        FlightServiceRecord(entry.recordId, value.serviceId, draft.serviceName, value.startUtc, value.endUtc,
            value.customerReferenceNumber, value.notes, old?.createdByUserId ?: actorId, old?.endedByUserId,
            old?.createdAtUtc ?: now, now, actorId, value.performers.map { person ->
                val staff = draft.employees.first { it.id == person.id }
                FlightServicePerformerRecord(person.id, person.staffMemberId, staff.employeeName, staff.employeeCode, person.startUtc, person.endUtc)
            }, true, true, attachments(value.attachments), true, true, old?.createdByDisplayName ?: actor?.fullName)
    }
    fun resources(values: List<SaveFlightTaskResourceRequest>, drafts: List<FlightResourceDraft>) = values.map { item ->
        val resource = drafts.first { it.id == item.id }
        FlightTaskResourceRecord(item.id, item.resourceId, resource.resourceName, resource.calculationType ?: if (item.quantity != null) 0 else 1,
            item.quantity, item.startUtc, item.endUtc, item.notes)
    }
    val tasks = input.tasks.map { entry ->
        val old = baseline?.tasks?.firstOrNull { it.id == entry.recordId }; val value = entry.input
        val draft = work.tasks.first { it.id == entry.recordId }
        FlightTaskRecord(entry.recordId, value.taskType, value.startUtc, value.endUtc, value.ataChapterId,
            draft.ataChapterCode, draft.ataChapterTitle, value.customerReferenceNumber, value.notes,
            value.performers.map { person -> val staff = draft.employees.first { it.id == person.id }
                FlightTaskPerformerRecord(person.id, person.staffMemberId, staff.employeeName, staff.employeeCode, person.startUtc, person.endUtc, person.notes) },
            resources(value.tools, draft.tools), resources(value.materials, draft.materials), resources(value.generalSupports, draft.generalSupports),
            attachments(value.attachments), old?.createdByUserId ?: actorId, old?.endedByUserId, old?.createdAtUtc ?: now,
            actorId, now, true, true, true, true, old?.createdByDisplayName ?: actor?.fullName)
    }
    return FlightReturnToRampRecord(id, sequence, input.arrival.arrivedAtUtc,
        baseline?.receivedByStaffMemberId ?: flight.currentStaffMemberId.orEmpty(), baseline?.receivedByFullName ?: actor?.fullName.orEmpty(),
        input.arrival.remarks, input.departure?.departedAtUtc, input.departure?.remarks,
        if (input.departure != null) flight.currentStaffMemberId else null, if (input.departure != null) actor?.fullName else null,
        signature.isNotBlank() || baseline?.hasSignature == true, signedAtUtc ?: baseline?.signatureSignedAtUtc,
        baseline?.createdByUserId ?: actorId, baseline?.createdAtUtc ?: now, actorId, if (signature.isBlank()) baseline?.updatedAtUtc else now,
        baseline?.createdByDisplayName ?: actor?.fullName, actor?.fullName, true, input.departure == null, services, tasks)
}

internal fun FlightDetail.withPendingReturns(rows: List<com.nags.operations.data.db.entities.FlightReturnToRampPendingEntity>): FlightDetail {
    val local = rows.filter { it.flightId == id }.map { it.preview(Json) }
    if (local.isEmpty()) return this
    val ids = local.map { it.id }.toSet()
    return copy(returnToRamps = (returnToRamps.filterNot { it.id in ids } + local).sortedBy { it.sequence })
}
