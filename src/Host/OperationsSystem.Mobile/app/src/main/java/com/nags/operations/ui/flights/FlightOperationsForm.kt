package com.nags.operations.ui.flights

import androidx.compose.foundation.background
import androidx.compose.animation.animateContentSize
import androidx.compose.animation.core.tween
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
import androidx.compose.material.icons.filled.FlightLand
import androidx.compose.material.icons.filled.FlightTakeoff
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
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.withFrameNanos
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
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
import com.nags.operations.data.FlightOperationReviewTarget
import com.nags.operations.data.AircraftTypeCatalogItem
import com.nags.operations.data.FlightDetail
import com.nags.operations.data.FlightListItem
import com.nags.operations.ui.components.FlightCard
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
import com.nags.operations.data.canUpdateArrivalSubmission
import com.nags.operations.data.canUpdateDepartureSubmission
import com.nags.operations.data.visibleArrival
import com.nags.operations.data.validationErrors
import com.nags.operations.data.userMessage
import com.nags.operations.data.db.entities.FlightArrivalPendingEntity
import com.nags.operations.data.db.entities.FlightDeparturePendingEntity
import com.nags.operations.data.arrivalInputTime
import com.nags.operations.data.parentTimeRelaxesBounds
import com.nags.operations.data.FlightServiceChange
import com.nags.operations.data.FlightServiceRecord
import com.nags.operations.data.FlightTaskRecord
import com.nags.operations.data.FlightTaskChange
import com.nags.operations.data.SaveFlightTaskRequest
import com.nags.operations.data.db.entities.FlightTaskPendingEntity
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
    pendingTasks: List<FlightTaskPendingEntity> = emptyList(),
    onSaveTasks: suspend (FlightDetail, List<FlightTaskChange>) -> Unit = { _, _ -> },
    onReviewTasks: suspend () -> FlightDetail = { flight },
    loadTaskAttachment: suspend (String, String, String, String) -> String? = { _, _, _, _ -> null },
    loadPendingTaskAttachment: suspend (String, String, String) -> String? = { _, _, _ -> null },
    listItem: FlightListItem? = null,
    onSaveReviewed: suspend (FlightDetail, FlightOperationReviewTarget, suspend () -> Unit) -> Unit = { _, _, _ -> error("Review saving is unavailable.") },
    onDiscardReviewed: suspend (FlightOperationReviewTarget) -> Unit = { error("Review discard is unavailable.") },
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
        (pendingInput?.ata ?: arrival?.ata)?.let { operationLocalTime(it, entryZone) }.orEmpty()
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
    val initialTasks = remember { initialTaskEditorDraft(flight, catalogs, pendingTasks, serviceFormDraft?.formJson, entryZone) }
    var workDrafts by rememberSaveable(stateSaver = FlightWorkDraftsSaver) {
        mutableStateOf(FlightWorkDrafts(initialServices.services, initialTasks))
    }
    var suppressedServiceIdsJson by rememberSaveable { mutableStateOf(Json.encodeToString(initialServices.suppressedPlannedServiceIds)) }
    val suppressedServiceIds = Json.decodeFromString<List<String>>(suppressedServiceIdsJson)
    var serviceBaseline by rememberSaveable { mutableStateOf(serviceEditorFingerprint(initialServices.services, emptyList(),
        initialServices.suppressedPlannedServiceIds)) }
    var serviceBaselineInputs by rememberSaveable { mutableStateOf(Json.encodeToString(initialServices.services.associate {
        it.id to serviceInputFingerprint(it.serviceInput(entryZone)) })) }
    var knownServiceIdsJson by rememberSaveable { mutableStateOf(Json.encodeToString((flight.services.map { it.id } + pendingServices.map { it.serviceRecordId }).distinct())) }
    var serviceReviewedJson by rememberSaveable { mutableStateOf(Json.encodeToString(flight.services)) }
    var serviceVersion by rememberSaveable { mutableStateOf(flight.rowVersion) }
    var reviewedServiceChangesJson by rememberSaveable { mutableStateOf("[]") }
    val reviewedServiceChanges = Json.decodeFromString<List<String>>(reviewedServiceChangesJson)
    val serviceBaselineDraft = checkNotNull(decodeServiceEditorDraft(serviceBaseline))
    val serviceRowsChanged = serviceEditorFingerprint(workDrafts.services, emptyList(), suppressedServiceIds) !=
        serviceEditorFingerprint(serviceBaselineDraft.services, emptyList(), serviceBaselineDraft.suppressedPlannedServiceIds)
    var taskBaseline by rememberSaveable { mutableStateOf(taskEditorFingerprint(initialTasks)) }
    var taskBaselineInputs by rememberSaveable { mutableStateOf(Json.encodeToString(initialTasks.associate {
        it.id to taskInputFingerprint(it.taskInput(entryZone)) })) }
    var knownTaskIdsJson by rememberSaveable { mutableStateOf(Json.encodeToString((flight.tasks.map { it.id } + pendingTasks.map { it.taskRecordId }).distinct())) }
    var taskReviewedJson by rememberSaveable { mutableStateOf(Json.encodeToString(flight.tasks)) }
    var taskVersion by rememberSaveable { mutableStateOf(flight.rowVersion) }
    var reviewedTaskChangesJson by rememberSaveable { mutableStateOf("[]") }
    val reviewedTaskChanges = Json.decodeFromString<List<String>>(reviewedTaskChangesJson)
    val tasksChanged = taskEditorFingerprint(workDrafts.tasks) != taskBaseline
    val servicesChanged = serviceRowsChanged
    val tasksNeedReview = pendingTasks.any { it.status != "pending" && it.changeId !in reviewedTaskChanges }
    val taskReviewed = Json.decodeFromString<List<FlightTaskRecord>>(taskReviewedJson)
    var pendingTaskEndReviewJson by rememberSaveable { mutableStateOf<String?>(null) }
    val servicesNeedReview = pendingServices.any { it.status != "pending" && it.changeId !in reviewedServiceChanges }
    val serviceReviewed = Json.decodeFromString<List<FlightServiceRecord>>(serviceReviewedJson)
    var originalAta by rememberSaveable { mutableStateOf(pendingInput?.ata ?: arrival?.ata) }
    var arrivalVersion by rememberSaveable { mutableStateOf(pending?.rowVersion ?: flight.rowVersion) }
    var arrivalReviewedSnapshot by rememberSaveable { mutableStateOf(Json.encodeToString(flight.arrivalSubmissions)) }
    fun input() = SaveFlightArrivalRequest(
        arrivalInputTime(ata, originalAta, entryZone),
        aircraftId, tailNumber, reference, arrivalRemarks).normalized()
    var arrivalBaseline by rememberSaveable { mutableStateOf(Json.encodeToString(input())) }
    var queuedBaseline by rememberSaveable { mutableStateOf(pendingInput?.withoutAcknowledgement()?.copy(targetSubmissionId = null)?.let { Json.encodeToString(it) }) }
    var reviewedChangeId by rememberSaveable { mutableStateOf<String?>(null) }
    var saving by remember { mutableStateOf(false) }
    var updateSection by rememberSaveable { mutableStateOf<String?>(null) }
    var updateSnapshot by rememberSaveable { mutableStateOf<String?>(null) }
    var updateReviewMode by rememberSaveable { mutableStateOf(false) }
    var updateReviewCached by rememberSaveable { mutableStateOf(false) }
    var workUpdateSnapshot by rememberSaveable { mutableStateOf<String?>(null) }
    var validationAttempted by rememberSaveable { mutableStateOf(false) }
    var saveError by rememberSaveable { mutableStateOf<String?>(null) }
    var confirmCanceledReport by rememberSaveable { mutableStateOf(false) }
    val formScope = rememberCoroutineScope()
    val validationState = rememberFlightFormValidationState()
    var validationRequest by remember { mutableIntStateOf(0) }
    LaunchedEffect(validationRequest) {
        if (validationRequest > 0) {
            withFrameNanos { }
            if (!validationState.revealFirstError()) validationState.revealTarget("save-error")
        }
    }
    val aircraftAvailable = aircraftTypes.any { it.id.equals(aircraftId, ignoreCase = true) }
    val arrivalChanged = Json.encodeToString(input()) != arrivalBaseline
    val departureChanged = atd != departureBaselineAtd || departureRemarks.trim() != departureBaselineRemarks.trim() || signature.isNotBlank()
    val otherEmployeeDeparture = departure?.takeIf {
        !it.submittedByEmployee.staffMemberId.equals(flight.currentStaffMemberId, ignoreCase = true)
    }
    val departureNeedsReview = pendingDeparture != null && pendingDeparture.status != "pending" && reviewedDepartureChangeId != pendingDeparture.changeId
    val departureEnabled = flight.departureEditable() && !departureNeedsReview && !saving
    val departureTime = arrivalInputTime(atd, originalAtd, entryZone)
    val signatureRequired = departure == null && pendingDeparture?.signatureFile == null && signature.isBlank()
    val requiresReview = pending != null && pending.status != "pending" && reviewedChangeId != pending.changeId
    val readOnlyArrival = arrival?.takeIf { !flight.arrivalEditable() }
    val otherEmployeeArrival = arrival?.takeIf {
        !it.submittedByEmployee.staffMemberId.equals(flight.currentStaffMemberId, ignoreCase = true)
    }
    val arrivalEnabled = flight.arrivalEditable() && !requiresReview && !saving
    val errors = if (validationAttempted && arrivalEnabled && (arrivalChanged || reviewedChangeId != null)) input().validationErrors(arrivalVersion) else emptySet()
    fun restoreOnlineArrival() {
        aircraftId = arrival?.aircraftTypeId ?: flight.aircraftTypeId.orEmpty()
        originalAta = arrival?.ata
        ata = arrival?.let { operationLocalTime(it.ata, entryZone) }.orEmpty()
        tailNumber = arrival?.aircraftTailNumber.orEmpty()
        reference = arrival?.customerWorkReferenceNumber.orEmpty()
        arrivalRemarks = arrival?.remarks.orEmpty()
        arrivalBaseline = Json.encodeToString(input())
        arrivalVersion = flight.rowVersion
        arrivalReviewedSnapshot = Json.encodeToString(flight.arrivalSubmissions)
        queuedBaseline = null
    }
    fun restoreOnlineDeparture() {
        atd = departure?.let { operationLocalTime(it.atd, entryZone) }.orEmpty()
        originalAtd = departure?.atd
        departureRemarks = departure?.remarks.orEmpty()
        departureBaselineAtd = atd
        departureBaselineRemarks = departureRemarks
        departureVersion = flight.rowVersion
        departureReviewedSnapshot = Json.encodeToString(flight.departureSubmissions)
        signature = ""; signatureSignedAtUtc = null
        queuedDepartureBaseline = null
    }
    // Refresh untouched arrival fields while retaining edits in every other section.
    LaunchedEffect(arrival, flight.rowVersion, pending?.changeId, pending?.inputJson, pending?.rowVersion) {
        if (pendingInput != null && reviewedChangeId != pending?.changeId) {
            queuedBaseline = pendingInput.withoutAcknowledgement().copy(targetSubmissionId = null).let { Json.encodeToString(it) }
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
            } else if (!arrivalChanged) {
                // A safe local discard is not an ACK. Restore only this untouched editor.
                restoreOnlineArrival()
            }
        } else if (!arrivalChanged && pending == null) restoreOnlineArrival()
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
            } else if (!departureChanged) restoreOnlineDeparture()
        } else if (!departureChanged && pendingDeparture == null) restoreOnlineDeparture()
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
            workDrafts = workDrafts.copy(services = services)
            suppressedServiceIdsJson = Json.encodeToString(next.suppressedPlannedServiceIds)
            serviceBaseline = serviceEditorFingerprint(services, emptyList(), next.suppressedPlannedServiceIds)
            serviceBaselineInputs = Json.encodeToString(services.associate { it.id to serviceInputFingerprint(it.serviceInput(entryZone)) })
            knownServiceIdsJson = Json.encodeToString((flight.services.map { it.id } + pendingServices.map { it.serviceRecordId }).distinct())
            serviceReviewedJson = Json.encodeToString(flight.services)
            serviceVersion = flight.rowVersion
        }
    }
    LaunchedEffect(flight.tasks, flight.rowVersion, flight.currentStaffMemberId, catalogs.stationStaff,
        catalogs.ataChapters, catalogs.tools, catalogs.materials, catalogs.generalSupports, pendingTasks, serviceFormDraft?.formJson) {
        if (!tasksChanged && reviewedTaskChanges.isEmpty()) {
            val next = initialTaskEditorDraft(flight, catalogs, pendingTasks, serviceFormDraft?.formJson, entryZone).map { task ->
                val previous = workDrafts.tasks.firstOrNull { it.id == task.id }
                task.copy(attachments = task.attachments.map { attachment ->
                    previous?.attachments?.firstOrNull { it.id == attachment.id && it.uploadedAtUtc == attachment.uploadedAtUtc }?.let {
                        attachment.copy(uri = it.uri, ownedCapture = it.ownedCapture)
                    } ?: attachment
                })
            }
            workDrafts = workDrafts.copy(tasks = next)
            taskBaseline = taskEditorFingerprint(next)
            taskBaselineInputs = Json.encodeToString(next.associate { it.id to taskInputFingerprint(it.taskInput(entryZone)) })
            knownTaskIdsJson = Json.encodeToString((flight.tasks.map { it.id } + pendingTasks.map { it.taskRecordId }).distinct())
            taskReviewedJson = Json.encodeToString(flight.tasks)
            taskVersion = flight.rowVersion
        }
    }
    // Submitted rows are read-only here. Their acknowledgments and metadata may refresh
    // independently while an unrelated new service/task draft remains dirty.
    LaunchedEffect(flight.services, pendingServices, catalogs.services, catalogs.stationStaff, serviceFormDraft?.formJson) {
        val previousKnown = Json.decodeFromString<List<String>>(knownServiceIdsJson).toSet()
        val nextKnown = (flight.services.map { it.id } + pendingServices.map { it.serviceRecordId }).toSet()
        val protected = pendingServices.filter { it.changeId in reviewedServiceChanges }.map { it.serviceRecordId }.toSet()
        val projected = initialServiceEditorDraft(flight, catalogs, pendingServices, serviceFormDraft?.formJson, entryZone).services
        workDrafts = workDrafts.copy(services = reconcileRecordedServices(workDrafts.services, projected, previousKnown, nextKnown, protected))
        val before = checkNotNull(decodeServiceEditorDraft(serviceBaseline))
        serviceBaseline = serviceEditorFingerprint(reconcileRecordedServices(before.services, projected, previousKnown, nextKnown, protected),
            emptyList(), before.suppressedPlannedServiceIds)
        val inputs = Json.decodeFromString<Map<String, String>>(serviceBaselineInputs)
            .filterKeys { it !in previousKnown && it !in nextKnown || it in protected } +
            projected.filter { it.id in nextKnown && it.id !in protected }.associate { it.id to serviceInputFingerprint(it.serviceInput(entryZone)) }
        serviceBaselineInputs = Json.encodeToString(inputs)
        knownServiceIdsJson = Json.encodeToString(nextKnown.toList())
    }
    LaunchedEffect(flight.tasks, pendingTasks, catalogs.stationStaff, catalogs.ataChapters, catalogs.tools,
        catalogs.materials, catalogs.generalSupports, serviceFormDraft?.formJson) {
        val previousKnown = Json.decodeFromString<List<String>>(knownTaskIdsJson).toSet()
        val nextKnown = (flight.tasks.map { it.id } + pendingTasks.map { it.taskRecordId }).toSet()
        val protected = pendingTasks.filter { it.changeId in reviewedTaskChanges }.map { it.taskRecordId }.toSet()
        val projected = initialTaskEditorDraft(flight, catalogs, pendingTasks, serviceFormDraft?.formJson, entryZone)
        workDrafts = workDrafts.copy(tasks = reconcileRecordedTasks(workDrafts.tasks, projected, previousKnown, nextKnown, protected))
        val before = Json.decodeFromString<List<FlightTaskDraft>>(taskBaseline)
        taskBaseline = taskEditorFingerprint(reconcileRecordedTasks(before, projected, previousKnown, nextKnown, protected))
        val inputs = Json.decodeFromString<Map<String, String>>(taskBaselineInputs)
            .filterKeys { it !in previousKnown && it !in nextKnown || it in protected } +
            projected.filter { it.id in nextKnown && it.id !in protected }.associate { it.id to taskInputFingerprint(it.taskInput(entryZone)) }
        taskBaselineInputs = Json.encodeToString(inputs)
        knownTaskIdsJson = Json.encodeToString(nextKnown.toList())
    }
    val storedServiceMedia = buildSet {
        pendingServices.forEach { row ->
            runCatching { Json.decodeFromString<Map<String, String>>(row.attachmentFilesJson) }.getOrDefault(emptyMap()).keys.forEach { add("${row.serviceRecordId}/$it") }
        }
        addAll(runCatching { Json.decodeFromString<Map<String, String>>(serviceFormDraft?.attachmentFilesJson ?: "{}") }.getOrDefault(emptyMap()).keys)
    }
    val serviceMediaKeys = workDrafts.services.flatMap { service -> service.attachments.map { Triple(service.id, it.id, it.uploadedAtUtc) } }
    LaunchedEffect(serviceMediaKeys, serviceFormDraft?.savedAtUtc, pendingServices.map { it.attachmentFilesJson }) {
        for ((recordId, attachmentId, uploadedAt) in serviceMediaKeys) {
            val attachment = workDrafts.services.firstOrNull { it.id == recordId }?.attachments.orEmpty().firstOrNull { it.id == attachmentId } ?: continue
            val stored = "$recordId/$attachmentId" in storedServiceMedia
            if (!stored && (uploadedAt == null || attachment.uri.isNotBlank())) continue
            try {
                val uri = if (stored) loadPendingServiceAttachment(flight.id, recordId, attachmentId)
                    else loadServiceAttachment(flight.id, recordId, attachmentId, checkNotNull(uploadedAt))
                if (uri != null) {
                    fun update(items: List<FlightAttachmentDraft>) = items.map { if (it.id == attachmentId) it.copy(uri = uri, ownedCapture = false) else it }
                    workDrafts = workDrafts.copy(services = workDrafts.services.map { if (it.id == recordId) it.copy(attachments = update(it.attachments)) else it })
                }
            } catch (cancelled: CancellationException) { throw cancelled }
            catch (_: Exception) { /* Retained media stays saved when a preview is temporarily unavailable. */ }
        }
    }
    val storedTaskMedia = buildSet {
        pendingTasks.forEach { row ->
            runCatching { Json.decodeFromString<Map<String, String>>(row.attachmentFilesJson) }.getOrDefault(emptyMap()).keys.forEach { add("${row.taskRecordId}/$it") }
        }
        addAll(runCatching { Json.decodeFromString<Map<String, String>>(serviceFormDraft?.attachmentFilesJson ?: "{}") }.getOrDefault(emptyMap()).keys)
    }
    val taskMediaKeys = workDrafts.tasks.flatMap { task -> task.attachments.map { Triple(task.id, it.id, it.uploadedAtUtc) } }
    LaunchedEffect(taskMediaKeys, serviceFormDraft?.savedAtUtc, pendingTasks.map { it.attachmentFilesJson }) {
        for ((recordId, attachmentId, uploadedAt) in taskMediaKeys) {
            val attachment = workDrafts.tasks.firstOrNull { it.id == recordId }?.attachments.orEmpty().firstOrNull { it.id == attachmentId } ?: continue
            val stored = "$recordId/$attachmentId" in storedTaskMedia
            if (!stored && (uploadedAt == null || attachment.uri.isNotBlank())) continue
            try {
                val uri = if (stored) loadPendingTaskAttachment(flight.id, recordId, attachmentId)
                    else loadTaskAttachment(flight.id, recordId, attachmentId, checkNotNull(uploadedAt))
                if (uri != null) workDrafts = workDrafts.copy(tasks = workDrafts.tasks.map { task ->
                    if (task.id == recordId) task.copy(attachments = task.attachments.map {
                        if (it.id == attachmentId) it.copy(uri = uri, ownedCapture = false) else it
                    }) else task
                })
            } catch (cancelled: CancellationException) { throw cancelled }
            catch (_: Exception) { /* Saved media remains retained while its preview is unavailable. */ }
        }
    }
    val knownTaskIds = Json.decodeFromString<List<String>>(knownTaskIdsJson).toSet()
    val baselineTasks = Json.decodeFromString<Map<String, String>>(taskBaselineInputs)
    val forcedTaskIds = pendingTasks.filter { it.status != "pending" && it.changeId in reviewedTaskChanges }.map { it.taskRecordId }.toSet()
    val taskChanges = taskEditorChanges(workDrafts.tasks, knownTaskIds, baselineTasks, pendingTasks,
        forcedTaskIds, storedTaskMedia, entryZone)
    val tasksEnabled = flight.canSaveTasks && !tasksNeedReview && !flight.hasCancellationSubmission && !saving
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
    val changedTaskSaves = taskChanges.filter { it.input != null }.map { it.taskRecordId }.toSet()
    val taskCompletionRequired = departureSaveRequested || actualDepartureForServices?.isNotBlank() == true && changedTaskSaves.isNotEmpty()
    val taskCompletionErrors = if (taskCompletionRequired) departureTaskCompletionValidation(workDrafts.tasks,
        flight.tasks, pendingTasks, taskChanges, entryZone, if (departureSaveRequested) null else changedTaskSaves) else emptyMap()
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
    val taskParentBoundsErrors = if (parentTimesChanged) taskActualBoundsValidation(workDrafts.tasks, entryZone,
        actualArrivalForServices, actualDepartureForServices) else emptyMap()
    val changedTaskErrors = if (validationAttempted) taskChanges.mapNotNull { change -> change.input?.let { value ->
        workDrafts.tasks.firstOrNull { it.id == change.taskRecordId }?.let { draft ->
            draft.id to taskEditorValidation(draft, value, flight, catalogs, taskVersion, actualArrivalForServices, actualDepartureForServices)
        }
    } }.toMap() else emptyMap()
    val taskErrors = if (validationAttempted) (changedTaskErrors.keys + taskParentBoundsErrors.keys + taskCompletionErrors.keys).associateWith { id ->
        changedTaskErrors[id].orEmpty() + taskParentBoundsErrors[id].orEmpty() + taskCompletionErrors[id].orEmpty()
    } else emptyMap()
    val taskActualBoundsMessage = stringResource(R.string.actuals_task_bounds)
    val taskReviewMessage = stringResource(R.string.task_sync_review)
    val taskCompletionMessage = stringResource(if (departureSaveRequested) R.string.departure_tasks_incomplete else R.string.task_departed_periods_required)
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
    fun applyTaskReview(latest: FlightDetail, useRecordedEnds: Boolean = false) {
        // The queue retains the server record originally reviewed before offline edits.
        val previous = taskReviewed.associateBy { it.id }.toMutableMap()
        pendingTasks.forEach { row -> row.reviewedTaskJson?.let { raw ->
            runCatching { Json.decodeFromString<FlightTaskRecord>(raw) }.getOrNull()?.let { previous[it.id] = it }
        } }
        val review = reviewTaskEnds(workDrafts.tasks, previous.values.toList(), latest.tasks, entryZone, useRecordedEnds)
        if (review.conflictedTaskIds.isNotEmpty()) {
            pendingTaskEndReviewJson = Json.encodeToString(latest)
        } else {
            workDrafts = workDrafts.copy(tasks = review.items)
            taskReviewedJson = Json.encodeToString(latest.tasks)
            taskVersion = latest.rowVersion
            reviewedTaskChangesJson = Json.encodeToString(pendingTasks.filter { it.status != "pending" }.map { it.changeId })
            pendingTaskEndReviewJson = null
        }
    }
    val closeForm = { mediaSession.clear(); onClose() }
    val saveChanges: (Boolean) -> Unit = { acknowledgeConflict ->
        validationAttempted = true
        val arrivalValue = input()
        val saveArrivalInput = arrivalEnabled && (arrivalChanged || reviewedChangeId != null)
        val saveDepartureInput = departureEnabled && (departureChanged || reviewedDepartureChangeId != null)
        val saveServiceInput = servicesChanged || forcedServiceIds.isNotEmpty()
        val saveTaskInput = tasksChanged || forcedTaskIds.isNotEmpty()
        val saveFormDraft = saveServiceInput || saveTaskInput
        val arrivalValid = !saveArrivalInput || aircraftAvailable && arrivalValue.validationErrors(arrivalVersion).isEmpty() && parentBoundsErrors.isEmpty() && taskParentBoundsErrors.isEmpty()
        val departureValid = !saveDepartureInput || departureTime.isNotBlank() && !signatureRequired && !departureNeedsReview &&
            parentBoundsErrors.isEmpty() && taskParentBoundsErrors.isEmpty() && serviceCompletionErrors.isEmpty() && taskCompletionErrors.isEmpty()
        val checkedServices = serviceChanges.mapNotNull { change -> change.input?.let { value ->
            workDrafts.services.firstOrNull { it.id == change.serviceRecordId }?.let { draft ->
                serviceEditorValidation(draft, value, flight, catalogs, serviceVersion, actualArrivalForServices, actualDepartureForServices)
            }
        } }
        val servicesValid = !saveServiceInput || servicesEnabled && checkedServices.all { it.isEmpty() } && serviceCompletionErrors.isEmpty()
        val checkedTasks = taskChanges.mapNotNull { change -> change.input?.let { value ->
            workDrafts.tasks.firstOrNull { it.id == change.taskRecordId }?.let { draft ->
                taskEditorValidation(draft, value, flight, catalogs, taskVersion, actualArrivalForServices, actualDepartureForServices)
            }
        } }
        val tasksValid = !saveTaskInput || tasksEnabled && checkedTasks.all { it.isEmpty() } && taskCompletionErrors.isEmpty()
        val anyInput = saveArrivalInput || saveDepartureInput || saveServiceInput || saveTaskInput
        if (mediaSession.hasActiveRecording) {
            saveError = finishRecordingMessage
        } else if (parentBoundsErrors.isNotEmpty()) {
            saveError = actualBoundsMessage
        } else if (taskParentBoundsErrors.isNotEmpty()) {
            saveError = taskActualBoundsMessage
        } else if (serviceCompletionErrors.isNotEmpty()) {
            saveError = serviceCompletionMessage
        } else if (taskCompletionErrors.isNotEmpty()) {
            saveError = taskCompletionMessage
        } else if (saveServiceInput && !servicesValid && checkedServices.all { it.isEmpty() }) {
            saveError = serviceReviewMessage
        } else if (saveTaskInput && !tasksValid && checkedTasks.all { it.isEmpty() }) {
            saveError = taskReviewMessage
        } else if (anyInput && arrivalValid && departureValid && servicesValid && tasksValid &&
            (saveArrivalInput || saveDepartureInput) && flight.cancellationSubmissions.any {
                !it.submittedByEmployee.staffMemberId.equals(flight.currentStaffMemberId, true)
            } && !acknowledgeConflict) {
            confirmCanceledReport = true
        } else if (anyInput && arrivalValid && departureValid && servicesValid && tasksValid) {
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
            val publishedOrQueuedTaskIds = knownTaskIds + taskChanges.filter { it.input != null }.map { it.taskRecordId }
            val localTasks = workDrafts.tasks.filter { it.id !in publishedOrQueuedTaskIds }
            val draftValue = FlightServiceEditorDraft(entryZone.id, localRows, localTasks, savedSuppressed)
            val localAttachments = localRows.flatMap { row -> row.attachments.map { row.id to it } } +
                localTasks.flatMap { row -> row.attachments.map { row.id to it } }
            val durableDraft = FlightServiceFormDraft(Json.encodeToString(draftValue), localAttachments.map { (recordId, attachment) ->
                FlightServiceFormDraftAttachment(recordId, attachment.serviceAttachmentInput("$recordId/${attachment.id}" in storedServiceMedia))
            })
            formScope.launch {
                try {
                    // The coordinator validates and commits every section as one durable save.
                    onSaveBatch {
                        suspend fun saveArrival() {
                            onSaveArrival(flight.copy(rowVersion = arrivalVersion,
                                arrivalSubmissions = Json.decodeFromString<List<FlightArrivalSubmission>>(arrivalReviewedSnapshot)),
                                arrivalValue.copy(acknowledgeConflictingSubmission = acknowledgeConflict))
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
                        // Import legacy Task draft files before replacing the combined local draft.
                        if (saveTaskInput && taskChanges.isNotEmpty()) {
                            onSaveTasks(flight.copy(rowVersion = taskVersion, tasks = taskReviewed), taskChanges)
                        }
                        if (saveFormDraft) {
                            onSaveServices(flight.copy(rowVersion = serviceVersion, services = serviceReviewed), serviceChanges, durableDraft)
                        }
                        if (saveArrivalInput && !relaxArrival) saveArrival()
                        if (saveDepartureInput && !relaxDeparture) saveDeparture()
                    }
                    // A failed later section must leave every editor dirty, including its review state.
                    if (saveArrivalInput) {
                        arrivalBaseline = Json.encodeToString(arrivalValue)
                        queuedBaseline = Json.encodeToString(arrivalValue)
                        reviewedChangeId = null
                    }
                    if (saveFormDraft) {
                        if (saveTaskInput) {
                            taskBaseline = taskEditorFingerprint(workSnapshot.tasks)
                            taskBaselineInputs = Json.encodeToString(workSnapshot.tasks.associate { it.id to taskInputFingerprint(it.taskInput(entryZone)) })
                            knownTaskIdsJson = Json.encodeToString((knownTaskIds + taskChanges.filter { it.input != null }.map { it.taskRecordId } -
                                taskChanges.filter { it.input == null }.map { it.taskRecordId }.toSet()).toList())
                            reviewedTaskChangesJson = "[]"
                        }
                        suppressedServiceIdsJson = Json.encodeToString(savedSuppressed)
                        serviceBaseline = serviceEditorFingerprint(workSnapshot.services, emptyList(), savedSuppressed)
                        serviceBaselineInputs = Json.encodeToString(workSnapshot.services.associate { it.id to serviceInputFingerprint(it.serviceInput(entryZone)) })
                        knownServiceIdsJson = Json.encodeToString((knownServiceIds + serviceChanges.filter { it.input != null }.map { it.serviceRecordId } -
                            serviceChanges.filter { it.input == null }.map { it.serviceRecordId }.toSet()).toList())
                        reviewedServiceChangesJson = "[]"
                    }
                    // A later edit cannot be claimed by an earlier disk-save snapshot.
                    if (serviceEditorFingerprint(workDrafts.services, workDrafts.tasks, suppressedServiceIds) == workFingerprint) closeForm()
                } catch (cancelled: CancellationException) { throw cancelled }
                catch (error: Exception) {
                    saveError = error.userMessage()
                }
                finally { saving = false }
            }
        }
        if (!arrivalValid || !departureValid || !servicesValid || !tasksValid || mediaSession.hasActiveRecording) {
            validationRequest++
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
    val hasChanges = arrivalChanged || departureChanged || servicesChanged || tasksChanged
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
        if (kind == "ata" && ata.isBlank()) {
            val now = Instant.now().truncatedTo(java.time.temporal.ChronoUnit.MINUTES)
            originalAta = now.toString()
            ata = now.atZone(entryZone).toLocalDateTime().toString()
        }
        if (kind == "atd" && atd.isBlank()) {
            atd = operationLocalTime(flight.scheduledDepartureUtc, entryZone)
            originalAtd = flight.scheduledDepartureUtc
        }
        picker = kind
    }
    val serviceMetadata = flightServiceRecordMetadata(flight, pendingServices)
    val taskMetadata = flightTaskRecordMetadata(flight, pendingTasks)
    val submittedServices = workDrafts.services.filter { it.id in serviceMetadata }
    val submittedTasks = workDrafts.tasks.filter { it.id in taskMetadata }
    val reviewTargets = flightReviewTargets(pending, pendingDeparture, pendingServices, pendingTasks)
    fun openReview(target: FlightOperationReviewTarget) {
        if (saving) return
        if (mediaSession.hasActiveRecording) { saveError = finishRecordingMessage; validationRequest++; return }
        mediaSession.stopAudio(); saving = true; saveError = null
        formScope.launch {
            try {
                val latest = when (target.kind) { "service" -> onReviewServices(); "task" -> onReviewTasks(); else -> onReviewArrival() }
                val reviewZone = stationTimeZone(latest.stationTimeZoneId)
                when (target.kind) {
                    "arrival", "departure" -> {
                        val explicitId = if (target.kind == "arrival") pendingInput?.targetSubmissionId else pendingDepartureInput?.targetSubmissionId
                        val id = explicitId ?: if (target.kind == "arrival") latest.visibleArrival()?.id else latest.visibleDeparture()?.id
                        updateSnapshot = Json.encodeToString(latest)
                        updateReviewMode = true
                        updateReviewCached = latest.fromCache
                        updateSection = "${target.kind}:${id.orEmpty()}"
                    }
                    "service" -> {
                        val row = pendingServices.first { it.serviceRecordId == target.recordId && it.changeId == target.changeId }
                        val recorded = latest.services.firstOrNull { it.id == row.serviceRecordId }
                        val value = (row.reviewSourceInputJson ?: row.inputJson)?.let { Json.decodeFromString<SaveFlightServiceRequest>(it) } ?: recorded?.input()
                        val local = value?.toEditorDraft(row.serviceRecordId,
                            recorded?.serviceName ?: catalogs.services.firstOrNull { it.id == value.serviceId }?.name.orEmpty(),
                            recorded ?: row.reviewedRecord(), catalogs, reviewZone) ?: FlightServiceDraft(id = row.serviceRecordId)
                        workUpdateSnapshot = Json.encodeToString(FlightWorkUpdateSnapshot(latest,
                            FlightWorkDrafts(services = listOf(local)), true, reviewMode = true, fromCache = latest.fromCache))
                    }
                    "task" -> {
                        val row = pendingTasks.first { it.taskRecordId == target.recordId && it.changeId == target.changeId }
                        val recorded = latest.tasks.firstOrNull { it.id == row.taskRecordId }
                        val value = (row.reviewSourceInputJson ?: row.inputJson)?.let { Json.decodeFromString<SaveFlightTaskRequest>(it) } ?: recorded?.input()
                        val local = value?.toTaskEditorDraft(row.taskRecordId, recorded ?: row.reviewedRecord(), catalogs, reviewZone)
                            ?: FlightTaskDraft(id = row.taskRecordId)
                        workUpdateSnapshot = Json.encodeToString(FlightWorkUpdateSnapshot(latest,
                            FlightWorkDrafts(tasks = listOf(local)), false, reviewMode = true, fromCache = latest.fromCache))
                    }
                }
            } catch (cancelled: CancellationException) { throw cancelled }
            catch (error: Exception) { saveError = error.userMessage(); validationRequest++ }
            finally { saving = false }
        }
    }
    fun openWorkUpdate(service: FlightServiceDraft? = null, task: FlightTaskDraft? = null) {
        val failed = reviewTargets.firstOrNull { it.recordId == (service?.id ?: task?.id) && it.kind == if (service != null) "service" else "task" }
        if (failed != null) { openReview(failed); return }

        if (saving) return
        if (mediaSession.hasActiveRecording) { saveError = finishRecordingMessage; validationRequest++; return }
        mediaSession.stopAudio()
        workUpdateSnapshot = Json.encodeToString(FlightWorkUpdateSnapshot(flight.copy(stationTimeZoneId = entryTimeZoneId),
            FlightWorkDrafts(listOfNotNull(service), listOfNotNull(task)), isService = service != null,
            taskNumber = (submittedTasks.indexOfFirst { it.id == task?.id } + 1).coerceAtLeast(1)))
    }
    val workTargets = workDrafts.copy(
        services = submittedServices + workDrafts.services.filterNot { it.id in serviceMetadata },
        tasks = submittedTasks + workDrafts.tasks.filterNot { it.id in taskMetadata }).navigationTargets()
    val cardFlight = flight.formCardSummary(listItem, workTargets).copy(stationTimeZoneId = entryTimeZoneId)
    var activeTargetIndex by remember { mutableIntStateOf(0) }
    var completedTargetIndex by remember { mutableIntStateOf(0) }
    fun navigateWork(completed: Boolean) {
        val matches = workTargets.filter { it.completed == completed }
        if (matches.isEmpty()) return
        focus.clearFocus(); keyboard?.hide()
        val index = if (completed) completedTargetIndex++ else activeTargetIndex++
        formScope.launch { validationState.revealTarget(matches[index.mod(matches.size)].key) }
    }
    val needsReview = progressStatus in listOf(FlightProgressStatus.ArrivalNeedsReview,
        FlightProgressStatus.DepartureNeedsReview, FlightProgressStatus.CancellationNeedsReview) ||
        pendingServices.any { it.status != "pending" } || pendingTasks.any { it.status != "pending" }
    val pendingLabel = when {
        needsReview -> stringResource(R.string.flight_card_needs_review)
        progressStatus == FlightProgressStatus.CancellationRevertPending -> stringResource(R.string.flight_card_revert_pending)
        pending != null || pendingDeparture != null || pendingServices.isNotEmpty() || pendingTasks.isNotEmpty() ||
            progressStatus == FlightProgressStatus.CancellationPendingSync -> stringResource(R.string.flight_card_pending_sync)
        else -> null
    }
    FlightRecordingFormScaffold(flight, stringResource(R.string.operations_title), stringResource(R.string.operations_save),
        loadCustomerLogo, requestClose,
        saveEnabled = !saving && (arrivalEnabled && (arrivalChanged || reviewedChangeId != null) ||
            departureEnabled && (departureChanged || reviewedDepartureChangeId != null) ||
            servicesEnabled && (servicesChanged || forcedServiceIds.isNotEmpty()) ||
            tasksEnabled && (tasksChanged || forcedTaskIds.isNotEmpty())),
        saveLoading = saving, onSave = { saveChanges(false) }, progressStatus = progressStatus,
        compact = true, validationState = validationState,
        identityContent = { positioned ->
            FlightCard(cardFlight, onClick = null, loadCustomerLogo = loadCustomerLogo,
                reportStatus = cardFlight.cardReportStatus(localArrival = pending != null,
                    localDeparture = pendingDeparture != null,
                    localCancellation = progressStatus in listOf(FlightProgressStatus.CancellationPendingSync,
                        FlightProgressStatus.CancellationNeedsReview, FlightProgressStatus.CancellationRevertPending)),
                pendingSyncLabel = pendingLabel, hasSyncError = needsReview,
                onReviewClick = reviewTargets.firstOrNull()?.let { target -> { openReview(target) } },
                onActiveWorkClick = if (workTargets.any { !it.completed }) ({ navigateWork(false) }) else null,
                onCompletedWorkClick = if (workTargets.any { it.completed }) ({ navigateWork(true) }) else null,
                onFlightNumberPositioned = positioned)
        }) {
        saveError?.let {
            FlightFormValidation(it, modifier = Modifier.flightFormNavigationTarget("save-error"), registerError = false) { }
        }
        if (otherEmployeeArrival != null) {
            // The scaffold adds 10dp after this group, matching the 28dp work-note gap.
            Column(Modifier.padding(bottom = 18.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                FlightSavedDetailsCard { FlightArrivalSummary(otherEmployeeArrival.withPendingCorrection(pending, aircraftTypes), entryZone) }
                FlightSubmissionNote(otherEmployeeArrival.submittedByEmployee.fullName, otherEmployeeArrival.createdAtUtc, entryZone,
                    updateLabel = stringResource(if (pending?.status?.let { it != "pending" } == true) R.string.flight_review_action else R.string.flight_submission_update_arrival_details),
                    needsReview = pending?.status?.let { it != "pending" } == true,
                    onUpdate = if (!saving && (flight.canUpdateArrivalSubmission(otherEmployeeArrival.id) || pending?.status?.let { it != "pending" } == true)) ({
                        val failed = reviewTargets.firstOrNull { it.kind == "arrival" }
                        if (failed != null) openReview(failed) else {
                        updateReviewMode = false
                        updateSnapshot = Json.encodeToString(flight)
                        updateSection = "arrival:${otherEmployeeArrival.id}"
                        }
                    }) else null)
            }
        } else {
            FlightFormSectionTitle(stringResource(R.string.operations_arrival))
            if (pending != null && pending.status != "pending" && reviewedChangeId != pending.changeId) {
                ArrivalFormMessage(stringResource(R.string.arrival_sync_review), error = true)
                pending.error?.takeIf { pending.status != "pending" }?.let { ArrivalFormMessage(it, error = true) }
                TextButton(enabled = !saving, onClick = { reviewTargets.firstOrNull { it.kind == "arrival" }?.let(::openReview) }) {
                    Text(stringResource(R.string.flight_review_action), color = MaterialTheme.colorScheme.error)
                }
            } else if (pending != null && pending.status != "pending") ArrivalFormMessage(stringResource(R.string.arrival_review_before_save))
            FlightActualFormCard(
                id = "actual-arrival", label = stringResource(R.string.operations_actual_arrival),
                icon = Icons.Filled.FlightLand,
                time = readOnlyArrival?.let { operationLocalTime(it.ata, entryZone) } ?: ata,
                description = stringResource(R.string.arrival_choose_ata), enabled = arrivalEnabled,
                removable = arrival == null && pending == null,
                errorMessage = if (ArrivalInputError.AtaRequired in errors) stringResource(R.string.arrival_ata_required) else null,
                onOpen = { openPicker("ata") }, onRemove = {
                    val baseline = Json.decodeFromString<SaveFlightArrivalRequest>(arrivalBaseline)
                    ata = ""; originalAta = null
                    aircraftId = baseline.aircraftTypeId; tailNumber = baseline.aircraftTailNumber
                    reference = baseline.customerWorkReferenceNumber.orEmpty(); arrivalRemarks = baseline.remarks.orEmpty()
                },
            ) {
                FlightFormChoiceRow(stringResource(R.string.arrival_aircraft), (readOnlyArrival?.aircraftTypeModel ?: selectedAircraft).ifBlank { stringResource(R.string.flight_form_required_field) },
                    stringResource(R.string.arrival_aircraft_type), isPlaceholder = (readOnlyArrival?.aircraftTypeModel ?: selectedAircraft).isBlank(), enabled = arrivalEnabled,
                    errorMessage = when {
                        ArrivalInputError.AircraftRequired in errors -> stringResource(R.string.arrival_aircraft_required)
                        validationAttempted && arrivalEnabled && (arrivalChanged || reviewedChangeId != null) && !aircraftAvailable -> stringResource(R.string.arrival_aircraft_unavailable)
                        else -> null
                    }, onClick = { openPicker("aircraft") })
                FlightFormDivider()
                FlightFormTextRow(stringResource(R.string.arrival_tail_number), readOnlyArrival?.aircraftTailNumber ?: tailNumber, { tailNumber = it },
                    stringResource(R.string.flight_form_required_field), keyboardOptions = KeyboardOptions(
                        capitalization = KeyboardCapitalization.Characters, imeAction = ImeAction.Next), enabled = arrivalEnabled,
                    errorMessage = if (ArrivalInputError.TailRequired in errors) stringResource(R.string.arrival_tail_required) else null)
                FlightFormDivider()
                FlightFormTextRow(stringResource(R.string.arrival_reference), if (readOnlyArrival != null) readOnlyArrival.customerWorkReferenceNumber.orEmpty() else reference, { reference = it }, "", enabled = arrivalEnabled)
                FlightFormDivider()
                FlightFormRemarks(if (readOnlyArrival != null) readOnlyArrival.remarks.orEmpty() else arrivalRemarks, { arrivalRemarks = it },
                    stringResource(R.string.operations_arrival_remarks), enabled = arrivalEnabled,
                    heading = stringResource(R.string.operations_arrival_remarks_heading), inGroup = true)
            }
            if (ArrivalInputError.VersionRequired in errors) FlightFormValidation(stringResource(R.string.arrival_reload_required)) { }
        }
        CompositionLocalProvider(LocalFlightMediaSession provides mediaSession) {
            val submittedMedia = rememberFlightComposerMedia(workDrafts.services.flatMap { it.attachments } + workDrafts.tasks.flatMap { it.attachments }) { }
            submittedMedia.error?.let { ArrivalFormMessage(it, error = true) }
            if (servicesNeedReview) {
                ArrivalFormMessage(stringResource(R.string.service_sync_review), error = true)
                pendingServices.filter { it.status != "pending" }.mapNotNull { it.error }.distinct().forEach { ArrivalFormMessage(it, error = true) }
                pendingServices.filter { it.status != "pending" }.forEach { row ->
                    TextButton(enabled = !saving, onClick = { openReview(FlightOperationReviewTarget("service", row.serviceRecordId, row.changeId)) }) {
                        Text(stringResource(R.string.flight_review_action), color = MaterialTheme.colorScheme.error)
                    }
                }
            }
            if (serviceErrors.values.any { "rowVersion" in it }) FlightFormValidation(stringResource(R.string.service_validation_version)) { }
            FlightFormSectionTitle(stringResource(R.string.work_services_count, workDrafts.services.size))
            if (submittedServices.isNotEmpty()) Column(verticalArrangement = Arrangement.spacedBy(28.dp)) {
                submittedServices.forEach { service -> androidx.compose.runtime.key(service.id) {
                    val recorded = flight.services.firstOrNull { it.id == service.id } ?: pendingServices.firstOrNull { it.serviceRecordId == service.id }?.reviewedRecord()
                    val error = serviceErrors[service.id]?.values?.firstOrNull()?.let { stringResource(it) }
                    FlightFormValidation(error) {
                        FlightSubmittedServiceCard(service, serviceMetadata.getValue(service.id), entryZone,
                            onUpdate = if (!saving && flight.canSaveServices && !flight.hasCancellationSubmission &&
                                (recorded == null || recorded.canEdit || recorded.canSetEnd || recorded.serviceDeletable())) ({ openWorkUpdate(service = service) }) else null,
                            attachmentContent = { FlightReadOnlyAttachmentRow(it, submittedMedia) })
                    }
                } }
            }
            if (submittedServices.isNotEmpty()) Spacer(Modifier.size(8.dp))
            FlightServiceSection(workDrafts.services.filterNot { it.id in serviceMetadata }, catalogs, referenceYear, zone,
                enabled = servicesEnabled,
                canEdit = { id -> serviceReviewed.firstOrNull { it.id == id }?.canEdit ?: true },
                canSetEnd = { id -> serviceReviewed.firstOrNull { it.id == id }?.canSetEnd ?: true },
                canChangeService = { id -> serviceReviewed.firstOrNull { it.id == id }?.serviceSelectionEditable() != false &&
                    flight.services.firstOrNull { it.id == id }?.serviceSelectionEditable() != false },
                canDelete = { id -> serviceReviewed.firstOrNull { it.id == id }?.serviceDeletable() != false &&
                    flight.services.firstOrNull { it.id == id }?.serviceDeletable() != false },
                errors = serviceErrors, defaultEmployee = defaultServiceEmployee(flight, catalogs),
                timeBounds = flightWorkTimeBounds(actualArrivalForServices, actualDepartureForServices),
                showHeading = false, countOffset = submittedServices.size, draftLabels = true,
                onChange = { change -> updateWorkDrafts(workDrafts.copy(services = editLocalWorkDrafts(workDrafts.services, serviceMetadata.keys, { it.id }, change))) })
            if (tasksNeedReview) {
                ArrivalFormMessage(stringResource(R.string.task_sync_review), error = true)
                pendingTasks.filter { it.status != "pending" }.mapNotNull { it.error }.distinct().forEach { ArrivalFormMessage(it, error = true) }
                pendingTasks.filter { it.status != "pending" }.forEach { row ->
                    TextButton(enabled = !saving, onClick = { openReview(FlightOperationReviewTarget("task", row.taskRecordId, row.changeId)) }) {
                        Text(stringResource(R.string.flight_review_action), color = MaterialTheme.colorScheme.error)
                    }
                }
            }
            if (taskErrors.values.any { "rowVersion" in it }) FlightFormValidation(stringResource(R.string.task_validation_version)) { }
            FlightFormSectionTitle(stringResource(R.string.work_tasks_count, workDrafts.tasks.size))
            if (submittedTasks.isNotEmpty()) Column(verticalArrangement = Arrangement.spacedBy(28.dp)) {
                submittedTasks.forEachIndexed { index, task -> androidx.compose.runtime.key(task.id) {
                    val recorded = flight.tasks.firstOrNull { it.id == task.id } ?: pendingTasks.firstOrNull { it.taskRecordId == task.id }?.reviewedRecord()
                    val error = taskErrors[task.id]?.values?.firstOrNull()?.let { stringResource(it) }
                    FlightFormValidation(error) {
                        FlightSubmittedTaskCard(task, index + 1, taskMetadata.getValue(task.id), entryZone,
                            onUpdate = if (!saving && flight.canSaveTasks && !flight.hasCancellationSubmission &&
                                (recorded == null || recorded.canEdit || recorded.canSetEnd || recorded.canDelete)) ({ openWorkUpdate(task = task) }) else null,
                            attachmentContent = { FlightReadOnlyAttachmentRow(it, submittedMedia) })
                    }
                } }
            }
            if (submittedTasks.isNotEmpty()) Spacer(Modifier.size(8.dp))
            FlightTaskSection(workDrafts.tasks.filterNot { it.id in taskMetadata }, catalogs, referenceYear, zone,
                enabled = tasksEnabled,
                canEdit = { id -> taskReviewed.firstOrNull { it.id == id }?.canEdit ?: true },
                canSetEnd = { id -> taskReviewed.firstOrNull { it.id == id }?.canSetEnd ?: true },
                canChangeType = { id -> taskReviewed.firstOrNull { it.id == id }?.canChangeType != false &&
                    flight.tasks.firstOrNull { it.id == id }?.canChangeType != false },
                canDelete = { id -> taskReviewed.firstOrNull { it.id == id }?.canDelete != false &&
                    flight.tasks.firstOrNull { it.id == id }?.canDelete != false },
                errors = taskErrors, defaultEmployee = defaultServiceEmployee(flight, catalogs),
                timeBounds = flightWorkTimeBounds(actualArrivalForServices, actualDepartureForServices),
                showHeading = false, countOffset = submittedTasks.size, draftLabels = true,
                onChange = { change -> updateWorkDrafts(workDrafts.copy(tasks = editLocalWorkDrafts(workDrafts.tasks, taskMetadata.keys, { it.id }, change))) })
        }
        if (otherEmployeeDeparture != null) {
            // The final note also keeps 28dp to the end, including the scaffold's 24dp inset.
            Column(Modifier.padding(bottom = 4.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                FlightSavedDetailsCard { FlightDepartureSummary(otherEmployeeDeparture.withPendingCorrection(pendingDeparture), entryZone, savedSignaturePreview) }
                FlightSubmissionNote(otherEmployeeDeparture.submittedByEmployee.fullName, otherEmployeeDeparture.createdAtUtc, entryZone,
                    updateLabel = stringResource(if (pendingDeparture?.status?.let { it != "pending" } == true) R.string.flight_review_action else R.string.flight_submission_update_departure_details),
                    needsReview = pendingDeparture?.status?.let { it != "pending" } == true,
                    onUpdate = if (!saving && (flight.canUpdateDepartureSubmission(otherEmployeeDeparture.id) || pendingDeparture?.status?.let { it != "pending" } == true)) ({
                        val failed = reviewTargets.firstOrNull { it.kind == "departure" }
                        if (failed != null) openReview(failed) else {
                        updateReviewMode = false
                        updateSnapshot = Json.encodeToString(flight)
                        updateSection = "departure:${otherEmployeeDeparture.id}"
                        }
                    }) else null)
            }
        } else {
            FlightFormSectionTitle(stringResource(R.string.operations_departure))
            if (pendingDeparture != null && pendingDeparture.status != "pending") {
                ArrivalFormMessage(stringResource(if (departureNeedsReview) R.string.departure_sync_review else R.string.departure_review_before_save), error = true)
                pendingDeparture.error?.let { ArrivalFormMessage(it, error = true) }
            }
            if (departureNeedsReview) {
                TextButton(enabled = !saving, onClick = { reviewTargets.firstOrNull { it.kind == "departure" }?.let(::openReview) }) {
                    Text(stringResource(R.string.flight_review_action), color = MaterialTheme.colorScheme.error)
                }
            }
            FlightActualFormCard(
                id = "actual-departure", label = stringResource(R.string.operations_actual_departure), time = atd,
                icon = Icons.Filled.FlightTakeoff,
                description = stringResource(R.string.operations_choose_atd), enabled = departureEnabled,
                removable = departure == null && pendingDeparture == null,
                errorMessage = if (validationAttempted && departureChanged && departureTime.isBlank()) stringResource(R.string.departure_atd_required) else null,
                onOpen = { openPicker("atd") }, onRemove = {
                    atd = ""; originalAtd = null; signature = ""; signatureSignedAtUtc = null
                    departureRemarks = initialDepartureRemarks
                },
            ) {
                FlightFormValidation(if (validationAttempted && departureChanged && signatureRequired) stringResource(R.string.departure_signature_required) else null) {
                    FlightSignatureField(signature, hasSavedSignature = hasSavedSignature, savedPreview = savedSignaturePreview,
                        loading = signaturePreviewLoading, enabled = departureEnabled, onOpen = { openPicker("signature") })
                }
                FlightFormDivider()
                FlightFormRemarks(departureRemarks, { departureRemarks = it }, stringResource(R.string.operations_departure_remarks), enabled = departureEnabled,
                    heading = stringResource(R.string.operations_departure_remarks_heading), inGroup = true)
            }
        }
    }
    if (picker == "ata" || picker == "atd") {
        val isArrival = picker == "ata"
        FlightFormDateTimeSheet(LocalDateTime.parse(if (isArrival) ata else atd), referenceYear,
            onValueChange = { if (isArrival) ata = it.toString() else atd = it.toString() }, onDismiss = { picker = null },
            title = stringResource(if (isArrival) R.string.arrival_ata else R.string.operations_atd), showTitle = false)
    }
    if (picker == "aircraft") FlightWorkPicker(title = stringResource(R.string.arrival_aircraft_type),
        searchLabel = stringResource(R.string.arrival_search_aircraft), emptyLabel = stringResource(R.string.arrival_no_aircraft),
        options = aircraftChoices.map { FlightWorkChoice(it.id, it.title, it.subtitle) }, selectedId = aircraftId,
        onDismiss = { picker = null }, onSelect = { aircraftId = it })
    if (picker == "signature") FlightSignatureSheet(signature, hasSavedSignature, savedSignaturePreview,
        signaturePreviewLoading, onRetry = { signaturePreviewRetry++ }, onValueChange = {
        signature = it
        signatureSignedAtUtc = if (it.isBlank()) null else Instant.now().toString()
    }, onDismiss = { picker = null })
    if (confirmCanceledReport) FlightSubmissionConfirmation(
        stringResource(R.string.submission_canceled_warning_title), stringResource(R.string.submission_canceled_warning),
        stringResource(R.string.submission_save_anyway), onConfirm = { confirmCanceledReport = false; saveChanges(true) },
        onDismiss = { confirmCanceledReport = false })
    if (pendingTaskEndReviewJson != null) FlightSubmissionConfirmation(
        stringResource(R.string.task_review_end_title), stringResource(R.string.task_review_end_message),
        stringResource(R.string.task_review_use_recorded_end), onConfirm = {
            pendingTaskEndReviewJson?.let { applyTaskReview(Json.decodeFromString(it), useRecordedEnds = true) }
        }, onDismiss = { pendingTaskEndReviewJson = null })
    if (confirmDiscard) FlightFormDiscardDialog(stringResource(R.string.operations_discard_title),
        onDismiss = { confirmDiscard = false }, onDiscard = closeForm)
    val sectionToUpdate = updateSection
    val snapshotToUpdate = updateSnapshot
    if (sectionToUpdate != null && snapshotToUpdate != null) {
        val snapshot = remember(snapshotToUpdate) { Json.decodeFromString<FlightDetail>(snapshotToUpdate) }
        androidx.compose.runtime.key(sectionToUpdate, snapshotToUpdate) {
            FlightSubmissionUpdateForm(snapshot, sectionToUpdate.substringAfter(":"), sectionToUpdate.startsWith("arrival:"),
                aircraftTypes, loadCustomerLogo, pending, pendingDeparture, loadSignature, onReviewArrival,
                onSaveArrival, onSaveDeparture, onClose = { updateSection = null; updateSnapshot = null },
                reviewMode = updateReviewMode, cachedReview = updateReviewCached, onSaveReviewed = onSaveReviewed, onDiscardReviewed = onDiscardReviewed)
        }
    }
    workUpdateSnapshot?.let { raw ->
        val snapshot = remember(raw) { Json.decodeFromString<FlightWorkUpdateSnapshot>(raw) }
        androidx.compose.runtime.key(raw) {
            FlightWorkSubmissionUpdateForm(snapshot, flight, catalogs,
                pendingServices.firstOrNull { it.serviceRecordId == snapshot.recordId },
                pendingTasks.firstOrNull { it.taskRecordId == snapshot.recordId },
                pendingInput?.takeIf { !snapshot.reviewMode || pending?.status == "pending" },
                pendingDepartureInput?.takeIf { !snapshot.reviewMode || pendingDeparture?.status == "pending" },
                loadCustomerLogo, if (snapshot.isService) onReviewServices else onReviewTasks,
                onSaveServices, onSaveTasks, onSaveBatch, loadServiceAttachment, loadPendingServiceAttachment,
                loadTaskAttachment, loadPendingTaskAttachment, onClose = { workUpdateSnapshot = null },
                onSaveReviewed = onSaveReviewed, onDiscardReviewed = onDiscardReviewed)
        }
    }
}

/** Removing a new actual clears its temporary fields; recorded submissions keep their correction rules. */
@Composable
private fun FlightActualFormCard(
    id: String,
    label: String,
    icon: ImageVector,
    time: String,
    description: String,
    enabled: Boolean,
    removable: Boolean,
    errorMessage: String?,
    onOpen: () -> Unit,
    onRemove: () -> Unit,
    content: @Composable () -> Unit,
) {
    FlightFormFieldGroup {
        Column(Modifier.animateContentSize(animationSpec = tween(200))) {
            if (time.isBlank()) FlightFormValidation(errorMessage) {
                FlightWorkAddRow(label, enabled = enabled, onClick = onOpen)
            } else {
                if (removable) FlightWorkRemovableEntry(id, stringResource(R.string.work_remove_item, label),
                    onRemove = onRemove, compactDelete = true, enabled = enabled) {
                    FlightFormDateTimeField(time, label, description, enabled = enabled, errorMessage = errorMessage,
                        leadingPadding = 0.dp, displayPattern = "dd/MM/yy HH:mm", prominentLabel = true, onClick = onOpen)
                } else FlightFormDateTimeField(time, label, description, enabled = enabled, errorMessage = errorMessage,
                    leadingPadding = 0.dp, displayPattern = "dd/MM/yy HH:mm", prominentLabel = true,
                    leadingContent = { FlightActualTimeIcon(icon) }, onClick = onOpen)
                FlightFormDivider()
                content()
            }
        }
    }
}

/** Saved details retain their existing summary and physical column order. */
@Composable
private fun FlightSavedDetailsCard(content: @Composable () -> Unit) {
    CompositionLocalProvider(androidx.compose.ui.platform.LocalLayoutDirection provides LayoutDirection.Ltr) {
        Surface(Modifier.fillMaxWidth(), color = MaterialTheme.colorScheme.surface, shape = RoundedCornerShape(16.dp)) {
            Column(Modifier.padding(12.dp)) { content() }
        }
    }
}

@Composable
private fun ArrivalFormMessage(message: String, error: Boolean = false) {
    Text(message, Modifier.padding(horizontal = 14.dp), style = MaterialTheme.typography.bodySmall,
        color = if (error) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.onSurfaceVariant)
}

private data class ArrivalChoice(val id: String, val title: String, val subtitle: String)
