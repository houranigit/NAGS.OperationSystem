package com.nags.operations.ui.flights

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Check
import androidx.compose.material.icons.outlined.Close
import androidx.compose.material.icons.outlined.Search
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.getValue
import androidx.compose.runtime.setValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.platform.LocalSoftwareKeyboardController
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.font.FontStyle
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.LayoutDirection
import androidx.compose.ui.unit.dp
import com.nags.operations.R
import com.nags.operations.data.AircraftTypeCatalogItem
import com.nags.operations.data.FlightDetail
import com.nags.operations.data.FlightArrivalSubmission
import com.nags.operations.ui.util.operationLocalTime
import com.nags.operations.ui.util.stationTimeZone
import java.time.OffsetDateTime
import java.time.LocalDate
import java.time.LocalDateTime
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.time.Instant
import kotlinx.coroutines.launch
import kotlinx.coroutines.CancellationException
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import com.nags.operations.data.SaveFlightDepartureRequest
import com.nags.operations.data.departureEditable
import com.nags.operations.data.visibleDeparture
import com.nags.operations.data.SaveFlightArrivalRequest
import com.nags.operations.data.ArrivalInputError
import com.nags.operations.data.arrivalEditable
import com.nags.operations.data.visibleArrival
import com.nags.operations.data.validationErrors
import com.nags.operations.data.userMessage
import com.nags.operations.data.db.entities.FlightArrivalPendingEntity
import com.nags.operations.data.db.entities.FlightDeparturePendingEntity
import com.nags.operations.data.arrivalInputTime
import com.nags.operations.data.parentTimeRelaxesBounds
import com.nags.operations.data.FlightServiceChange
import com.nags.operations.data.FlightServiceRecord
import com.nags.operations.data.FlightServiceFormDraft
import com.nags.operations.data.FlightServiceFormDraftAttachment
import com.nags.operations.data.SaveFlightServiceRequest
import com.nags.operations.data.db.entities.FlightServiceFormDraftEntity
import com.nags.operations.data.db.entities.FlightServicePendingEntity

/** Arrival and departure close only after their input is saved on this device. */
@Composable
internal fun FlightOperationsForm(
    flight: FlightDetail,
    aircraftTypes: List<AircraftTypeCatalogItem>,
    loadCustomerLogo: (String) -> Flow<ByteArray?>,
    onClose: () -> Unit,
    catalogs: FlightOperationsCatalogs = FlightOperationsCatalogs(),
    pending: FlightArrivalPendingEntity? = null,
    pendingDeparture: FlightDeparturePendingEntity? = null,
    progressStatus: FlightProgressStatus? = null,
    loadSignature: suspend (FlightDetail, FlightDeparturePendingEntity?) -> ByteArray? = { _, _ -> null },
    onSaveArrival: suspend (FlightDetail, SaveFlightArrivalRequest) -> Unit,
    onReviewArrival: suspend () -> FlightDetail,
    onSaveDeparture: suspend (FlightDetail, SaveFlightDepartureRequest) -> Unit,
    pendingServices: List<FlightServicePendingEntity> = emptyList(),
    onSaveServices: suspend (FlightDetail, List<FlightServiceChange>, FlightServiceFormDraft?) -> Unit = { _, _, _ -> },
    serviceFormDraft: FlightServiceFormDraftEntity? = null,
    onReviewServices: suspend () -> FlightDetail = { flight },
    loadServiceAttachment: suspend (String, String, String, String) -> String? = { _, _, _, _ -> null },
    loadPendingServiceAttachment: suspend (String, String, String) -> String? = { _, _, _ -> null },
    onSaveBatch: suspend (suspend () -> Unit) -> Unit = { save -> save() },
) {
    val entryTimeZoneId = rememberSaveable { flight.stationTimeZoneId }
    val entryZone = remember(entryTimeZoneId) { stationTimeZone(entryTimeZoneId) }
    val zone = entryZone
    val arrival = flight.visibleArrival()
    val departure = flight.visibleDeparture()
    val hasSavedSignature = departure != null || pendingDeparture?.signatureFile != null
    var savedSignaturePreview by remember { mutableStateOf<ByteArray?>(null) }
    var signaturePreviewLoading by remember { mutableStateOf(false) }
    var signaturePreviewRetry by remember { mutableStateOf(0) }
    LaunchedEffect(departure?.id, departure?.signatureVersion, departure?.updatedAtUtc,
        pendingDeparture?.changeId, pendingDeparture?.signatureFile, signaturePreviewRetry) {
        savedSignaturePreview = null
        if (hasSavedSignature) {
            signaturePreviewLoading = true
            try { savedSignaturePreview = loadSignature(flight, pendingDeparture) }
            catch (cancelled: CancellationException) { throw cancelled }
            catch (_: Exception) { /* The existing signature remains unchanged when a preview cannot load. */ }
            finally { signaturePreviewLoading = false }
        }
    }
    val pendingInput = remember(pending?.inputJson) {
        pending?.let { runCatching { Json.decodeFromString<SaveFlightArrivalRequest>(it.inputJson) }.getOrNull() }
    }
    val pendingDepartureInput = remember(pendingDeparture?.inputJson) {
        pendingDeparture?.let { runCatching { Json.decodeFromString<SaveFlightDepartureRequest>(it.inputJson) }.getOrNull() }
    }
    val initialAircraftId = rememberSaveable { pendingInput?.aircraftTypeId ?: arrival?.aircraftTypeId ?: flight.aircraftTypeId.orEmpty() }
    val initialAircraftModel = rememberSaveable { arrival?.aircraftTypeModel ?: flight.aircraftTypeModel.orEmpty() }
    val initialAta = rememberSaveable {
        operationLocalTime(pendingInput?.ata ?: arrival?.ata ?: flight.scheduledArrivalUtc, entryZone)
    }
    val initialAtd = rememberSaveable { (pendingDepartureInput?.atd ?: departure?.atd)?.let { operationLocalTime(it, entryZone) }.orEmpty() }
    val initialDepartureRemarks = rememberSaveable { pendingDepartureInput?.remarks ?: departure?.remarks.orEmpty() }
    val referenceYear = rememberSaveable { LocalDate.now(entryZone).year }
    var aircraftId by rememberSaveable { mutableStateOf(initialAircraftId) }
    var ata by rememberSaveable { mutableStateOf(initialAta) }
    var atd by rememberSaveable { mutableStateOf(initialAtd) }
    var tailNumber by rememberSaveable { mutableStateOf(pendingInput?.aircraftTailNumber ?: arrival?.aircraftTailNumber.orEmpty()) }
    var reference by rememberSaveable { mutableStateOf(pendingInput?.customerWorkReferenceNumber ?: arrival?.customerWorkReferenceNumber.orEmpty()) }
    var arrivalRemarks by rememberSaveable { mutableStateOf(pendingInput?.remarks ?: arrival?.remarks.orEmpty()) }
    var departureRemarks by rememberSaveable { mutableStateOf(initialDepartureRemarks) }
    var signature by rememberSaveable { mutableStateOf("") }
    var signatureSignedAtUtc by rememberSaveable { mutableStateOf<String?>(null) }
    var originalAtd by rememberSaveable { mutableStateOf(pendingDepartureInput?.atd ?: departure?.atd) }
    var departureBaselineAtd by rememberSaveable { mutableStateOf(initialAtd) }
    var departureBaselineRemarks by rememberSaveable { mutableStateOf(initialDepartureRemarks) }
    var departureVersion by rememberSaveable { mutableStateOf(pendingDeparture?.rowVersion ?: flight.rowVersion) }
    var departureReviewedSnapshot by rememberSaveable { mutableStateOf(pendingDeparture?.reviewedDepartureJson ?: Json.encodeToString(flight.departureSubmissions)) }
    var reviewedDepartureChangeId by rememberSaveable { mutableStateOf<String?>(null) }
    var queuedDepartureBaseline by rememberSaveable { mutableStateOf<String?>(pendingDeparture?.inputJson) }
    val initialServices = remember { initialServiceEditorDraft(flight, catalogs, pendingServices, serviceFormDraft?.formJson, entryZone) }
    var workDrafts by rememberSaveable(stateSaver = FlightWorkDraftsSaver) {
        mutableStateOf(FlightWorkDrafts(initialServices.services, initialServices.tasks))
    }
    var suppressedServiceIdsJson by rememberSaveable { mutableStateOf(Json.encodeToString(initialServices.suppressedPlannedServiceIds)) }
    val suppressedServiceIds = Json.decodeFromString<List<String>>(suppressedServiceIdsJson)
    var serviceBaseline by rememberSaveable { mutableStateOf(serviceEditorFingerprint(initialServices.services, initialServices.tasks,
        initialServices.suppressedPlannedServiceIds)) }
    var serviceBaselineInputs by rememberSaveable { mutableStateOf(Json.encodeToString(initialServices.services.associate {
        it.id to serviceInputFingerprint(it.serviceInput(entryZone)) })) }
    var knownServiceIdsJson by rememberSaveable { mutableStateOf(Json.encodeToString((flight.services.map { it.id } + pendingServices.map { it.serviceRecordId }).distinct())) }
    var serviceReviewedJson by rememberSaveable { mutableStateOf(Json.encodeToString(flight.services)) }
    var serviceVersion by rememberSaveable { mutableStateOf(flight.rowVersion) }
    var reviewedServiceChangesJson by rememberSaveable { mutableStateOf("[]") }
    val reviewedServiceChanges = Json.decodeFromString<List<String>>(reviewedServiceChangesJson)
    val servicesChanged = serviceEditorFingerprint(workDrafts.services, workDrafts.tasks, suppressedServiceIds) != serviceBaseline
    val serviceBaselineDraft = checkNotNull(decodeServiceEditorDraft(serviceBaseline))
    val serviceRowsChanged = serviceEditorFingerprint(workDrafts.services, emptyList(), suppressedServiceIds) !=
        serviceEditorFingerprint(serviceBaselineDraft.services, emptyList(), serviceBaselineDraft.suppressedPlannedServiceIds)
    val tasksChanged = serviceEditorFingerprint(emptyList(), workDrafts.tasks, emptyList()) !=
        serviceEditorFingerprint(emptyList(), serviceBaselineDraft.tasks, emptyList())
    val servicesNeedReview = pendingServices.any { it.status != "pending" && it.changeId !in reviewedServiceChanges }
    val serviceReviewed = Json.decodeFromString<List<FlightServiceRecord>>(serviceReviewedJson)
    var originalAta by rememberSaveable { mutableStateOf(pendingInput?.ata ?: arrival?.ata) }
    var arrivalVersion by rememberSaveable { mutableStateOf(pending?.rowVersion ?: flight.rowVersion) }
    var arrivalReviewedSnapshot by rememberSaveable { mutableStateOf(Json.encodeToString(flight.arrivalSubmissions)) }
    fun input() = SaveFlightArrivalRequest(
        arrivalInputTime(ata, originalAta, entryZone),
        aircraftId, tailNumber, reference, arrivalRemarks).normalized()
    var arrivalBaseline by rememberSaveable { mutableStateOf(Json.encodeToString(input())) }
    var queuedBaseline by rememberSaveable { mutableStateOf(pendingInput?.withoutAcknowledgement()?.let { Json.encodeToString(it) }) }
    var reviewedChangeId by rememberSaveable { mutableStateOf<String?>(null) }
    var saving by remember { mutableStateOf(false) }
    var validationAttempted by rememberSaveable { mutableStateOf(false) }
    var saveError by rememberSaveable { mutableStateOf<String?>(null) }
    var confirmCanceledReport by rememberSaveable { mutableStateOf(false) }
    val formScope = rememberCoroutineScope()
    val aircraftAvailable = aircraftTypes.any { it.id.equals(aircraftId, ignoreCase = true) }
    val arrivalChanged = Json.encodeToString(input()) != arrivalBaseline
    val departureChanged = atd != departureBaselineAtd || departureRemarks.trim() != departureBaselineRemarks.trim() || signature.isNotBlank()
    val otherEmployeeDeparture = departure?.takeIf {
        pendingDeparture == null && !it.submittedByEmployee.staffMemberId.equals(flight.currentStaffMemberId, ignoreCase = true)
    }
    val departureNeedsReview = pendingDeparture != null && pendingDeparture.status != "pending" && reviewedDepartureChangeId != pendingDeparture.changeId
    val departureEnabled = flight.departureEditable() && !departureNeedsReview && !saving
    val departureTime = arrivalInputTime(atd, originalAtd, entryZone)
    val signatureRequired = departure == null && pendingDeparture?.signatureFile == null && signature.isBlank()
    val requiresReview = pending != null && pending.status != "pending" && reviewedChangeId != pending.changeId
    val readOnlyArrival = arrival?.takeIf { !flight.arrivalEditable() }
    val otherEmployeeArrival = arrival?.takeIf {
        pending == null && !it.submittedByEmployee.staffMemberId.equals(flight.currentStaffMemberId, ignoreCase = true)
    }
    val arrivalEnabled = flight.arrivalEditable() && !requiresReview && !saving
    val errors = if (validationAttempted && arrivalEnabled && (arrivalChanged || reviewedChangeId != null)) input().validationErrors(arrivalVersion) else emptySet()
    // Refresh untouched arrival fields while retaining edits in every other section.
    LaunchedEffect(arrival, flight.rowVersion, pending?.changeId, pending?.inputJson, pending?.rowVersion) {
        if (pendingInput != null && reviewedChangeId != pending?.changeId) {
            queuedBaseline = pendingInput.withoutAcknowledgement().let { Json.encodeToString(it) }
            arrivalVersion = pending?.rowVersion.orEmpty()
            if (!arrivalChanged) {
                aircraftId = pendingInput.aircraftTypeId
                originalAta = pendingInput.ata
                ata = operationLocalTime(pendingInput.ata, entryZone)
                tailNumber = pendingInput.aircraftTailNumber
                reference = pendingInput.customerWorkReferenceNumber.orEmpty()
                arrivalRemarks = pendingInput.remarks.orEmpty()
                arrivalBaseline = Json.encodeToString(input())
            }
        } else if (pending == null && queuedBaseline != null) {
            val acknowledged = arrival?.input()?.normalized()?.let { value ->
                value.copy(ata = OffsetDateTime.parse(value.ata).toInstant().toString())
            }
            if (acknowledged != null && Json.encodeToString(acknowledged) == queuedBaseline) {
                // Our queued save was acknowledged while this form was open. Keep any
                // newer edits and advance only to that acknowledged input/version.
                if (!arrivalChanged) {
                    arrivalVersion = flight.rowVersion
                    arrivalReviewedSnapshot = Json.encodeToString(flight.arrivalSubmissions)
                }
                arrivalBaseline = checkNotNull(queuedBaseline)
                queuedBaseline = null
            }
        } else if (!arrivalChanged && pending == null) {
            if (arrival != null) {
                aircraftId = arrival.aircraftTypeId
                originalAta = arrival.ata
                ata = operationLocalTime(arrival.ata, entryZone)
                tailNumber = arrival.aircraftTailNumber
                reference = arrival.customerWorkReferenceNumber.orEmpty()
                arrivalRemarks = arrival.remarks.orEmpty()
                arrivalBaseline = Json.encodeToString(input())
            }
            arrivalVersion = flight.rowVersion
            arrivalReviewedSnapshot = Json.encodeToString(flight.arrivalSubmissions)
        }
    }
    LaunchedEffect(departure, flight.rowVersion, pendingDeparture?.changeId, pendingDeparture?.inputJson, pendingDeparture?.rowVersion) {
        if (pendingDepartureInput != null && reviewedDepartureChangeId != pendingDeparture?.changeId) {
            queuedDepartureBaseline = pendingDeparture?.inputJson
            departureVersion = pendingDeparture?.rowVersion.orEmpty()
            departureReviewedSnapshot = pendingDeparture?.reviewedDepartureJson ?: departureReviewedSnapshot
            if (!departureChanged) {
                atd = operationLocalTime(pendingDepartureInput.atd, entryZone)
                originalAtd = pendingDepartureInput.atd
                departureRemarks = pendingDepartureInput.remarks.orEmpty()
                departureBaselineAtd = atd
                departureBaselineRemarks = departureRemarks
            }
        } else if (pendingDeparture == null && queuedDepartureBaseline != null) {
            val queued = runCatching { Json.decodeFromString<SaveFlightDepartureRequest>(checkNotNull(queuedDepartureBaseline)) }.getOrNull()
            if (departure != null && queued != null &&
                OffsetDateTime.parse(departure.atd).toInstant() == OffsetDateTime.parse(queued.atd).toInstant() &&
                departure.remarks.orEmpty().trim() == queued.remarks.orEmpty().trim()) {
                departureBaselineAtd = operationLocalTime(queued.atd, entryZone)
                departureBaselineRemarks = queued.remarks.orEmpty()
                queuedDepartureBaseline = null
                // A new edit keeps its reviewed snapshot; the queue owns acknowledged version advancement.
                if (!departureChanged) {
                    departureVersion = flight.rowVersion
                    departureReviewedSnapshot = Json.encodeToString(flight.departureSubmissions)
                }
            }
        } else if (!departureChanged && pendingDeparture == null) {
            atd = departure?.let { operationLocalTime(it.atd, entryZone) }.orEmpty()
            originalAtd = departure?.atd
            departureRemarks = departure?.remarks.orEmpty()
            departureBaselineAtd = atd
            departureBaselineRemarks = departureRemarks
            departureVersion = flight.rowVersion
            departureReviewedSnapshot = Json.encodeToString(flight.departureSubmissions)
        }
    }
    // Untouched editors follow refreshed data; a dirty editor keeps its reviewed records and rowversion.
    LaunchedEffect(flight.services, flight.rowVersion, flight.allowedServiceIds, flight.currentStaffMemberId, flight.assignedEmployees, catalogs.services, catalogs.stationStaff,
        pendingServices, serviceFormDraft?.formJson) {
        if (!serviceRowsChanged && reviewedServiceChanges.isEmpty()) {
            val next = initialServiceEditorDraft(flight, catalogs, pendingServices, serviceFormDraft?.formJson, entryZone)
            fun media(nextItems: List<FlightAttachmentDraft>, oldItems: List<FlightAttachmentDraft>) = nextItems.map { attachment ->
                oldItems.firstOrNull { it.id == attachment.id && it.uploadedAtUtc == attachment.uploadedAtUtc }?.let {
                    attachment.copy(uri = it.uri, ownedCapture = it.ownedCapture)
                } ?: attachment
            }
            val services = next.services.map { value ->
                val old = workDrafts.services.firstOrNull { it.id == value.id } ?: value.plannedServiceId?.let { plannedId ->
                    workDrafts.services.firstOrNull { it.plannedServiceId.equals(plannedId, true) && it.period.start.isBlank() }
                }
                value.copy(id = old?.id ?: value.id, attachments = media(value.attachments, old?.attachments.orEmpty()),
                    employees = value.employees.map { employee -> employee.copy(id = old?.employees
                        ?.firstOrNull { it.employeeId.equals(employee.employeeId, true) }?.id ?: employee.id) })
            }
            val tasks = if (tasksChanged) workDrafts.tasks else next.tasks.map { value -> value.copy(attachments = media(value.attachments,
                workDrafts.tasks.firstOrNull { it.id == value.id }?.attachments.orEmpty())) }
            workDrafts = FlightWorkDrafts(services, tasks)
            suppressedServiceIdsJson = Json.encodeToString(next.suppressedPlannedServiceIds)
            // Task-only edits must survive staff/catalog refresh and remain visibly unsaved.
            serviceBaseline = serviceEditorFingerprint(services, if (tasksChanged) serviceBaselineDraft.tasks else tasks,
                next.suppressedPlannedServiceIds)
            serviceBaselineInputs = Json.encodeToString(services.associate { it.id to serviceInputFingerprint(it.serviceInput(entryZone)) })
            knownServiceIdsJson = Json.encodeToString((flight.services.map { it.id } + pendingServices.map { it.serviceRecordId }).distinct())
            serviceReviewedJson = Json.encodeToString(flight.services)
            serviceVersion = flight.rowVersion
        }
    }
    val storedServiceMedia = buildSet {
        pendingServices.forEach { row ->
            runCatching { Json.decodeFromString<Map<String, String>>(row.attachmentFilesJson) }.getOrDefault(emptyMap()).keys.forEach { add("${row.serviceRecordId}/$it") }
        }
        addAll(runCatching { Json.decodeFromString<Map<String, String>>(serviceFormDraft?.attachmentFilesJson ?: "{}") }.getOrDefault(emptyMap()).keys)
    }
    val serviceMediaKeys = workDrafts.services.flatMap { service -> service.attachments.map { Triple(service.id, it.id, it.uploadedAtUtc) } } +
        workDrafts.tasks.flatMap { task -> task.attachments.map { Triple(task.id, it.id, it.uploadedAtUtc) } }
    LaunchedEffect(serviceMediaKeys, serviceFormDraft?.savedAtUtc, pendingServices.map { it.attachmentFilesJson }) {
        for ((recordId, attachmentId, uploadedAt) in serviceMediaKeys) {
            val attachment = (workDrafts.services.firstOrNull { it.id == recordId }?.attachments ?:
                workDrafts.tasks.firstOrNull { it.id == recordId }?.attachments).orEmpty().firstOrNull { it.id == attachmentId } ?: continue
            val stored = "$recordId/$attachmentId" in storedServiceMedia
            if (!stored && (uploadedAt == null || attachment.uri.isNotBlank())) continue
            try {
                val uri = if (stored) loadPendingServiceAttachment(flight.id, recordId, attachmentId)
                    else loadServiceAttachment(flight.id, recordId, attachmentId, checkNotNull(uploadedAt))
                if (uri != null) {
                    fun update(items: List<FlightAttachmentDraft>) = items.map { if (it.id == attachmentId) it.copy(uri = uri, ownedCapture = false) else it }
                    workDrafts = workDrafts.copy(
                        services = workDrafts.services.map { if (it.id == recordId) it.copy(attachments = update(it.attachments)) else it },
                        tasks = workDrafts.tasks.map { if (it.id == recordId) it.copy(attachments = update(it.attachments)) else it })
                }
            } catch (cancelled: CancellationException) { throw cancelled }
            catch (_: Exception) { /* Retained media stays saved when a preview is temporarily unavailable. */ }
        }
    }
    val knownServiceIds = Json.decodeFromString<List<String>>(knownServiceIdsJson).toSet()
    val baselineServices = Json.decodeFromString<Map<String, String>>(serviceBaselineInputs)
    val forcedServiceIds = pendingServices.filter { it.status != "pending" && it.changeId in reviewedServiceChanges }.map { it.serviceRecordId }.toSet()
    val serviceChanges = serviceEditorChanges(workDrafts.services, knownServiceIds, baselineServices, pendingServices,
        forcedServiceIds, storedServiceMedia, entryZone)
    val servicesEnabled = flight.canSaveServices && !servicesNeedReview && !flight.hasCancellationSubmission && !saving
    val actualArrivalForServices = if (arrivalEnabled && arrivalChanged) input().ata else pendingInput?.ata ?: arrival?.ata
    val actualDepartureForServices = if (departureEnabled && departureChanged) departureTime else pendingDepartureInput?.atd ?: departure?.atd
    val departureSaveRequested = departureEnabled && (departureChanged || reviewedDepartureChangeId != null)
    val changedServiceSaves = serviceChanges.filter { it.input != null }.map { it.serviceRecordId }.toSet()
    val serviceCompletionRequired = departureSaveRequested || actualDepartureForServices?.isNotBlank() == true && changedServiceSaves.isNotEmpty()
    val serviceCompletionErrors = if (serviceCompletionRequired) departureServiceCompletionValidation(workDrafts.services,
        flight.services, pendingServices, serviceChanges, entryZone, if (departureSaveRequested) null else changedServiceSaves) else emptyMap()
    val parentTimesChanged = arrivalEnabled && arrivalChanged || departureEnabled && departureChanged
    val parentBoundsErrors = if (parentTimesChanged) serviceActualBoundsValidation(workDrafts.services, entryZone,
        actualArrivalForServices, actualDepartureForServices) else emptyMap()
    val changedServiceErrors = if (validationAttempted) serviceChanges.mapNotNull { change -> change.input?.let { value ->
        workDrafts.services.firstOrNull { it.id == change.serviceRecordId }?.let { draft ->
            draft.id to serviceEditorValidation(draft, value, flight, catalogs, serviceVersion, actualArrivalForServices, actualDepartureForServices)
        }
    } }.toMap() else emptyMap()
    val serviceErrors = if (validationAttempted) (changedServiceErrors.keys + parentBoundsErrors.keys + serviceCompletionErrors.keys).associateWith { id ->
        changedServiceErrors[id].orEmpty() + parentBoundsErrors[id].orEmpty() + serviceCompletionErrors[id].orEmpty()
    } else emptyMap()
    val actualBoundsMessage = stringResource(R.string.actuals_service_bounds)
    val finishRecordingMessage = stringResource(R.string.service_finish_recording)
    val serviceReviewMessage = stringResource(R.string.service_sync_review)
    val serviceCompletionMessage = stringResource(if (departureSaveRequested) R.string.departure_services_incomplete else R.string.service_departed_periods_required)
    val mediaSession = rememberFlightMediaSession()
    fun updateWorkDrafts(next: FlightWorkDrafts) {
        val retained = (next.services.flatMap { it.attachments } + next.tasks.flatMap { it.attachments }).map { it.uri }.toSet()
        (workDrafts.services.flatMap { it.attachments } + workDrafts.tasks.flatMap { it.attachments })
            .filter { it.ownedCapture && it.uri !in retained }.forEach { mediaSession.delete(it.uri) }
        val removed = workDrafts.services.filter { old -> next.services.none { it.id == old.id } }
        val suppressed = suppressedServiceIds + removed.mapNotNull { it.plannedServiceId ?: it.serviceId.takeIf { id -> flight.plannedServices.any { planned -> planned.serviceId.equals(id, true) } } }
        suppressedServiceIdsJson = Json.encodeToString(suppressed.distinct())
        workDrafts = next
    }
    val closeForm = { mediaSession.clear(); onClose() }
    val saveChanges: (Boolean) -> Unit = { acknowledgeConflict ->
        validationAttempted = true
        val arrivalValue = input()
        val saveArrivalInput = arrivalEnabled && (arrivalChanged || reviewedChangeId != null)
        val saveDepartureInput = departureEnabled && (departureChanged || reviewedDepartureChangeId != null)
        val saveServiceInput = servicesChanged || forcedServiceIds.isNotEmpty()
        val arrivalValid = !saveArrivalInput || aircraftAvailable && arrivalValue.validationErrors(arrivalVersion).isEmpty() && parentBoundsErrors.isEmpty()
        val departureValid = !saveDepartureInput || departureTime.isNotBlank() && !signatureRequired && !departureNeedsReview &&
            parentBoundsErrors.isEmpty() && serviceCompletionErrors.isEmpty()
        val checkedServices = serviceChanges.mapNotNull { change -> change.input?.let { value ->
            workDrafts.services.firstOrNull { it.id == change.serviceRecordId }?.let { draft ->
                serviceEditorValidation(draft, value, flight, catalogs, serviceVersion, actualArrivalForServices, actualDepartureForServices)
            }
        } }
        val servicesValid = !saveServiceInput || servicesEnabled && checkedServices.all { it.isEmpty() } && serviceCompletionErrors.isEmpty()
        val anyInput = saveArrivalInput || saveDepartureInput || saveServiceInput
        if (mediaSession.hasActiveRecording) {
            saveError = finishRecordingMessage
        } else if (parentBoundsErrors.isNotEmpty()) {
            saveError = actualBoundsMessage
        } else if (serviceCompletionErrors.isNotEmpty()) {
            saveError = serviceCompletionMessage
        } else if (saveServiceInput && !servicesValid && checkedServices.all { it.isEmpty() }) {
            saveError = serviceReviewMessage
        } else if (anyInput && arrivalValid && departureValid && servicesValid &&
            (saveArrivalInput || saveDepartureInput) && flight.cancellationSubmissions.any {
                !it.submittedByEmployee.staffMemberId.equals(flight.currentStaffMemberId, true)
            } && !acknowledgeConflict) {
            confirmCanceledReport = true
        } else if (anyInput && arrivalValid && departureValid && servicesValid) {
            saving = true; saveError = null
            val workSnapshot = workDrafts
            val workFingerprint = serviceEditorFingerprint(workSnapshot.services, workSnapshot.tasks, suppressedServiceIds)
            val ink = signature
            val signedAt = if (signature.isNotBlank()) signatureSignedAtUtc else null
            val departureValue = SaveFlightDepartureRequest(departureTime, signatureSignedAtUtc = signedAt,
                remarks = departureRemarks).normalized()
            val savedSuppressed = (suppressedServiceIds + workDrafts.services.filter { it.period.start.isNotBlank() }
                .mapNotNull { it.plannedServiceId }).distinct()
            val localRows = workDrafts.services.filter { it.id !in knownServiceIds && it.period.start.isBlank() }
            val draftValue = FlightServiceEditorDraft(entryZone.id, localRows, workDrafts.tasks, savedSuppressed)
            val localAttachments = localRows.flatMap { row -> row.attachments.map { row.id to it } } +
                workDrafts.tasks.flatMap { row -> row.attachments.map { row.id to it } }
            val durableDraft = FlightServiceFormDraft(Json.encodeToString(draftValue), localAttachments.map { (recordId, attachment) ->
                FlightServiceFormDraftAttachment(recordId, attachment.serviceAttachmentInput("$recordId/${attachment.id}" in storedServiceMedia))
            })
            formScope.launch {
                try {
                    // The worker cannot drain a partially enqueued form between these durable local writes.
                    onSaveBatch {
                        suspend fun saveArrival() {
                            onSaveArrival(flight.copy(rowVersion = arrivalVersion,
                                arrivalSubmissions = Json.decodeFromString<List<FlightArrivalSubmission>>(arrivalReviewedSnapshot)),
                                arrivalValue.copy(acknowledgeConflictingSubmission = acknowledgeConflict))
                            arrivalBaseline = Json.encodeToString(arrivalValue)
                            queuedBaseline = Json.encodeToString(arrivalValue)
                            reviewedChangeId = null
                        }
                        suspend fun saveDeparture() {
                            val content = if (ink.isNotBlank()) withContext(Dispatchers.Default) { signaturePngContent(ink) } else null
                            onSaveDeparture(flight.copy(rowVersion = departureVersion,
                                departureSubmissions = Json.decodeFromString(departureReviewedSnapshot)),
                                departureValue.copy(signatureContent = content, acknowledgeConflictingSubmission = acknowledgeConflict))
                        }
                        val previousArrival = Json.decodeFromString<List<FlightArrivalSubmission>>(arrivalReviewedSnapshot)
                            .firstOrNull { it.submittedByEmployee.staffMemberId.equals(flight.currentStaffMemberId, true) }?.ata
                        val previousDeparture = flight.copy(departureSubmissions = Json.decodeFromString(departureReviewedSnapshot)).visibleDeparture()?.atd
                        val relaxArrival = parentTimeRelaxesBounds(previousArrival, arrivalValue.ata, arrival = true)
                        val relaxDeparture = parentTimeRelaxesBounds(previousDeparture, departureValue.atd, arrival = false)
                        if (saveArrivalInput && relaxArrival) saveArrival()
                        if (saveDepartureInput && relaxDeparture) saveDeparture()
                        if (saveServiceInput) {
                            onSaveServices(flight.copy(rowVersion = serviceVersion, services = serviceReviewed), serviceChanges, durableDraft)
                            suppressedServiceIdsJson = Json.encodeToString(savedSuppressed)
                            serviceBaseline = serviceEditorFingerprint(workSnapshot.services, workSnapshot.tasks, savedSuppressed)
                            serviceBaselineInputs = Json.encodeToString(workSnapshot.services.associate { it.id to serviceInputFingerprint(it.serviceInput(entryZone)) })
                            knownServiceIdsJson = Json.encodeToString((knownServiceIds + serviceChanges.filter { it.input != null }.map { it.serviceRecordId } -
                                serviceChanges.filter { it.input == null }.map { it.serviceRecordId }.toSet()).toList())
                            reviewedServiceChangesJson = "[]"
                        }
                        if (saveArrivalInput && !relaxArrival) saveArrival()
                        if (saveDepartureInput && !relaxDeparture) saveDeparture()
                    }
                    // Task editors remain local. A later edit cannot be claimed by an earlier disk-save snapshot.
                    if (serviceEditorFingerprint(workDrafts.services, workDrafts.tasks, suppressedServiceIds) == workFingerprint) closeForm()
                } catch (cancelled: CancellationException) { throw cancelled }
                catch (error: Exception) {
                    saveError = error.userMessage()
                }
                finally { saving = false }
            }
        }
    }
    var picker by rememberSaveable { mutableStateOf<String?>(null) }
    LaunchedEffect(arrivalEnabled) {
        if (!arrivalEnabled && picker in listOf("ata", "aircraft")) picker = null
    }
    LaunchedEffect(departureEnabled) {
        if (!departureEnabled && picker in listOf("atd", "signature")) picker = null
    }
    var confirmDiscard by rememberSaveable { mutableStateOf(false) }
    val hasChanges = arrivalChanged || departureChanged || servicesChanged
    val requestClose = {
        if (saving) Unit else if (picker != null) picker = null else if (hasChanges) confirmDiscard = true else closeForm()
    }
    val focus = LocalFocusManager.current
    val keyboard = LocalSoftwareKeyboardController.current
    val unavailable = stringResource(R.string.flights_card_aircraft_unavailable)
    val scheduled = stringResource(R.string.flights_scheduled)
    val aircraftChoices = remember(aircraftTypes, initialAircraftId, initialAircraftModel, unavailable, scheduled) {
        val choices = aircraftTypes.map { ArrivalChoice(it.id, it.model, it.manufacturer) }.toMutableList()
        if (initialAircraftId.isNotBlank() && choices.none { it.id == initialAircraftId }) {
            choices += ArrivalChoice(initialAircraftId, initialAircraftModel.ifBlank { unavailable }, scheduled)
        }
        choices.sortedBy { it.title }
    }
    val selectedAircraft = aircraftChoices.firstOrNull { it.id == aircraftId }?.title
        ?: arrival?.takeIf { it.aircraftTypeId == aircraftId }?.aircraftTypeModel.orEmpty()
    fun openPicker(kind: String) {
        focus.clearFocus(); keyboard?.hide()
        if (kind == "ata" && ata.isBlank()) ata = LocalDateTime.now(zone).withSecond(0).withNano(0).toString()
        if (kind == "atd" && atd.isBlank()) {
            atd = operationLocalTime(flight.scheduledDepartureUtc, entryZone)
            originalAtd = flight.scheduledDepartureUtc
        }
        picker = kind
    }
    FlightRecordingFormScaffold(flight, stringResource(R.string.operations_title), stringResource(R.string.operations_save),
        loadCustomerLogo, requestClose,
        saveEnabled = !saving && (arrivalEnabled && (arrivalChanged || reviewedChangeId != null) ||
            departureEnabled && (departureChanged || reviewedDepartureChangeId != null) ||
            servicesEnabled && (servicesChanged || forcedServiceIds.isNotEmpty())),
        saveLoading = saving, onSave = { saveChanges(false) }, progressStatus = progressStatus) {
        saveError?.let { ArrivalFormMessage(it, error = true) }
        if (otherEmployeeArrival != null) {
            FlightSavedDetailsCard {
                FlightArrivalSummary(otherEmployeeArrival, entryZone)
            }
        } else {
            FlightFormSectionTitle(stringResource(R.string.operations_arrival))
            if (pending != null && pending.status != "pending" && reviewedChangeId != pending.changeId) {
                ArrivalFormMessage(stringResource(R.string.arrival_sync_review), error = true)
                pending.error?.takeIf { pending.status != "pending" }?.let { ArrivalFormMessage(it, error = true) }
                if (pending.status != "pending") TextButton(enabled = !saving, onClick = {
                    saving = true; saveError = null
                    formScope.launch {
                        try {
                            val latest = onReviewArrival()
                            arrivalVersion = latest.rowVersion
                            arrivalReviewedSnapshot = Json.encodeToString(latest.arrivalSubmissions)
                            reviewedChangeId = pending.changeId
                        } catch (cancelled: CancellationException) { throw cancelled }
                        catch (error: Exception) { saveError = error.userMessage() }
                        finally { saving = false }
                    }
                }) { Text(stringResource(R.string.arrival_reload_review)) }
            } else if (pending != null && pending.status != "pending") ArrivalFormMessage(stringResource(R.string.arrival_review_before_save))
            Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
                FlightFormFieldGroup {
                    FlightFormDateTimeField(readOnlyArrival?.let { operationLocalTime(it.ata, entryZone) } ?: ata, stringResource(R.string.arrival_ata_short), stringResource(R.string.arrival_choose_ata),
                        enabled = arrivalEnabled,
                        errorMessage = if (ArrivalInputError.AtaRequired in errors) stringResource(R.string.arrival_ata_required) else null,
                        onClick = { openPicker("ata") })
                }
            }
            if (ArrivalInputError.VersionRequired in errors) ArrivalFormMessage(stringResource(R.string.arrival_reload_required), error = true)
            FlightFormFieldGroup {
                FlightFormChoiceRow(stringResource(R.string.arrival_aircraft), (readOnlyArrival?.aircraftTypeModel ?: selectedAircraft).ifBlank { stringResource(R.string.arrival_select_aircraft) },
                    stringResource(R.string.arrival_aircraft_type), isPlaceholder = (readOnlyArrival?.aircraftTypeModel ?: selectedAircraft).isBlank(), enabled = arrivalEnabled,
                    errorMessage = when {
                        ArrivalInputError.AircraftRequired in errors -> stringResource(R.string.arrival_aircraft_required)
                        validationAttempted && arrivalEnabled && (arrivalChanged || reviewedChangeId != null) && !aircraftAvailable -> stringResource(R.string.arrival_aircraft_unavailable)
                        else -> null
                    }, onClick = { openPicker("aircraft") })
                FlightFormDivider()
                FlightFormTextRow(stringResource(R.string.arrival_tail_number), readOnlyArrival?.aircraftTailNumber ?: tailNumber, { tailNumber = it },
                    stringResource(R.string.arrival_tail_hint), keyboardOptions = KeyboardOptions(
                        capitalization = KeyboardCapitalization.Characters, imeAction = ImeAction.Next), enabled = arrivalEnabled,
                    errorMessage = if (ArrivalInputError.TailRequired in errors) stringResource(R.string.arrival_tail_required) else null)
            }
            FlightFormFieldGroup {
                FlightFormTextRow(stringResource(R.string.arrival_reference), if (readOnlyArrival != null) readOnlyArrival.customerWorkReferenceNumber.orEmpty() else reference, { reference = it }, stringResource(R.string.arrival_optional), enabled = arrivalEnabled)
            }
            FlightFormRemarks(if (readOnlyArrival != null) readOnlyArrival.remarks.orEmpty() else arrivalRemarks, { arrivalRemarks = it }, stringResource(R.string.operations_arrival_remarks), enabled = arrivalEnabled)
        }
        CompositionLocalProvider(LocalFlightMediaSession provides mediaSession) {
            if (servicesNeedReview) {
                ArrivalFormMessage(stringResource(R.string.service_sync_review), error = true)
                pendingServices.filter { it.status != "pending" }.mapNotNull { it.error }.distinct().forEach { ArrivalFormMessage(it, error = true) }
                TextButton(enabled = !saving, onClick = {
                    saving = true; saveError = null
                    formScope.launch {
                        try {
                            val latest = onReviewServices()
                            serviceReviewedJson = Json.encodeToString(latest.services)
                            serviceVersion = latest.rowVersion
                            reviewedServiceChangesJson = Json.encodeToString(pendingServices.filter { it.status != "pending" }.map { it.changeId })
                        } catch (cancelled: CancellationException) { throw cancelled }
                        catch (error: Exception) { saveError = error.userMessage() }
                        finally { saving = false }
                    }
                }) { Text(stringResource(R.string.service_review)) }
            }
            if (serviceErrors.values.any { "rowVersion" in it }) ArrivalFormMessage(stringResource(R.string.service_validation_version), error = true)
            FlightServiceSection(workDrafts.services, catalogs, referenceYear, zone,
                enabled = servicesEnabled,
                canEdit = { id -> serviceReviewed.firstOrNull { it.id == id }?.canEdit ?: true },
                canSetEnd = { id -> serviceReviewed.firstOrNull { it.id == id }?.canSetEnd ?: true },
                canDelete = { id -> serviceReviewed.firstOrNull { it.id == id }?.let { it.canEdit && it.canSetEnd } ?: true },
                errors = serviceErrors, defaultEmployee = defaultServiceEmployee(flight, catalogs),
                timeBounds = flightWorkTimeBounds(actualArrivalForServices, actualDepartureForServices),
                onChange = { change -> updateWorkDrafts(workDrafts.copy(services = change(workDrafts.services))) })
            FlightTaskSection(workDrafts.tasks, catalogs, referenceYear, zone,
                onChange = { change -> updateWorkDrafts(workDrafts.copy(tasks = change(workDrafts.tasks))) })
        }
        if (otherEmployeeDeparture != null) {
            FlightSavedDetailsCard {
                FlightDepartureSummary(otherEmployeeDeparture, entryZone, savedSignaturePreview)
            }
        } else {
            FlightFormSectionTitle(stringResource(R.string.operations_departure))
            if (pendingDeparture != null && pendingDeparture.status != "pending") {
                ArrivalFormMessage(stringResource(if (departureNeedsReview) R.string.departure_sync_review else R.string.departure_review_before_save), error = true)
                pendingDeparture.error?.let { ArrivalFormMessage(it, error = true) }
            }
            if (departureNeedsReview) {
                TextButton(enabled = !saving, onClick = {
                    saving = true
                    formScope.launch {
                        try {
                            val latest = onReviewArrival()
                            departureVersion = latest.rowVersion
                            departureReviewedSnapshot = Json.encodeToString(latest.departureSubmissions)
                            reviewedDepartureChangeId = pendingDeparture.changeId
                            saveError = null
                        } catch (cancelled: CancellationException) { throw cancelled }
                        catch (error: Exception) { saveError = error.userMessage() }
                        finally { saving = false }
                    }
                }) { Text(stringResource(R.string.departure_reload_review)) }
            }
            FlightFormFieldGroup {
                FlightFormDateTimeField(atd, stringResource(R.string.operations_atd_short), stringResource(R.string.operations_choose_atd),
                    enabled = departureEnabled,
                    errorMessage = if (validationAttempted && departureChanged && departureTime.isBlank()) stringResource(R.string.departure_atd_required) else null,
                    onClear = if (atd.isNotBlank()) ({ atd = ""; originalAtd = null }) else null,
                    clearDescription = stringResource(R.string.operations_clear_atd),
                    onClick = { openPicker("atd") })
            }
            FlightFormFieldGroup {
                FlightSignatureField(signature, hasSavedSignature = hasSavedSignature, savedPreview = savedSignaturePreview,
                    loading = signaturePreviewLoading, enabled = departureEnabled, onOpen = { openPicker("signature") })
            }
            if (validationAttempted && departureChanged && signatureRequired) ArrivalFormMessage(stringResource(R.string.departure_signature_required), error = true)
            FlightFormRemarks(departureRemarks, { departureRemarks = it }, stringResource(R.string.operations_departure_remarks), enabled = departureEnabled)
        }
    }
    if (picker == "ata" || picker == "atd") {
        val isArrival = picker == "ata"
        FlightFormDateTimeSheet(LocalDateTime.parse(if (isArrival) ata else atd), referenceYear,
            onValueChange = { if (isArrival) ata = it.toString() else atd = it.toString() }, onDismiss = { picker = null },
            title = stringResource(if (isArrival) R.string.arrival_ata else R.string.operations_atd), showTitle = false)
    }
    if (picker == "aircraft") ArrivalAircraftPicker(title = stringResource(R.string.arrival_aircraft_type),
        options = aircraftChoices, selectedId = aircraftId, onDismiss = { picker = null }, onSelect = { aircraftId = it })
    if (picker == "signature") FlightSignatureSheet(signature, hasSavedSignature, savedSignaturePreview,
        signaturePreviewLoading, onRetry = { signaturePreviewRetry++ }, onValueChange = {
        signature = it
        signatureSignedAtUtc = if (it.isBlank()) null else Instant.now().toString()
    }, onDismiss = { picker = null })
    if (confirmCanceledReport) FlightSubmissionConfirmation(
        stringResource(R.string.submission_canceled_warning_title), stringResource(R.string.submission_canceled_warning),
        stringResource(R.string.submission_save_anyway), onConfirm = { confirmCanceledReport = false; saveChanges(true) },
        onDismiss = { confirmCanceledReport = false })
    if (confirmDiscard) FlightFormDiscardDialog(stringResource(R.string.operations_discard_title),
        onDismiss = { confirmDiscard = false }, onDiscard = closeForm)
}

/** Saved details retain the identity card's surface, insets, and physical column order. */
@Composable
private fun FlightSavedDetailsCard(content: @Composable () -> Unit) {
    CompositionLocalProvider(androidx.compose.ui.platform.LocalLayoutDirection provides LayoutDirection.Ltr) {
        Surface(Modifier.fillMaxWidth(), color = Color.White, shape = RoundedCornerShape(28.dp)) {
            Column(Modifier.padding(16.dp)) { content() }
        }
    }
}

@Composable
private fun ArrivalFormMessage(message: String, error: Boolean = false) {
    Text(message, Modifier.padding(horizontal = 14.dp), style = MaterialTheme.typography.bodySmall,
        color = if (error) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.onSurfaceVariant)
}

private data class ArrivalChoice(val id: String, val title: String, val subtitle: String)

@Composable
private fun ArrivalAircraftPicker(title: String, options: List<ArrivalChoice>, selectedId: String,
    onDismiss: () -> Unit, onSelect: (String) -> Unit) {
    var search by rememberSaveable { mutableStateOf("") }
    val focus = LocalFocusManager.current
    val keyboard = LocalSoftwareKeyboardController.current
    val searchDescription = stringResource(R.string.arrival_search_aircraft)
    val filtered = remember(options, search) {
        options.filter { it.title.contains(search.trim(), ignoreCase = true) || it.subtitle.contains(search.trim(), ignoreCase = true) }
    }
    FlightFormModalSheet(title, onDismiss) { close ->
        Column(Modifier.fillMaxWidth().heightIn(max = 600.dp).fillMaxHeight(0.85f)) {
            Row(Modifier.fillMaxWidth().padding(horizontal = 8.dp), verticalAlignment = Alignment.CenterVertically) {
                Spacer(Modifier.size(48.dp))
                Text(title, Modifier.weight(1f), style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Bold,
                    textAlign = TextAlign.Center, maxLines = 2, overflow = TextOverflow.Ellipsis)
                IconButton(onClick = close, modifier = Modifier.size(48.dp)) {
                    Box(Modifier.size(30.dp).background(FlightFormGround, CircleShape), contentAlignment = Alignment.Center) {
                        Icon(Icons.Outlined.Close, stringResource(R.string.flights_cancel), Modifier.size(18.dp))
                    }
                }
            }
            Row(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 6.dp).clip(RoundedCornerShape(12.dp))
                .background(FlightFormGround), verticalAlignment = Alignment.CenterVertically) {
                Icon(Icons.Outlined.Search, null, Modifier.padding(start = 12.dp, end = 8.dp).size(20.dp), tint = MaterialTheme.colorScheme.onSurfaceVariant)
                BasicTextField(search, { search = it }, modifier = Modifier.weight(1f).heightIn(min = 48.dp)
                    .semantics { contentDescription = searchDescription }, singleLine = true,
                    textStyle = MaterialTheme.typography.bodyLarge.copy(color = MaterialTheme.colorScheme.onSurface, fontWeight = FontWeight.Medium),
                    cursorBrush = SolidColor(MaterialTheme.colorScheme.primary), keyboardOptions = KeyboardOptions(imeAction = ImeAction.Search),
                    keyboardActions = KeyboardActions(onSearch = { focus.clearFocus(); keyboard?.hide() }),
                    decorationBox = { inner -> Box(contentAlignment = Alignment.CenterStart) {
                        if (search.isEmpty()) Text(stringResource(R.string.arrival_search_aircraft), style = MaterialTheme.typography.bodyMedium, fontStyle = FontStyle.Italic,
                            color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1, overflow = TextOverflow.Ellipsis)
                        inner()
                    } })
                if (search.isNotEmpty()) IconButton(onClick = { search = "" }, modifier = Modifier.size(48.dp)) {
                    Icon(Icons.Outlined.Close, stringResource(R.string.flights_search_clear), Modifier.size(18.dp))
                } else Spacer(Modifier.size(12.dp))
            }
            if (filtered.isEmpty()) Text(if (options.isEmpty()) stringResource(R.string.arrival_no_aircraft) else stringResource(R.string.arrival_no_results),
                Modifier.padding(20.dp), style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
            else LazyColumn(Modifier.weight(1f).fillMaxWidth()) {
                itemsIndexed(filtered, key = { _, option -> option.id }) { index, option ->
                    val selected = option.id == selectedId
                    Row(Modifier.fillMaxWidth().heightIn(min = 56.dp).selectable(selected = selected, role = Role.RadioButton,
                        onClick = { onSelect(option.id); focus.clearFocus(); keyboard?.hide(); close() })
                        .padding(horizontal = 20.dp, vertical = 10.dp), horizontalArrangement = Arrangement.spacedBy(12.dp), verticalAlignment = Alignment.CenterVertically) {
                        Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                            Text(option.title, style = MaterialTheme.typography.bodyLarge,
                                fontWeight = if (selected) FontWeight.Bold else FontWeight.SemiBold, maxLines = 2, overflow = TextOverflow.Ellipsis)
                            if (option.subtitle.isNotBlank()) Text(option.subtitle, style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1, overflow = TextOverflow.Ellipsis)
                        }
                        if (selected) Icon(Icons.Filled.Check, stringResource(R.string.arrival_aircraft_selected), Modifier.size(20.dp), tint = MaterialTheme.colorScheme.primary)
                        else Spacer(Modifier.size(20.dp))
                    }
                    if (index < filtered.lastIndex) HorizontalDivider(Modifier.padding(start = 20.dp), thickness = 0.5.dp,
                        color = MaterialTheme.colorScheme.outlineVariant.copy(alpha = 0.6f))
                }
            }
        }
    }
}
