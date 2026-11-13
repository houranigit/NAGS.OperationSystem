package com.nags.operations.ui.flights

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.layout.padding
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
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
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import com.nags.operations.R
import com.nags.operations.ui.components.InlineNotice
import com.nags.operations.ui.components.InlineNoticeKind
import com.nags.operations.data.*
import com.nags.operations.data.db.entities.FlightServicePendingEntity
import com.nags.operations.data.db.entities.FlightTaskPendingEntity
import com.nags.operations.ui.util.stationTimeZone
import java.time.LocalDate
import java.time.ZoneId
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.launch
import kotlinx.serialization.Serializable
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json

/** Only this record and the flight it was reviewed against enter the separate update page. */
@Serializable
internal data class FlightWorkUpdateSnapshot(
    val flight: FlightDetail,
    val drafts: FlightWorkDrafts,
    val isService: Boolean,
    val taskNumber: Int = 1,
    val reviewMode: Boolean = false,
    val fromCache: Boolean = false,
) {
    val recordId: String get() = if (isService) drafts.services.single().id else drafts.tasks.single().id
}

/** Stage sparse retained intent against the read-only server snapshot without writing the queue. */
internal fun flightWorkReviewDrafts(
    snapshot: FlightWorkUpdateSnapshot,
    catalogs: FlightOperationsCatalogs,
    zone: ZoneId,
    serviceSource: SaveFlightServiceRequest? = null,
    taskSource: SaveFlightTaskRequest? = null,
    retainedAttachmentIds: Set<String> = emptySet(),
): FlightWorkDrafts {
    val id = snapshot.recordId
    return if (snapshot.isService) {
        val record = snapshot.flight.services.firstOrNull { it.id == id }
        val local = serviceSource ?: snapshot.drafts.services.single().serviceInput(zone, retainedAttachmentIds)
        val merged = mergeServiceReview(record?.input() ?: SaveFlightServiceRequest("", ""), local)
        FlightWorkDrafts(services = listOf(merged.toEditorDraft(id,
            catalogs.services.firstOrNull { it.id == merged.serviceId }?.name
                ?: record?.takeIf { it.serviceId == merged.serviceId }?.serviceName
                ?: snapshot.drafts.services.single().serviceName,
            record, catalogs, zone)))
    } else {
        val record = snapshot.flight.tasks.firstOrNull { it.id == id }
        val local = taskSource ?: snapshot.drafts.tasks.single().taskInput(zone, retainedAttachmentIds)
        FlightWorkDrafts(tasks = listOf(mergeTaskReview(record?.input() ?: SaveFlightTaskRequest(-1, ""), local)
            .toTaskEditorDraft(id, record, catalogs, zone)))
    }
}

/** Correction validation uses retained sibling additions/deletions as well as the current server graph. */
internal fun flightServiceCorrectionOverlapErrors(
    flight: FlightDetail,
    pending: List<FlightServicePendingEntity>,
    catalogs: FlightOperationsCatalogs,
    zone: ZoneId,
    service: FlightServiceDraft,
): Map<String, Int> {
    val finalServices = initialServiceEditorDraft(flight, catalogs,
        pending.filterNot { it.serviceRecordId == service.id }, null, zone).services.filterNot { it.id == service.id } + service
    return serviceOverlapValidation(finalServices, zone, setOf(service.id))[service.id].orEmpty()
}

@Composable
internal fun FlightWorkSubmissionUpdateForm(
    snapshot: FlightWorkUpdateSnapshot,
    latestFlight: FlightDetail,
    catalogs: FlightOperationsCatalogs,
    pendingService: FlightServicePendingEntity?,
    pendingTask: FlightTaskPendingEntity?,
    queuedArrival: SaveFlightArrivalRequest?,
    queuedDeparture: SaveFlightDepartureRequest?,
    loadCustomerLogo: (String) -> Flow<ByteArray?>,
    onReview: suspend () -> FlightDetail,
    onSaveServices: suspend (FlightDetail, List<FlightServiceChange>, FlightServiceFormDraft?) -> Unit,
    onSaveTasks: suspend (FlightDetail, List<FlightTaskChange>) -> Unit,
    onSaveBatch: suspend (suspend () -> Unit) -> Unit,
    loadServiceAttachment: suspend (String, String, String, String) -> String?,
    loadPendingServiceAttachment: suspend (String, String, String) -> String?,
    loadTaskAttachment: suspend (String, String, String, String) -> String?,
    loadPendingTaskAttachment: suspend (String, String, String) -> String?,
    onClose: () -> Unit,
    onSaveReviewed: suspend (FlightDetail, FlightOperationReviewTarget, suspend () -> Unit) -> Unit = { _, _, _ -> error("Review saving is unavailable.") },
    onDiscardReviewed: suspend (FlightOperationReviewTarget) -> Unit = { error("Review discard is unavailable.") },
    pendingServiceGraph: List<FlightServicePendingEntity> = emptyList(),
) {
    val id = snapshot.recordId
    val isService = snapshot.isService
    val zone = remember(snapshot.flight.stationTimeZoneId) { stationTimeZone(snapshot.flight.stationTimeZoneId) }
    val referenceYear = rememberSaveable { LocalDate.now(zone).year }
    val reviewing = snapshot.reviewMode || (if (isService) pendingService?.status else pendingTask?.status)?.let { it != "pending" } == true
    val onlineDraft = remember {
        if (isService) FlightWorkDrafts(services = listOf(snapshot.flight.services.firstOrNull { it.id == id }?.input()
            ?.toEditorDraft(id, snapshot.flight.services.firstOrNull { it.id == id }?.serviceName.orEmpty(), snapshot.flight.services.firstOrNull { it.id == id }, catalogs, zone)
            ?: FlightServiceDraft(id = id)))
        else FlightWorkDrafts(tasks = listOf(snapshot.flight.tasks.firstOrNull { it.id == id }?.input()
            ?.toTaskEditorDraft(id, snapshot.flight.tasks.firstOrNull { it.id == id }, catalogs, zone) ?: FlightTaskDraft(id = id)))
    }
    var onlineDisplayDraft by remember { mutableStateOf(onlineDraft) }
    var localDiscarded by rememberSaveable { mutableStateOf(false) }
    var localApplied by rememberSaveable { mutableStateOf(reviewing) }
    val localDelete = remember { if (isService) pendingService?.operation == "delete" else pendingTask?.operation == "delete" }
    val initialStoredIds = remember {
        val files = if (isService) pendingService?.attachmentFilesJson else pendingTask?.attachmentFilesJson
        runCatching { Json.decodeFromString<Map<String, String>>(files ?: "{}") }.getOrDefault(emptyMap()).keys
    }
    val mergedReviewDraft = remember {
        val serviceSource = pendingService?.let { (it.reviewSourceInputJson ?: it.inputJson)?.let { raw ->
            Json.decodeFromString<SaveFlightServiceRequest>(raw) } }
        val taskSource = pendingTask?.let { (it.reviewSourceInputJson ?: it.inputJson)?.let { raw ->
            Json.decodeFromString<SaveFlightTaskRequest>(raw) } }
        flightWorkReviewDrafts(snapshot, catalogs, zone, serviceSource, taskSource, initialStoredIds)
    }
    // Preserve the exact initial staged values separately from subsequent editable changes.
    val stagedReviewJson = rememberSaveable {
        if (isService) Json.encodeToString(mergedReviewDraft.services.single().serviceInput(zone, initialStoredIds))
        else Json.encodeToString(mergedReviewDraft.tasks.single().taskInput(zone, initialStoredIds))
    }
    val initialDraft = if (reviewing) { if (localDelete) FlightWorkDrafts() else mergedReviewDraft } else snapshot.drafts

    val initialFlight = remember {
        if (!reviewing && isService && pendingService != null) snapshot.flight.copy(rowVersion = pendingService.rowVersion,
            services = snapshot.flight.services.filterNot { it.id == id } + listOfNotNull(pendingService.reviewedRecord()))
        else if (!reviewing && !isService && pendingTask != null) snapshot.flight.copy(rowVersion = pendingTask.rowVersion,
            tasks = snapshot.flight.tasks.filterNot { it.id == id } + listOfNotNull(pendingTask.reviewedRecord()))
        else snapshot.flight
    }
    var wasPublished by rememberSaveable { mutableStateOf(if (isService) snapshot.flight.services.any { it.id == id } || pendingService?.reviewedRecord() != null
        else snapshot.flight.tasks.any { it.id == id } || pendingTask?.reviewedRecord() != null) }
    var reviewedJson by rememberSaveable { mutableStateOf(Json.encodeToString(initialFlight)) }
    val reviewed = remember(reviewedJson) { Json.decodeFromString<FlightDetail>(reviewedJson) }
    var draft by rememberSaveable(stateSaver = FlightWorkDraftsSaver) { mutableStateOf(initialDraft) }
    var baseline by rememberSaveable { mutableStateOf(if (isService) serviceEditorFingerprint(draft.services, emptyList(), emptyList()) else taskEditorFingerprint(draft.tasks)) }
    val changed = (if (isService) serviceEditorFingerprint(draft.services, emptyList(), emptyList()) else taskEditorFingerprint(draft.tasks)) != baseline
    var lastPresentDraft by rememberSaveable(stateSaver = FlightWorkDraftsSaver) { mutableStateOf(if (reviewing) mergedReviewDraft else snapshot.drafts) }
    val serviceRecord = reviewed.services.firstOrNull { it.id == id }
    val taskRecord = reviewed.tasks.firstOrNull { it.id == id }
    var missing by rememberSaveable { mutableStateOf(reviewing && wasPublished && (if (isService) snapshot.flight.services.none { it.id == id } else snapshot.flight.tasks.none { it.id == id })) }
    var saving by remember { mutableStateOf(false) }
    var attempted by rememberSaveable { mutableStateOf(false) }
    var saveError by rememberSaveable { mutableStateOf<String?>(null) }
    var reviewedChangeId by rememberSaveable { mutableStateOf<String?>(null) }
    val pendingId = if (isService) pendingService?.changeId else pendingTask?.changeId
    val reviewCardChangeId = rememberSaveable { mutableStateOf(pendingId) }.value
    val pendingStatus = if (isService) pendingService?.status else pendingTask?.status
    val needsReview = pendingStatus != null && pendingStatus != "pending" && reviewedChangeId != pendingId
    var confirmDiscard by rememberSaveable { mutableStateOf(false) }
    var endReviewJson by rememberSaveable { mutableStateOf<String?>(null) }
    val mediaSession = rememberFlightMediaSession()
    val scope = rememberCoroutineScope()
    val validation = rememberFlightFormValidationState()
    var validationRequest by remember { mutableIntStateOf(0) }
    LaunchedEffect(validationRequest) {
        if (validationRequest > 0) {
            withFrameNanos { }
            if (!validation.revealFirstError()) validation.revealTarget("work-update-error")
        }
    }
    val canEdit = if (isService) serviceRecord?.canEdit != false else taskRecord?.canEdit != false
    val canSetEnd = if (isService) serviceRecord?.canSetEnd != false else taskRecord?.canSetEnd != false
    val canDelete = if (isService) serviceRecord?.serviceDeletable() != false else taskRecord?.canDelete != false
    val completionOnly = if (isService) serviceRecord?.canCloseOnly == true else taskRecord?.canCloseOnly == true
    val hasPublishedTarget = if (isService) serviceRecord != null else taskRecord != null
    val canAdd = if (isService) reviewed.canAddServices ?: reviewed.canSaveServices else reviewed.canAddTasks ?: reviewed.canSaveTasks
    val hasWriteCapability = if (hasPublishedTarget) canEdit || canSetEnd || canDelete else canAdd
    val enabled = !saving && !missing && !reviewed.hasCancellationSubmission && hasWriteCapability &&
        (if (isService) reviewed.canSaveServices else reviewed.canSaveTasks)
    val openPerformerIds = if (isService) serviceRecord?.performers.orEmpty().filter { it.endUtc == null }.map { it.id }.toSet()
        else taskRecord?.performers.orEmpty().filter { it.endUtc == null }.map { it.id }.toSet()
    val openResourceIds = mapOf(
        FlightWorkResourceKind.Tool to taskRecord?.tools.orEmpty(),
        FlightWorkResourceKind.Material to taskRecord?.materials.orEmpty(),
        FlightWorkResourceKind.GeneralSupport to taskRecord?.generalSupports.orEmpty(),
    ).mapValues { (_, values) -> values.filter { it.calculationType == 1 && it.endUtc == null }.map { it.id }.toSet() }
    val actualArrival = queuedArrival?.ata ?: reviewed.visibleArrival()?.ata
    val actualDeparture = queuedDeparture?.atd ?: reviewed.visibleDeparture()?.atd
    fun overlapErrors(service: FlightServiceDraft) = flightServiceCorrectionOverlapErrors(reviewed, pendingServiceGraph, catalogs, zone, service)
    val storedFilesJson = if (isService) pendingService?.attachmentFilesJson else pendingTask?.attachmentFilesJson
    val storedIds = remember(storedFilesJson) { runCatching { Json.decodeFromString<Map<String, String>>(storedFilesJson ?: "{}") }.getOrDefault(emptyMap()).keys }
    val initialStoredIdsJson = rememberSaveable { Json.encodeToString(storedIds.toList()) }
    val retainedIds = storedIds + remember(initialStoredIdsJson) { Json.decodeFromString<List<String>>(initialStoredIdsJson).toSet() }
    fun reviewTarget() = FlightOperationReviewTarget(if (isService) "service" else "task", id,
        checkNotNull(reviewCardChangeId), useLocalSource = localApplied, stagedInputJson = stagedReviewJson)
    val latestService = latestFlight.takeIf { it.id == snapshot.flight.id }?.services?.firstOrNull { it.id == id }
    val latestTask = latestFlight.takeIf { it.id == snapshot.flight.id }?.tasks?.firstOrNull { it.id == id }
    val acknowledgedAttachments = if (isService) latestService?.attachments.orEmpty() else latestTask?.attachments.orEmpty()
    LaunchedEffect(acknowledgedAttachments, retainedIds, latestService != null || latestTask != null) {
        if (if (isService) latestService != null else latestTask != null) wasPublished = true
        fun promote(value: FlightWorkDrafts) = value.copy(
            services = value.services.map { if (it.id == id) it.copy(attachments = promoteAcknowledgedWorkAttachments(it.attachments,
                acknowledgedAttachments, retainedIds)) else it },
            tasks = value.tasks.map { if (it.id == id) it.copy(attachments = promoteAcknowledgedWorkAttachments(it.attachments,
                acknowledgedAttachments, retainedIds)) else it })
        draft = promote(draft)
        lastPresentDraft = promote(lastPresentDraft)
        baseline = if (isService) {
            val before = checkNotNull(decodeServiceEditorDraft(baseline))
            serviceEditorFingerprint(promote(FlightWorkDrafts(services = before.services)).services, emptyList(), emptyList())
        } else {
            val before = Json.decodeFromString<List<FlightTaskDraft>>(baseline)
            taskEditorFingerprint(promote(FlightWorkDrafts(tasks = before)).tasks)
        }
    }
    val serviceValue = draft.services.singleOrNull()
    val taskValue = draft.tasks.singleOrNull()
    val serviceInput = serviceValue?.serviceInput(zone, retainedIds)
    val taskInput = taskValue?.taskInput(zone, retainedIds)
    val errors = if (!attempted) emptyMap() else if (isService && serviceValue != null && serviceInput != null) {
        serviceEditorValidation(serviceValue, serviceInput, reviewed, catalogs, reviewed.rowVersion, actualArrival, actualDeparture) + overlapErrors(serviceValue) +
            if (actualDeparture != null) departureServiceCompletionValidation(listOf(serviceValue), reviewed.services,
                listOfNotNull(pendingService), listOf(FlightServiceChange(id, serviceInput)), zone, setOf(id))[id].orEmpty() else emptyMap()
    } else if (!isService && taskValue != null && taskInput != null) {
        taskEditorValidation(taskValue, taskInput, reviewed, catalogs, reviewed.rowVersion, actualArrival, actualDeparture) +
            if (actualDeparture != null) departureTaskCompletionValidation(listOf(taskValue), reviewed.tasks,
                listOfNotNull(pendingTask), listOf(FlightTaskChange(id, taskInput)), zone, setOf(id))[id].orEmpty() else emptyMap()
    } else emptyMap()
    val attachments = serviceValue?.attachments ?: taskValue?.attachments.orEmpty()
    val onlineAttachments = onlineDisplayDraft.services.flatMap { it.attachments } + onlineDisplayDraft.tasks.flatMap { it.attachments }
    val mediaKeys = (attachments + onlineAttachments).map { it.id to it.uploadedAtUtc }.distinct()
    LaunchedEffect(mediaKeys, storedFilesJson) {
        for ((attachmentId, uploadedAt) in mediaKeys) {
            val item = (draft.services.singleOrNull()?.attachments ?: draft.tasks.singleOrNull()?.attachments.orEmpty())
                .firstOrNull { it.id == attachmentId } ?: onlineAttachments.firstOrNull { it.id == attachmentId } ?: continue
            if (item.ownedCapture || attachmentId !in storedIds && uploadedAt == null) continue
            try {
                val uri = item.uri.takeIf(String::isNotBlank) ?: if (isService) {
                    if (attachmentId in storedIds) loadPendingServiceAttachment(reviewed.id, id, attachmentId)
                    else loadServiceAttachment(reviewed.id, id, attachmentId, checkNotNull(uploadedAt))
                } else {
                    if (attachmentId in storedIds) loadPendingTaskAttachment(reviewed.id, id, attachmentId)
                    else loadTaskAttachment(reviewed.id, id, attachmentId, checkNotNull(uploadedAt))
                }
                if (uri != null) {
                    fun update(items: List<FlightAttachmentDraft>) = items.map { if (it.id == attachmentId && it.uri.isBlank()) it.copy(uri = uri, ownedCapture = false) else it }
                    draft = draft.copy(services = draft.services.map { it.copy(attachments = update(it.attachments)) },
                        tasks = draft.tasks.map { it.copy(attachments = update(it.attachments)) })
                    onlineDisplayDraft = onlineDisplayDraft.copy(services = onlineDisplayDraft.services.map { it.copy(attachments = update(it.attachments)) },
                        tasks = onlineDisplayDraft.tasks.map { it.copy(attachments = update(it.attachments)) })
                }
            } catch (cancelled: CancellationException) { throw cancelled }
            catch (_: Exception) { /* Metadata remains retained when a private preview is unavailable. */ }
        }
    }
    fun changeDraft(next: FlightWorkDrafts) {
        if (next.services.isEmpty() && next.tasks.isEmpty()) { lastPresentDraft = draft; draft = next; return }
        val retained = (next.services.flatMap { it.attachments } + next.tasks.flatMap { it.attachments }).map { it.uri }.toSet()
        (draft.services.flatMap { it.attachments } + draft.tasks.flatMap { it.attachments })
            .filter { it.ownedCapture && it.uri !in retained }.forEach { mediaSession.delete(it.uri) }
        draft = next
    }
    fun applyReview(latest: FlightDetail, useRecordedEnd: Boolean = false) {
        if (wasPublished && (if (isService) latest.services.none { it.id == id } else latest.tasks.none { it.id == id })) {
            missing = true; reviewedJson = Json.encodeToString(latest); return
        }
        if (isService) {
            val result = reviewServiceEnds(draft.services, reviewed.services, latest.services, zone, useRecordedEnd)
            if (result.conflictedServiceIds.isNotEmpty()) { endReviewJson = Json.encodeToString(latest); return }
            draft = draft.copy(services = result.items)
        } else {
            val result = reviewTaskEnds(draft.tasks, reviewed.tasks, latest.tasks, zone, useRecordedEnd)
            if (result.conflictedTaskIds.isNotEmpty()) { endReviewJson = Json.encodeToString(latest); return }
            draft = draft.copy(tasks = result.items)
        }
        reviewedJson = Json.encodeToString(latest)
        reviewedChangeId = pendingId
        endReviewJson = null
    }
    val finishRecording = stringResource(R.string.service_finish_recording)
    val deleteNotAllowed = stringResource(R.string.work_delete_not_allowed)
    val close = { if (!saving) { if (changed || mediaSession.hasActiveRecording) confirmDiscard = true else onClose() }; Unit }
    BackHandler { close() }
    fun save() {
        if (!enabled) return
        attempted = true
        val service = draft.services.singleOrNull()
        val task = draft.tasks.singleOrNull()
        val svcInput = service?.serviceInput(zone, retainedIds)
        val tskInput = task?.taskInput(zone, retainedIds)
        val fieldErrors = if (isService && service != null && svcInput != null) serviceEditorValidation(service, svcInput,
            reviewed, catalogs, reviewed.rowVersion, actualArrival, actualDeparture) + overlapErrors(service) +
            if (actualDeparture != null) departureServiceCompletionValidation(listOf(service), reviewed.services, listOfNotNull(pendingService),
                listOf(FlightServiceChange(id, svcInput)), zone, setOf(id))[id].orEmpty() else emptyMap()
        else if (!isService && task != null && tskInput != null) taskEditorValidation(task, tskInput,
            reviewed, catalogs, reviewed.rowVersion, actualArrival, actualDeparture) +
            if (actualDeparture != null) departureTaskCompletionValidation(listOf(task), reviewed.tasks, listOfNotNull(pendingTask),
                listOf(FlightTaskChange(id, tskInput)), zone, setOf(id))[id].orEmpty() else emptyMap()
        else emptyMap()
        if (mediaSession.hasActiveRecording) { saveError = finishRecording; validationRequest++; return }
        if (fieldErrors.isNotEmpty()) { validationRequest++; return }
        if (service == null && isService && !canDelete || task == null && !isService && !canDelete) {
            saveError = deleteNotAllowed; validationRequest++; return
        }
        saving = true; saveError = null
        scope.launch {
            try {
                val write: suspend () -> Unit = {
                    if (isService) onSaveServices(reviewed, listOf(FlightServiceChange(id, svcInput)), null)
                    else onSaveTasks(reviewed, listOf(FlightTaskChange(id, tskInput)))
                }
                if (reviewing && !localDiscarded && reviewCardChangeId != null)
                    onSaveReviewed(reviewed, reviewTarget(), write)
                else onSaveBatch(write)
                mediaSession.clear(); onClose()
            } catch (cancelled: CancellationException) { throw cancelled }
            catch (failure: Exception) { saveError = failure.userMessage(); validationRequest++ }
            finally { saving = false }
        }
    }
    val title = stringResource(when {
        reviewing -> R.string.flight_review_action
        completionOnly && isService -> R.string.flight_complete_service
        completionOnly -> R.string.flight_complete_task
        isService -> R.string.work_update_service_title
        else -> R.string.work_update_task_title
    })
    FlightRecordingFormScaffold(reviewed, title, stringResource(R.string.work_update_save), loadCustomerLogo, close,
        saveEnabled = enabled && (changed || localApplied || reviewing && !localDiscarded || reviewedChangeId != null), saveLoading = saving, onSave = { save() },
        compact = true, validationState = validation, identityContent = {}, navigationIcon = Icons.AutoMirrored.Filled.ArrowBack,
        navigationDescription = stringResource(R.string.account_back), headerTitle = title) {
        saveError?.let { InlineNotice(it, modifier = Modifier.flightFormNavigationTarget("work-update-error"), kind = InlineNoticeKind.Error) }
        if (missing) InlineNotice(stringResource(R.string.work_update_unavailable), kind = InlineNoticeKind.Warning)
        else if (!hasWriteCapability) InlineNotice(stringResource(R.string.flight_work_no_longer_editable), kind = InlineNoticeKind.Warning)
        if (needsReview && !reviewing) {
            InlineNotice(stringResource(if (isService) R.string.service_sync_review else R.string.task_sync_review), kind = InlineNoticeKind.Warning)
            (if (isService) pendingService?.error else pendingTask?.error)?.let {
                InlineNotice(it, kind = InlineNoticeKind.Error)
            }
            TextButton(enabled = !saving, onClick = {
                saving = true
                scope.launch {
                    try { applyReview(onReview()); saveError = null }
                    catch (cancelled: CancellationException) { throw cancelled }
                    catch (failure: Exception) { saveError = failure.userMessage(); validationRequest++ }
                    finally { saving = false }
                }
            }) { Text(stringResource(if (isService) R.string.service_review else R.string.task_review)) }
        }
        if (reviewing) {
            FlightReviewOnlineHeading(snapshot.fromCache || snapshot.flight.fromCache)
            CompositionLocalProvider(LocalFlightMediaSession provides mediaSession) {
                val onlineMedia = rememberFlightComposerMedia(onlineAttachments) { }
                onlineMedia.error?.let { InlineNotice(it, kind = InlineNoticeKind.Error) }
                if (isService && serviceRecord != null) FlightSubmittedServiceCard(onlineDisplayDraft.services.single(),
                    flightServiceRecordMetadata(reviewed, emptyList()).getValue(id), zone,
                    attachmentContent = { FlightReadOnlyAttachmentRow(it, onlineMedia) })
                else if (!isService && taskRecord != null) FlightSubmittedTaskCard(onlineDisplayDraft.tasks.single(), snapshot.taskNumber,
                    flightTaskRecordMetadata(reviewed, emptyList()).getValue(id), zone,
                    attachmentContent = { FlightReadOnlyAttachmentRow(it, onlineMedia) })
                else Text(stringResource(R.string.flight_review_empty_online), Modifier.padding(horizontal = 14.dp),
                    style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            if (!localDiscarded) FlightReviewLocalHeading()
        }
        errors["rowVersion"]?.let { FlightFormValidation(stringResource(it)) {} }
        CompositionLocalProvider(LocalFlightMediaSession provides mediaSession) {
            if (isService) FlightServiceSection(draft.services, catalogs, referenceYear, zone,
                onChange = { update -> changeDraft(draft.copy(services = update(draft.services))) }, enabled = enabled,
                canEdit = { canEdit }, canSetEnd = { canSetEnd }, canDelete = { canDelete },
                canChangeService = { serviceRecord?.serviceSelectionEditable() != false }, errors = mapOf(id to errors),
                defaultEmployee = defaultServiceEmployee(reviewed, catalogs), timeBounds = flightWorkTimeBounds(actualArrival, actualDeparture),
                showHeading = false, showAdd = false, showRemoveControls = canDelete,
                completionOnly = { completionOnly },
                canCompletePerformerEnd = { _, performerId -> completionOnly && performerId in openPerformerIds })
            else FlightTaskSection(draft.tasks, catalogs, referenceYear, zone,
                onChange = { update -> changeDraft(draft.copy(tasks = update(draft.tasks))) }, enabled = enabled,
                canEdit = { canEdit }, canSetEnd = { canSetEnd }, canDelete = { canDelete }, canChangeType = { taskRecord?.canChangeType != false },
                errors = mapOf(id to errors), defaultEmployee = defaultServiceEmployee(reviewed, catalogs), timeBounds = flightWorkTimeBounds(actualArrival, actualDeparture),
                showHeading = false, showAdd = false, countOffset = snapshot.taskNumber - 1, showRemoveControls = canDelete,
                completionOnly = { completionOnly },
                canCompletePerformerEnd = { _, performerId -> completionOnly && performerId in openPerformerIds },
                canCompleteResourceEnd = { _, kind, usageId -> completionOnly && usageId in openResourceIds[kind].orEmpty() })
        }
        if (serviceValue == null && isService || taskValue == null && !isService) {
            Text(stringResource(if (isService) R.string.work_delete_service_on_save else R.string.work_delete_task_on_save),
                Modifier.padding(horizontal = 14.dp), style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            TextButton(enabled = !saving, onClick = { draft = lastPresentDraft }) { Text(stringResource(R.string.operations_signature_undo)) }
        }

        if (reviewing && !localDiscarded && reviewCardChangeId != null) {
            if ((serviceValue == null && isService || taskValue == null && !isService) && !canDelete) Text(deleteNotAllowed, Modifier.padding(horizontal = 14.dp),
                style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.error)
            FlightReviewActions(!saving, onDiscard = {
                saving = true; saveError = null
                scope.launch {
                    try {
                        onDiscardReviewed(FlightOperationReviewTarget(if (isService) "service" else "task", id, reviewCardChangeId))
                        draft = onlineDraft; lastPresentDraft = onlineDraft; localApplied = false; localDiscarded = true
                        baseline = if (isService) serviceEditorFingerprint(draft.services, emptyList(), emptyList()) else taskEditorFingerprint(draft.tasks)
                    } catch (cancelled: CancellationException) { throw cancelled }
                    catch (error: Exception) { saveError = error.userMessage(); validationRequest++ }
                    finally { saving = false }
                }
            })
        }

    }
    if (confirmDiscard) FlightFormDiscardDialog(stringResource(R.string.operations_discard_title),
        onDismiss = { confirmDiscard = false }, onDiscard = { mediaSession.clear(); onClose() })
    if (endReviewJson != null) FlightSubmissionConfirmation(stringResource(R.string.work_review_end_title), stringResource(R.string.work_review_end_message),
        stringResource(R.string.task_review_use_recorded_end), onConfirm = { endReviewJson?.let { applyReview(Json.decodeFromString(it), true) } },
        onDismiss = { endReviewJson = null })
}
