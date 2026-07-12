package com.nags.operations.ui.workorder

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.nags.operations.data.TaskTypeKind
import com.nags.operations.data.WorkOrderStatusKind
import com.nags.operations.data.db.entities.AircraftTypeEntity
import com.nags.operations.data.db.entities.CustomerEntity
import com.nags.operations.data.db.entities.EmployeeEntity
import com.nags.operations.data.db.entities.GeneralSupportEntity
import com.nags.operations.data.db.entities.MaterialEntity
import com.nags.operations.data.db.entities.ServiceEntity
import com.nags.operations.data.db.entities.ToolEntity
import com.nags.operations.data.TokenStore
import com.nags.operations.data.db.entities.WorkOrderOutboxEntity
import com.nags.operations.data.outbox.EnqueueAttachment
import com.nags.operations.data.outbox.EnqueueRequest
import com.nags.operations.data.outbox.OutboxPayload
import com.nags.operations.data.outbox.WorkOrderOutboxRepository
import com.nags.operations.data.repo.CatalogsRepository
import com.nags.operations.data.repo.EmployeesRepository
import com.nags.operations.data.repo.FlightsRepository
import com.nags.operations.data.repo.WorkOrderDraftJson
import com.nags.operations.data.repo.WorkOrderDraftsRepository
import com.nags.operations.data.repo.WorkOrderFlightRow
import com.nags.operations.data.db.entities.WorkOrderDraftEntity
import com.nags.operations.ui.util.normalizeWorkOrderAircraftTailInput
import com.nags.operations.ui.util.normalizeWorkOrderFlightNumberInput
import com.nags.operations.ui.util.parseOffsetDateTime
import java.time.OffsetDateTime
import java.time.ZoneOffset
import java.util.UUID
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import kotlinx.serialization.Serializable

sealed interface WorkOrderFlightLoadState {
    data object Loading : WorkOrderFlightLoadState
    data class Error(val message: String) : WorkOrderFlightLoadState
    data object Ready : WorkOrderFlightLoadState
}

sealed interface CreateWorkOrderLaunchMode {
    data class FromFlight(val flightId: String) : CreateWorkOrderLaunchMode
    data class FromDraft(val draftId: String) : CreateWorkOrderLaunchMode
    /** Create an Ad Hoc flight and work order from scratch; no server create yet — local UI + drafts only. */
    data object AdHocScratch : CreateWorkOrderLaunchMode
}

@Serializable
data class ServiceLineFormRow(
    val localKey: Long,
    val serviceId: String? = null,
    /** StaffMember id of the performer. */
    val employeeId: String? = null,
    val fromIso: String = "",
    val toIso: String = "",
    val description: String = "",
    /** True when the line was added on the RTR screen (client-side tag only). */
    val returnToRamp: Boolean = false,
)

/**
 * One task on the form. [serverId] carries the stable server task id when editing an existing
 * work order — resending it lets the server reconcile the task in place and keep its uploaded
 * attachments; new tasks leave it null.
 */
@Serializable
data class TaskFormRow(
    val localKey: Long,
    val serverId: String? = null,
    /** `Major` / `Minor` (`TaskType` enum names on the server). */
    val taskType: String = TaskTypeKind.Minor,
    val employeeIds: List<String> = emptyList(),
    val toolIds: List<String> = emptyList(),
    val materialIds: List<String> = emptyList(),
    val generalSupportIds: List<String> = emptyList(),
    val description: String = "",
    val fromIso: String = "",
    val toIso: String = "",
    val attachments: List<TaskAttachmentDraft> = emptyList(),
    /** Read-only names of attachments already uploaded on the server (edit mode). */
    val existingAttachmentNames: List<String> = emptyList(),
    val returnToRamp: Boolean = false,
)

@Serializable
data class CreateWorkOrderFormState(
    val flightNumber: String = "",
    /** Resolved aircraft type id from synced catalog; nullable until user picks or flight model matches. */
    val aircraftTypeId: String? = null,
    val aircraftTailNumber: String = "",
    val ataIso: String = "",
    val remarks: String = "",
    val serviceLines: List<ServiceLineFormRow> = emptyList(),
    val tasks: List<TaskFormRow> = emptyList(),
    /** Base64-encoded PNG of customer signature; optional until submit supports it. */
    val customerSignaturePng: String? = null,
)

/** Populated after a failed submit — cleared when the form edits ([updateForm]) or reapplied on next submit. */
data class ServiceLineSubmitFieldErrors(
    val serviceType: String? = null,
    val performer: String? = null,
    val from: String? = null,
    val to: String? = null,
)

data class TaskLineSubmitFieldErrors(
    val performers: String? = null,
    val from: String? = null,
    val to: String? = null,
)

data class CreateWorkOrderSubmitFieldErrors(
    val customer: String? = null,
    val aircraftType: String? = null,
    val ata: String? = null,
    /** Set when ATD is invalid vs ATA or work times (also shown under the submit ATD dialog). */
    val atd: String? = null,
    val serviceLinesByKey: Map<Long, ServiceLineSubmitFieldErrors> = emptyMap(),
    val tasksByKey: Map<Long, TaskLineSubmitFieldErrors> = emptyMap(),
)

data class CreateWorkOrderUiState(
    val flightLoad: WorkOrderFlightLoadState = WorkOrderFlightLoadState.Loading,
    val flight: WorkOrderFlightRow? = null,
    /** True when creating a new Ad Hoc flight + work order from the center nav action. */
    val isAdHocScratch: Boolean = false,
    val catalogCustomers: List<CustomerEntity> = emptyList(),
    val selectedCustomerId: String? = null,
    val catalogServices: List<ServiceEntity> = emptyList(),
    val catalogEmployees: List<EmployeeEntity> = emptyList(),
    val catalogTools: List<ToolEntity> = emptyList(),
    val catalogMaterials: List<MaterialEntity> = emptyList(),
    val catalogGeneralSupports: List<GeneralSupportEntity> = emptyList(),
    val catalogAircraftTypes: List<AircraftTypeEntity> = emptyList(),
    /** True when the flight row carried a cached under-review work order and the form was prefilled locally. */
    val isUpdatingCachedUnderReviewWorkOrder: Boolean = false,
    /** Preferred default for the performer when the synced catalog includes the signed-in employee from `/me`. */
    val loggedInEmployeeId: String? = null,
    /** When non-null, form is linked to a Row in `work_order_drafts` (new saves update the same row). */
    val activeDraftId: String? = null,
    /**
     * When true, the form shows **Save as draft** (new flight). When false and [activeDraftId] is set, **Update draft**.
     */
    val showSaveAsDraftButton: Boolean = true,
    val form: CreateWorkOrderFormState = CreateWorkOrderFormState(),
    /** Inline errors from the last submit; null when validation passed or the form was edited. */
    val submitFieldErrors: CreateWorkOrderSubmitFieldErrors? = null,
    /** Set when Submit validation passes (dry-run only for now); cleared after snackbar. */
    val submitValidationResult: SubmitValidationResult? = null,
)

sealed interface SubmitValidationResult {
    data object Passed : SubmitValidationResult
}

/**
 * Outcome of [CreateWorkOrderViewModel.enqueueSubmission]. [Enqueued] fires after
 * the outbox write completes on a background thread (the user may already have
 * navigated back). [Failed] can happen before navigation (prep) or after
 * (disk/Room) — use application-context UI (e.g. Toast) for errors.
 */
sealed interface SubmitOfflineResult {
    /** Successfully queued; the row will be drained when connectivity returns. */
    data class Enqueued(val clientMutationId: String) : SubmitOfflineResult

    /** Prep, disk-write, or Room error. */
    data class Failed(val message: String) : SubmitOfflineResult
}

class CreateWorkOrderViewModel(
    private val launchMode: CreateWorkOrderLaunchMode,
    private val flightsRepository: FlightsRepository,
    private val draftsRepository: WorkOrderDraftsRepository,
    private val outboxRepository: WorkOrderOutboxRepository,
    private val tokenStore: TokenStore,
    /**
     * Process scope for finishing the outbox write after the user navigates back.
     * [viewModelScope] is cancelled when this screen pops; disk + Room work must not be.
     */
    private val applicationScope: CoroutineScope,
    catalogsRepository: CatalogsRepository,
    private val employeesRepository: EmployeesRepository,
) : ViewModel() {

    /** Stable local id for draft + future “create ad hoc flight” API; not a server flight id yet. */
    private val adHocScratchFlightId: String = UUID.randomUUID().toString()

    /**
     * ATD confirmed in the submit dialog only — not stored in [CreateWorkOrderFormState] or drafts;
     * intended for the future submit-work-order API call. Cleared when the form is edited.
     */
    private var pendingSubmitAtdIso: String? = null

    private val _state = MutableStateFlow(CreateWorkOrderUiState())
    val state: StateFlow<CreateWorkOrderUiState> = _state.asStateFlow()

    private var nextLocalKey = 1L

    private fun allocKey(): Long = nextLocalKey++

    init {
        viewModelScope.launch {
            hydrateLoggedInEmployeeFromCache()
        }
        viewModelScope.launch {
            catalogsRepository.servicesFlow().collect { list ->
                _state.update { it.copy(catalogServices = list) }
            }
        }
        viewModelScope.launch {
            employeesRepository.observe().collect { list ->
                _state.update { s -> s.copy(catalogEmployees = list) }
                applyDefaultPerformersAcrossForm(_state.value)
            }
        }
        viewModelScope.launch {
            catalogsRepository.toolsFlow().collect { list ->
                _state.update { it.copy(catalogTools = list) }
            }
        }
        viewModelScope.launch {
            catalogsRepository.materialsFlow().collect { list ->
                _state.update { it.copy(catalogMaterials = list) }
            }
        }
        viewModelScope.launch {
            catalogsRepository.generalSupportsFlow().collect { list ->
                _state.update { it.copy(catalogGeneralSupports = list) }
            }
        }
        viewModelScope.launch {
            catalogsRepository.customersFlow().collect { list ->
                _state.update { it.copy(catalogCustomers = list) }
            }
        }
        viewModelScope.launch {
            catalogsRepository.aircraftTypesFlow().collect { list ->
                _state.update { s ->
                    val mergedId = s.form.aircraftTypeId
                        ?: resolveAircraftTypeIdFromFlightModel(s.flight?.aircraftTypeModel, list)
                    val nextForm = s.form.copy(aircraftTypeId = s.form.aircraftTypeId ?: mergedId)
                    val touched = nextForm != s.form
                    s.copy(
                        catalogAircraftTypes = list,
                        form = nextForm,
                        submitFieldErrors = if (touched) null else s.submitFieldErrors,
                    )
                }
            }
        }
        viewModelScope.launch {
            when (val mode = launchMode) {
                is CreateWorkOrderLaunchMode.FromFlight -> loadFlight(mode.flightId)
                is CreateWorkOrderLaunchMode.FromDraft -> loadDraft(mode.draftId)
                CreateWorkOrderLaunchMode.AdHocScratch -> loadAdHocScratch()
            }
        }
    }

    fun retryLoadFlight() {
        viewModelScope.launch {
            when (val mode = launchMode) {
                is CreateWorkOrderLaunchMode.FromFlight -> loadFlight(mode.flightId)
                is CreateWorkOrderLaunchMode.FromDraft -> loadDraft(mode.draftId)
                CreateWorkOrderLaunchMode.AdHocScratch -> loadAdHocScratch()
            }
        }
    }

    fun selectCustomer(customerId: String) {
        val customer = _state.value.catalogCustomers.firstOrNull { it.customerId == customerId } ?: return
        _state.update { s ->
            val f = s.flight ?: return@update s
            pendingSubmitAtdIso = null
            s.copy(
                selectedCustomerId = customerId,
                flight = f.copy(customerName = customer.name, customerIataCode = customer.iataCode),
                submitFieldErrors = null,
            )
        }
    }

    /** Planned services seeded into the form, excluding the Per-Landing designation. */
    private fun seededServiceLines(row: WorkOrderFlightRow): List<ServiceLineFormRow> {
        // Per-Landing flights start with zero seeded lines — Per Landing is a flight
        // designation, never a performable service line (mirrors the portal's NewModel()).
        if (row.isPerLanding) return emptyList()
        return row.plannedServices
            .filterNot { it.isAircraftPerLanding }
            .map { svc ->
                ServiceLineFormRow(
                    localKey = allocKey(),
                    serviceId = svc.serviceId,
                    fromIso = row.sta,
                    toIso = row.std,
                )
            }
    }

    /**
     * Persists current form + flight snapshot to Room. Subsequent saves reuse [CreateWorkOrderUiState.activeDraftId].
     */
    fun saveDraft(onFinished: (success: Boolean, message: String) -> Unit) {
        viewModelScope.launch {
            val snapshot = _state.value
            val flight = snapshot.flight
            if (flight == null || snapshot.flightLoad != WorkOrderFlightLoadState.Ready) {
                onFinished(false, "Finish loading before saving a draft.")
                return@launch
            }
            try {
                val draftId = snapshot.activeDraftId ?: UUID.randomUUID().toString()
                val normalizedForm = normalizedFormIdentifiers(snapshot.form)
                val normalizedFn = normalizeWorkOrderFlightNumberInput(normalizedForm.flightNumber)
                    .ifBlank { normalizeWorkOrderFlightNumberInput(flight.flightNumber) }
                val entity = WorkOrderDraftEntity(
                    draftId = draftId,
                    flightId = flight.id,
                    flightNumber = normalizedFn,
                    customerName = flight.customerName,
                    staIso = flight.sta,
                    stationCode = flight.stationIata,
                    flightJson = WorkOrderDraftJson.encodeFlight(flight),
                    formJson = WorkOrderDraftJson.encodeForm(normalizedForm),
                    updatedAtEpochMs = System.currentTimeMillis(),
                )
                draftsRepository.upsertDraft(entity)
                _state.update { it.copy(activeDraftId = draftId, form = normalizedForm) }
                onFinished(true, if (snapshot.activeDraftId == null) "Draft saved locally." else "Draft updated.")
            } catch (_: Exception) {
                onFinished(false, "Could not save draft.")
            }
        }
    }

    private suspend fun loadFlight(flightId: String) {
        _state.update {
            it.copy(
                flightLoad = WorkOrderFlightLoadState.Loading,
                flight = null,
                activeDraftId = null,
                isAdHocScratch = false,
                isUpdatingCachedUnderReviewWorkOrder = false,
                selectedCustomerId = null,
            )
        }
        val row = flightsRepository.findWorkOrderFlight(flightId)
        if (row == null) {
            _state.update {
                it.copy(
                    flightLoad = WorkOrderFlightLoadState.Error(
                        "Flight not found in the offline cache. Open the flight list and refresh, then try again.",
                    ),
                )
            }
            return
        }
        val wo = row.cachedMyWorkOrder
        val underReview =
            wo != null && WorkOrderStatusKind.fromWire(wo.status)?.isEditable == true

        val presetTypeId = row.aircraftTypeId

        val formBase = if (underReview) {
            wo!!.toPrefilledCreateFormState(::allocKey)
        } else {
            // Planned services copied into seeded lines (skip Per-Landing); the user completes
            // each line's performer or removes it, and may add extra lines.
            CreateWorkOrderFormState(
                flightNumber = row.flightNumber,
                aircraftTypeId = presetTypeId,
                serviceLines = seededServiceLines(row),
                tasks = emptyList(),
            )
        }
        val formNormalized = normalizedFormIdentifiers(formBase)
        if (underReview) {
            reconcileNextLocalKeyFromForm(formNormalized)
        }
        _state.update {
            it.copy(
                flightLoad = WorkOrderFlightLoadState.Ready,
                flight = row,
                activeDraftId = null,
                showSaveAsDraftButton = true,
                isAdHocScratch = false,
                isUpdatingCachedUnderReviewWorkOrder = underReview,
                selectedCustomerId = null,
                form = formNormalized,
                submitFieldErrors = null,
                submitValidationResult = null,
            )
        }
        applyDefaultPerformersAcrossForm(_state.value)
    }

    private suspend fun loadAdHocScratch() {
        val employees = employeesRepository.snapshot()
        val employeeId = tokenStore.getEmployeeId()
        val stationCode = tokenStore.getStationCode()?.trim().orEmpty()
        val now = OffsetDateTime.now(ZoneOffset.UTC).toString()
        val row = WorkOrderFlightRow(
            id = adHocScratchFlightId,
            flightNumber = "",
            operationTypeName = "Ad Hoc",
            sta = now,
            std = now,
            aircraftTypeId = null,
            aircraftTypeModel = null,
            customerName = "",
            customerIataCode = null,
            stationIata = stationCode,
            isPerLanding = false,
            isAdHoc = true,
            plannedServices = emptyList(),
        )
        _state.update {
            it.copy(
                flightLoad = WorkOrderFlightLoadState.Ready,
                flight = row,
                activeDraftId = null,
                showSaveAsDraftButton = true,
                isAdHocScratch = true,
                isUpdatingCachedUnderReviewWorkOrder = false,
                selectedCustomerId = null,
                loggedInEmployeeId = employeeId ?: it.loggedInEmployeeId,
                catalogEmployees = employees.ifEmpty { it.catalogEmployees },
                form = normalizedFormIdentifiers(
                    CreateWorkOrderFormState(
                        flightNumber = "",
                        aircraftTypeId = null,
                        serviceLines = emptyList(),
                        tasks = emptyList(),
                    ),
                ),
                submitFieldErrors = null,
                submitValidationResult = null,
            )
        }
        applyDefaultPerformersAcrossForm(_state.value)
    }

    private suspend fun hydrateLoggedInEmployeeFromCache() {
        val employeeId = tokenStore.getEmployeeId()
        val stationCode = tokenStore.getStationCode()?.trim().orEmpty()
        _state.update { s ->
            var next = if (employeeId != null && s.loggedInEmployeeId.isNullOrBlank()) {
                s.copy(loggedInEmployeeId = employeeId)
            } else {
                s
            }
            if (s.isAdHocScratch && s.flight != null && s.flight.stationIata.isBlank() && stationCode.isNotBlank()) {
                next = next.copy(flight = s.flight.copy(stationIata = stationCode))
            }
            next
        }
        applyDefaultPerformersAcrossForm(_state.value)
    }

    private suspend fun loadDraft(draftId: String) {
        _state.update {
            it.copy(
                flightLoad = WorkOrderFlightLoadState.Loading,
                flight = null,
                activeDraftId = null,
                isAdHocScratch = false,
                isUpdatingCachedUnderReviewWorkOrder = false,
                selectedCustomerId = null,
            )
        }
        val entity = draftsRepository.getDraft(draftId)
        if (entity == null) {
            _state.update {
                it.copy(
                    flightLoad = WorkOrderFlightLoadState.Error(
                        "Draft not found. It may have been removed.",
                    ),
                )
            }
            return
        }
        val flight = try {
            WorkOrderDraftJson.decodeFlight(entity.flightJson)
        } catch (_: Exception) {
            _state.update {
                it.copy(
                    flightLoad = WorkOrderFlightLoadState.Error(
                        "Could not read saved flight data.",
                    ),
                )
            }
            return
        }
        val form = try {
            WorkOrderDraftJson.decodeForm(entity.formJson)
        } catch (_: Exception) {
            _state.update {
                it.copy(
                    flightLoad = WorkOrderFlightLoadState.Error(
                        "Could not read saved draft form.",
                    ),
                )
            }
            return
        }
        val formNormalized = normalizedFormIdentifiers(form)
        reconcileNextLocalKeyFromForm(formNormalized)
        _state.update {
            it.copy(
                flightLoad = WorkOrderFlightLoadState.Ready,
                flight = flight,
                form = formNormalized,
                activeDraftId = draftId,
                showSaveAsDraftButton = false,
                isAdHocScratch = false,
                isUpdatingCachedUnderReviewWorkOrder = false,
                selectedCustomerId = null,
                submitFieldErrors = null,
                submitValidationResult = null,
            )
        }
        applyDefaultPerformersAcrossForm(_state.value)
    }

    private fun reconcileNextLocalKeyFromForm(form: CreateWorkOrderFormState) {
        val keys = form.serviceLines.map { it.localKey } + form.tasks.map { it.localKey }
        val maxKey = keys.maxOrNull() ?: 0L
        nextLocalKey = maxOf(maxKey + 1, nextLocalKey)
    }

    /**
     * When `/me` and the synced roster agree:
     * - Service lines still on a single performer get that default once (null employeeId only).
     * - Tasks with no performers yet get a one-person default (`employeeIds` empty only).
     * Never overwrites explicit user edits.
     */
    private fun applyDefaultPerformersAcrossForm(snapshot: CreateWorkOrderUiState) {
        val presetId = resolvedDefaultPerformingEmployeeId(snapshot) ?: return
        _state.update { s ->
            val newLines = s.form.serviceLines.map { line ->
                if (line.employeeId == null) line.copy(employeeId = presetId) else line
            }
            val newTasks = s.form.tasks.map { task ->
                if (task.employeeIds.isEmpty()) task.copy(employeeIds = listOf(presetId)) else task
            }
            val linesChanged = newLines != s.form.serviceLines
            val tasksChanged = newTasks != s.form.tasks
            if (!linesChanged && !tasksChanged) return@update s
            s.copy(
                form = s.form.copy(serviceLines = newLines, tasks = newTasks),
                submitFieldErrors = null,
            )
        }
    }

    private fun resolvedDefaultPerformingEmployeeId(snapshot: CreateWorkOrderUiState): String? {
        val id = snapshot.loggedInEmployeeId ?: return null
        if (snapshot.catalogEmployees.none { it.staffMemberId == id }) return null
        return id
    }

    /** Same performer defaulting as `/me` plus synced catalog — used when adding rows. */
    private fun defaultSingleEmployeeIdFromState(): String? = resolvedDefaultPerformingEmployeeId(_state.value)

    private fun defaultEmployeeIdsFromState(): List<String> =
        defaultSingleEmployeeIdFromState()?.let { listOf(it) } ?: emptyList()

    fun updateForm(transform: (CreateWorkOrderFormState) -> CreateWorkOrderFormState) {
        _state.update { s ->
            val next = transform(s.form)
            if (next == s.form) s
            else {
                pendingSubmitAtdIso = null
                s.copy(form = next, submitFieldErrors = null)
            }
        }
    }

    fun updateFlightNumber(raw: String) {
        val normalized = normalizeWorkOrderFlightNumberInput(raw)
        _state.update { s ->
            val nextForm = s.form.copy(flightNumber = normalized)
            if (nextForm == s.form && (!s.isAdHocScratch || s.flight?.flightNumber == normalized)) {
                return@update s
            }
            pendingSubmitAtdIso = null
            val nextFlight = if (s.isAdHocScratch) s.flight?.copy(flightNumber = normalized) else s.flight
            s.copy(form = nextForm, flight = nextFlight, submitFieldErrors = null)
        }
    }

    fun updateAircraftTailNumber(raw: String) {
        updateForm { it.copy(aircraftTailNumber = normalizeWorkOrderAircraftTailInput(raw)) }
    }

    fun setAircraftType(id: String?) {
        updateForm { it.copy(aircraftTypeId = id) }
    }

    fun addServiceLine() {
        val preset = defaultSingleEmployeeIdFromState()
        updateForm {
            it.copy(
                serviceLines = it.serviceLines + ServiceLineFormRow(localKey = allocKey(), employeeId = preset),
            )
        }
    }

    fun removeServiceLine(localKey: Long) {
        updateForm { f ->
            f.copy(serviceLines = f.serviceLines.filterNot { it.localKey == localKey })
        }
    }

    fun replaceServiceLine(row: ServiceLineFormRow) {
        updateForm { f ->
            f.copy(serviceLines = f.serviceLines.map { if (it.localKey == row.localKey) row else it })
        }
    }

    fun addTask() {
        val presetEmployees = defaultEmployeeIdsFromState()
        updateForm {
            it.copy(
                tasks = it.tasks + TaskFormRow(localKey = allocKey(), employeeIds = presetEmployees),
            )
        }
    }

    fun removeTask(localKey: Long) {
        updateForm { f ->
            f.copy(tasks = f.tasks.filterNot { it.localKey == localKey })
        }
    }

    fun replaceTask(row: TaskFormRow) {
        updateForm { f ->
            f.copy(tasks = f.tasks.map { if (it.localKey == row.localKey) row else it })
        }
    }

    /**
     * Validates the form against the ATD chosen in the submit dialog. Returns
     * the field-level errors when something is off; returns `null` when the
     * form is valid and ready to be queued via [enqueueSubmission].
     *
     * Doesn't enqueue itself — keeps the dialog flow honest: the screen sees
     * `null`, closes the dialog, then triggers the suspending enqueue. The
     * brief gap between "validated" and "enqueued" is fine because the form
     * is frozen for the duration of the modal.
     */
    fun confirmSubmitWithAtd(atdIso: String?): CreateWorkOrderSubmitFieldErrors? {
        val f = normalizedFormIdentifiers(_state.value.form)
        val snap = _state.value
        val errors = computeSubmitErrors(
            f,
            atdIso,
            isAdHocScratch = snap.isAdHocScratch,
            selectedCustomerId = snap.selectedCustomerId,
        )
        if (errors == null) {
            pendingSubmitAtdIso = atdIso?.trim()?.takeIf { it.isNotEmpty() }
            _state.update { it.copy(submitFieldErrors = null) }
            return null
        }
        _state.update { it.copy(submitFieldErrors = errors, submitValidationResult = null) }
        return errors
    }

    /**
     * Queues the validated work-order submission for offline delivery.
     *
     * Navigation is intentionally **before** disk/Room work: copying attachments
     * and inserting the outbox row can take hundreds of ms; the user should pop
     * back immediately. The heavy work runs on [applicationScope] + [Dispatchers.IO]
     * so it is not cancelled when this [ViewModel] is cleared on pop.
     *
     * Call only after [confirmSubmitWithAtd] returns `null`.
     *
     * @param onInstantNavigate invoked synchronously on the caller thread once
     *        the in-memory [EnqueueRequest] is built — use this to `popBackStack`.
     * @param onFinished called on the main thread after the background write
     *        succeeds or fails. For [SubmitOfflineResult.Enqueued] the list’s
     *        Flow already shows the chip; a snackbar is optional. For [Failed],
     *        show a toast with application context (the create screen may be gone).
     */
    fun enqueueSubmission(
        flightKindOverride: Int? = null,
        onInstantNavigate: () -> Unit,
        onFinished: (SubmitOfflineResult) -> Unit,
    ) {
        val snapshot = _state.value
        val flight = snapshot.flight
        if (flight == null || snapshot.flightLoad != WorkOrderFlightLoadState.Ready) {
            onFinished(SubmitOfflineResult.Failed("Finish loading the flight before submitting."))
            return
        }

        val mutationId = UUID.randomUUID().toString()
        val isScratch = snapshot.isAdHocScratch
        val flightKind = flightKindOverride ?: resolveFlightKind(snapshot, flight)
        val isUpdateExisting = snapshot.isUpdatingCachedUnderReviewWorkOrder && !isScratch
        val knownServerWorkOrderId = if (isUpdateExisting) {
            flight.cachedMyWorkOrder?.id
                ?: run {
                    onFinished(
                        SubmitOfflineResult.Failed(
                            "Could not resolve the work order on this device — try again when the flight has synced.",
                        ),
                    )
                    return
                }
        } else {
            null
        }
        val payload = try {
            buildOutboxPayload(snapshot, flight, isScratch, isUpdateExisting)
        } catch (e: IllegalStateException) {
            onFinished(SubmitOfflineResult.Failed(e.message ?: "Could not prepare submission."))
            return
        }
        val attachmentsToPersist = collectAttachmentsInTaskOrder(snapshot.form)
        val request = EnqueueRequest(
            clientMutationId = mutationId,
            flightId = flight.id,
            flightKind = flightKind,
            clientFlightId = if (isScratch) flight.id else null,
            payload = payload,
            attachmentsToPersist = attachmentsToPersist,
            knownServerWorkOrderId = knownServerWorkOrderId,
        )
        val draftId = snapshot.activeDraftId

        pendingSubmitAtdIso = null
        onInstantNavigate()

        applicationScope.launch(Dispatchers.IO) {
            try {
                outboxRepository.enqueue(request)
                draftId?.let { id -> runCatching { draftsRepository.deleteDraft(id) } }
                withContext(Dispatchers.Main.immediate) {
                    onFinished(SubmitOfflineResult.Enqueued(clientMutationId = mutationId))
                }
            } catch (e: Exception) {
                withContext(Dispatchers.Main.immediate) {
                    onFinished(
                        SubmitOfflineResult.Failed(
                            "Could not save your submission: ${e.message ?: e.javaClass.simpleName}",
                        ),
                    )
                }
            }
        }
    }

    private fun resolveFlightKind(snapshot: CreateWorkOrderUiState, flight: WorkOrderFlightRow): Int {
        if (snapshot.isAdHocScratch) return WorkOrderOutboxEntity.FLIGHT_KIND_AD_HOC_SCRATCH
        return when {
            flight.isPerLanding -> WorkOrderOutboxEntity.FLIGHT_KIND_PER_LANDING
            flight.isAdHoc -> WorkOrderOutboxEntity.FLIGHT_KIND_AD_HOC
            else -> WorkOrderOutboxEntity.FLIGHT_KIND_MY
        }
    }

    private fun buildOutboxPayload(
        snapshot: CreateWorkOrderUiState,
        flight: WorkOrderFlightRow,
        isScratch: Boolean,
        isUpdateExisting: Boolean,
    ): OutboxPayload {
        val form = normalizedFormIdentifiers(snapshot.form)
        val atdIso = pendingSubmitAtdIso
        val ataIso = form.ataIso.trim().takeIf { it.isNotEmpty() }

        val workOrder = OutboxPayload.WorkOrderInput(
            // The mobile create/update form always authors a Completion work order;
            // cancellations flow through the dedicated cancel path.
            type = "Completion",
            actualFlightNumber = form.flightNumber.ifBlank { flight.flightNumber },
            aircraftTypeId = form.aircraftTypeId,
            aircraftTailNumber = form.aircraftTailNumber.takeIf { it.isNotBlank() },
            ataIso = ataIso,
            atdIso = atdIso,
            remarks = form.remarks.takeIf { it.isNotBlank() },
            serviceLines = form.serviceLines.map { row ->
                OutboxPayload.ServiceLineInput(
                    serviceId = row.serviceId
                        ?: error("Service line missing serviceId — validation should have caught this"),
                    performedByStaffMemberId = row.employeeId
                        ?: error("Service line missing performer — validation should have caught this"),
                    fromIso = row.fromIso,
                    toIso = row.toIso,
                    description = row.description.takeIf { it.isNotBlank() },
                )
            },
            // attachments inside each task are replaced by the outbox repository with
            // the persisted file-path versions; we ship empty here as a placeholder.
            tasks = form.tasks.map { task ->
                OutboxPayload.TaskInput(
                    id = task.serverId,
                    taskType = task.taskType,
                    description = task.description.takeIf { it.isNotBlank() },
                    fromIso = task.fromIso,
                    toIso = task.toIso,
                    employeeIds = task.employeeIds,
                    tools = task.toolIds.map { OutboxPayload.ResourceInput(it) },
                    materials = task.materialIds.map { OutboxPayload.ResourceInput(it) },
                    generalSupports = task.generalSupportIds.map { OutboxPayload.ResourceInput(it) },
                    // Each task carries `task.attachments.size` slots; the repository
                    // re-stitches them after writing the durable copies to disk.
                    attachments = List(task.attachments.size) { idx ->
                        OutboxPayload.AttachmentInput(
                            relativePath = "",
                            kind = task.attachments[idx].kind,
                            contentType = task.attachments[idx].contentType,
                            fileName = task.attachments[idx].fileName,
                            capturedAtIso = task.attachments[idx].capturedAtIso,
                            sizeBytes = task.attachments[idx].sizeBytes,
                        )
                    },
                )
            },
            customerSignaturePngBase64 = form.customerSignaturePng,
        )

        val scratch = if (isScratch) {
            val customerId = snapshot.selectedCustomerId
                ?: error("Ad-hoc-scratch submission missing customer — validation should have caught this")
            OutboxPayload.ScratchFlightInput(
                customerId = customerId,
                flightNumber = form.flightNumber.ifBlank { flight.flightNumber },
                staIso = flight.sta,
                stdIso = flight.std,
                aircraftTypeId = form.aircraftTypeId,
                plannedServiceIds = emptyList(),
            )
        } else null

        return OutboxPayload(
            kind = when {
                isScratch -> OutboxPayload.Kind.ScratchAdHoc
                isUpdateExisting -> OutboxPayload.Kind.UpdateExisting
                else -> OutboxPayload.Kind.ForFlight
            },
            workOrder = workOrder,
            scratchFlight = scratch,
        )
    }

    private fun collectAttachmentsInTaskOrder(form: CreateWorkOrderFormState): List<EnqueueAttachment> =
        form.tasks.flatMap { task ->
            task.attachments.map { a ->
                EnqueueAttachment(
                    base64 = a.base64,
                    kind = a.kind,
                    contentType = a.contentType,
                    fileName = a.fileName,
                    capturedAtIso = a.capturedAtIso,
                    sizeBytes = a.sizeBytes,
                )
            }
        }

    fun clearAtdSubmitError() {
        _state.update { s ->
            val e = s.submitFieldErrors ?: return@update s
            val next = e.copy(atd = null)
            s.copy(submitFieldErrors = if (isBlankSubmitErrors(next)) null else next)
        }
    }

    fun clearSubmitValidationResult() {
        _state.update { it.copy(submitValidationResult = null) }
    }

    /**
     * Validates create-work-order input. On success, the UI shows the ATD confirmation dialog.
     */
    fun submitDryRunValidate() {
        _state.update { s ->
            val nextForm = normalizedFormIdentifiers(s.form)
            if (nextForm == s.form) {
                s
            } else {
                s.copy(form = nextForm, submitFieldErrors = null)
            }
        }
        val f = _state.value.form
        val snap = _state.value
        val errors = computeSubmitErrors(
            f,
            dialogAtdIso = null,
            isAdHocScratch = snap.isAdHocScratch,
            selectedCustomerId = snap.selectedCustomerId,
        )
        val hasProblems = errors != null
        _state.update {
            it.copy(
                submitFieldErrors = errors,
                submitValidationResult = if (hasProblems) null else SubmitValidationResult.Passed,
            )
        }
    }

    private companion object {
        fun isBlankSubmitErrors(e: CreateWorkOrderSubmitFieldErrors): Boolean =
            e.customer == null && e.aircraftType == null && e.ata == null && e.atd == null &&
                e.serviceLinesByKey.isEmpty() && e.tasksByKey.isEmpty()

        fun mergeMsg(a: String?, b: String): String =
            when {
                a.isNullOrBlank() -> b
                a.contains(b) -> a
                else -> "$a\n$b"
            }

        fun safeParseOffset(iso: String): OffsetDateTime? =
            runCatching { parseOffsetDateTime(iso) }.getOrNull()

        fun computeSubmitErrors(
            form: CreateWorkOrderFormState,
            dialogAtdIso: String?,
            isAdHocScratch: Boolean,
            selectedCustomerId: String?,
        ): CreateWorkOrderSubmitFieldErrors? {
            val customerErr =
                if (isAdHocScratch && selectedCustomerId.isNullOrBlank()) "Customer is required." else null
            val rawAtd = dialogAtdIso?.trim().orEmpty()
            val aircraftErr = if (form.aircraftTypeId.isNullOrBlank()) "Aircraft type is required." else null
            var ataErr = if (form.ataIso.isBlank()) "ATA is required." else null
            val ataDt = form.ataIso.takeIf { it.isNotBlank() }?.let { safeParseOffset(it) }
            if (ataErr == null && form.ataIso.isNotBlank() && ataDt == null) {
                ataErr = "Invalid ATA date or time."
            }

            var atdErr: String? = null
            val atdDt = rawAtd.takeIf { it.isNotBlank() }?.let { safeParseOffset(it) }
            if (rawAtd.isNotBlank() && atdDt == null) {
                atdErr = "Invalid ATD date or time."
            }
            if (atdErr == null && ataDt != null && atdDt != null && atdDt.isBefore(ataDt)) {
                atdErr = "Departure (ATD) can't be before arrival (ATA)."
            }

            val serviceMap = LinkedHashMap<Long, ServiceLineSubmitFieldErrors>()
            form.serviceLines.forEach { row ->
                var st = if (row.serviceId.isNullOrBlank()) "Service type is required." else null
                var perf = if (row.employeeId.isNullOrBlank()) "Performed by is required." else null
                var fromE = if (row.fromIso.isBlank()) "From date and time is required." else null
                var toE = if (row.toIso.isBlank()) "To date and time is required." else null

                val fromDt = row.fromIso.takeIf { it.isNotBlank() }?.let { safeParseOffset(it) }
                val toDt = row.toIso.takeIf { it.isNotBlank() }?.let { safeParseOffset(it) }
                if (row.fromIso.isNotBlank() && fromDt == null) {
                    fromE = mergeMsg(fromE, "Invalid From date or time.")
                }
                if (row.toIso.isNotBlank() && toDt == null) {
                    toE = mergeMsg(toE, "Invalid To date or time.")
                }

                if (fromDt != null && toDt != null && toDt.isBefore(fromDt)) {
                    toE = mergeMsg(toE, "Must be on or after From.")
                }
                if (ataDt != null && fromDt != null && fromDt.isBefore(ataDt)) {
                    fromE = mergeMsg(fromE, "Can't be before actual arrival (ATA).")
                }
                if (ataDt != null && fromDt != null && ataDt.isAfter(fromDt)) {
                    ataErr = mergeMsg(ataErr, "Can't be after a service line start time.")
                }
                if (atdDt != null && toDt != null && toDt.isAfter(atdDt)) {
                    toE = mergeMsg(toE, "Can't be after departure (ATD).")
                }

                if (st != null || perf != null || fromE != null || toE != null) {
                    serviceMap[row.localKey] = ServiceLineSubmitFieldErrors(st, perf, fromE, toE)
                }
            }

            val taskMap = LinkedHashMap<Long, TaskLineSubmitFieldErrors>()
            form.tasks.forEach { row ->
                var performers = if (row.employeeIds.isEmpty()) "Choose at least one person." else null
                var fromE = if (row.fromIso.isBlank()) "From date and time is required." else null
                var toE = if (row.toIso.isBlank()) "To date and time is required." else null

                val fromDt = row.fromIso.takeIf { it.isNotBlank() }?.let { safeParseOffset(it) }
                val toDt = row.toIso.takeIf { it.isNotBlank() }?.let { safeParseOffset(it) }
                if (row.fromIso.isNotBlank() && fromDt == null) {
                    fromE = mergeMsg(fromE, "Invalid From date or time.")
                }
                if (row.toIso.isNotBlank() && toDt == null) {
                    toE = mergeMsg(toE, "Invalid To date or time.")
                }

                if (fromDt != null && toDt != null && toDt.isBefore(fromDt)) {
                    toE = mergeMsg(toE, "Must be on or after From.")
                }
                if (ataDt != null && fromDt != null && fromDt.isBefore(ataDt)) {
                    fromE = mergeMsg(fromE, "Can't be before actual arrival (ATA).")
                }
                if (ataDt != null && fromDt != null && ataDt.isAfter(fromDt)) {
                    ataErr = mergeMsg(ataErr, "Can't be after a task start time.")
                }
                if (atdDt != null && toDt != null && toDt.isAfter(atdDt)) {
                    toE = mergeMsg(toE, "Can't be after departure (ATD).")
                }

                if (performers != null || fromE != null || toE != null) {
                    taskMap[row.localKey] = TaskLineSubmitFieldErrors(performers, fromE, toE)
                }
            }

            val hasProblems = customerErr != null || aircraftErr != null || ataErr != null || atdErr != null ||
                serviceMap.isNotEmpty() || taskMap.isNotEmpty()
            return if (!hasProblems) {
                null
            } else {
                CreateWorkOrderSubmitFieldErrors(
                    customer = customerErr,
                    aircraftType = aircraftErr,
                    ata = ataErr,
                    atd = atdErr,
                    serviceLinesByKey = serviceMap,
                    tasksByKey = taskMap,
                )
            }
        }

        fun normalizedFormIdentifiers(form: CreateWorkOrderFormState): CreateWorkOrderFormState =
            form.copy(
                flightNumber = normalizeWorkOrderFlightNumberInput(form.flightNumber),
                aircraftTailNumber = normalizeWorkOrderAircraftTailInput(form.aircraftTailNumber),
            )

        fun resolveAircraftTypeIdFromFlightModel(
            model: String?,
            catalog: List<AircraftTypeEntity>,
        ): String? {
            val m = model?.trim()?.takeIf { it.isNotEmpty() } ?: return null
            catalog.firstOrNull { it.model.trim().equals(m, ignoreCase = true) }
                ?.let { return it.aircraftTypeId }
            catalog.firstOrNull { it.model.contains(m, ignoreCase = true) }
                ?.let { return it.aircraftTypeId }
            return null
        }
    }
}
