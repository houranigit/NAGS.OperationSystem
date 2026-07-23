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
import com.nags.operations.data.db.entities.allowedPerformedServiceIds
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
import java.time.Clock
import java.time.OffsetDateTime
import java.time.ZoneOffset
import java.time.temporal.ChronoUnit
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
    /** Stable server identity used to preserve server-owned provenance during an update. */
    val serverId: String? = null,
    val serviceId: String? = null,
    /** Preserves the display name when a saved service is later inactive or no longer allowed. */
    val serviceName: String? = null,
    /** StaffMember ids of everyone credited with performing this service. */
    val employeeIds: List<String> = emptyList(),
    val fromIso: String = "",
    val toIso: String = "",
    val description: String = "",
    val attachments: List<TaskAttachmentDraft> = emptyList(),
    /** Read-only names of attachments already uploaded on the server (edit mode). */
    val existingAttachmentNames: List<String> = emptyList(),
    /** True when the line originated from a return-to-ramp submission. */
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
    val taskType: String = TaskTypeKind.Major,
    val employeeIds: List<String> = emptyList(),
    val toolIds: List<String> = emptyList(),
    /** Quantity keyed by tool id. Missing keys from legacy drafts mean the portal default of 1. */
    val toolQuantities: Map<String, Double> = emptyMap(),
    val materialIds: List<String> = emptyList(),
    /** Quantity keyed by material id. Missing keys from legacy drafts mean the portal default of 1. */
    val materialQuantities: Map<String, Double> = emptyMap(),
    val generalSupportIds: List<String> = emptyList(),
    /** Quantity keyed by general-support id. Missing keys from legacy drafts mean the portal default of 1. */
    val generalSupportQuantities: Map<String, Double> = emptyMap(),
    val description: String = "",
    val fromIso: String = "",
    val toIso: String = "",
    val attachments: List<TaskAttachmentDraft> = emptyList(),
    /** Read-only names of attachments already uploaded on the server (edit mode). */
    val existingAttachmentNames: List<String> = emptyList(),
    /** True when the task originated from a return-to-ramp submission. */
    val returnToRamp: Boolean = false,
)

@Serializable
data class CreateWorkOrderFormState(
    val flightNumber: String = "",
    /** Resolved aircraft type id from synced catalog; nullable until user picks or flight model matches. */
    val aircraftTypeId: String? = null,
    val aircraftTailNumber: String = "",
    /** Scratch-flight schedule. Defaults keep pre-release draft JSON readable. */
    val scheduledArrivalIso: String = "",
    val scheduledDepartureIso: String = "",
    val ataIso: String = "",
    val atdIso: String = "",
    val remarks: String = "",
    val serviceLines: List<ServiceLineFormRow> = emptyList(),
    val tasks: List<TaskFormRow> = emptyList(),
    /** Base64-encoded PNG of customer signature; optional until submit supports it. */
    val customerSignaturePng: String? = null,
    /** Read-only signature name already stored by the server when editing. */
    val existingCustomerSignatureName: String? = null,
    /** Persisted routing context so a resumed draft uses the same endpoint as the original form. */
    val draftSubmissionMode: String = WorkOrderDraftSubmissionMode.Unknown,
    val draftCustomerId: String? = null,
    val draftWorkOrderId: String? = null,
    val draftWorkOrderRowVersion: String? = null,
    /** Persisted with drafts; zero identifies legacy forms that discarded service-line ids. */
    val serviceLineIdentityVersion: Int = 0,
)

/** Populated after a failed submit — cleared when the form edits ([updateForm]) or reapplied on next submit. */
data class ServiceLineSubmitFieldErrors(
    val serviceType: String? = null,
    val performer: String? = null,
    val from: String? = null,
    val to: String? = null,
    val description: String? = null,
    val attachments: String? = null,
)

data class TaskLineSubmitFieldErrors(
    val taskType: String? = null,
    val performers: String? = null,
    val from: String? = null,
    val to: String? = null,
    val description: String? = null,
    val tools: String? = null,
    val materials: String? = null,
    val generalSupports: String? = null,
    val attachments: String? = null,
)

data class CreateWorkOrderSubmitFieldErrors(
    val customer: String? = null,
    val flightNumber: String? = null,
    val aircraftType: String? = null,
    val aircraftTailNumber: String? = null,
    val scheduledArrival: String? = null,
    val scheduledDeparture: String? = null,
    val ata: String? = null,
    /** Set when ATD is invalid vs ATA or work times (also shown under the submit ATD dialog). */
    val atd: String? = null,
    val remarks: String? = null,
    val serviceLinesByKey: Map<Long, ServiceLineSubmitFieldErrors> = emptyMap(),
    val tasksByKey: Map<Long, TaskLineSubmitFieldErrors> = emptyMap(),
)

data class WorkOrderEditState(
    val revision: Long = 0L,
    val lastSavedRevision: Long = 0L,
) {
    val hasUnsavedChanges: Boolean
        get() = revision != lastSavedRevision

    internal fun afterEdit(): WorkOrderEditState = copy(revision = revision + 1L)

    /**
     * Marks only the snapshot that actually reached Room as saved. If the employee edited while
     * that write was in flight, [revision] stays ahead and the form remains dirty.
     */
    internal fun afterSuccessfulSave(snapshotRevision: Long): WorkOrderEditState =
        copy(lastSavedRevision = maxOf(lastSavedRevision, snapshotRevision))
}

enum class WorkOrderPersistenceState {
    Idle,
    SavingDraft,
    Submitting,
}

internal fun WorkOrderPersistenceState.canStartWrite(): Boolean =
    this == WorkOrderPersistenceState.Idle

internal fun stableDraftId(
    activeDraftId: String?,
    sessionDraftId: String?,
    createId: () -> String,
): String = activeDraftId ?: sessionDraftId ?: createId()

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
    /** Stable update target and base revision, persisted through local drafts. */
    val updatingWorkOrderId: String? = null,
    val updatingWorkOrderRowVersion: String? = null,
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
    val editState: WorkOrderEditState = WorkOrderEditState(),
    val persistenceState: WorkOrderPersistenceState = WorkOrderPersistenceState.Idle,
    val wizardStep: WorkOrderWizardStep = WorkOrderWizardStep.Flight,
    val isAtdDialogVisible: Boolean = false,
) {
    val hasUnsavedChanges: Boolean
        get() = editState.hasUnsavedChanges

    val isSavingDraft: Boolean
        get() = persistenceState == WorkOrderPersistenceState.SavingDraft

    val isSubmitting: Boolean
        get() = persistenceState == WorkOrderPersistenceState.Submitting
}

sealed interface SubmitValidationResult {
    data object Passed : SubmitValidationResult
}

sealed interface DraftSaveResult {
    val message: String

    data class Saved(
        override val message: String,
        /** False when a newer edit arrived after the saved snapshot was captured. */
        val isCurrent: Boolean,
    ) : DraftSaveResult

    data class Failed(override val message: String) : DraftSaveResult
}

/**
 * Outcome of [CreateWorkOrderViewModel.enqueueSubmission]. [Enqueued] fires after
 * the durable outbox write completes; only then does the screen navigate back.
 * [Failed] keeps the form open so the caller can surface the disk/Room error.
 */
sealed interface SubmitOfflineResult {
    /** Successfully queued; the row will be drained when connectivity returns. */
    data class Enqueued(val clientMutationId: String) : SubmitOfflineResult

    /** Prep, disk-write, or Room error. */
    data class Failed(val message: String) : SubmitOfflineResult
}

/**
 * Builds the service-line defaults for a new work order. Per-Landing flights intentionally start
 * empty so staff record only services that were actually performed; normal flights retain their
 * planned-service prefill.
 */
internal fun serviceLinesToPrefill(
    row: WorkOrderFlightRow,
    allowedPerformedServiceIds: Set<String>,
    nextKey: () -> Long,
): List<ServiceLineFormRow> {
    if (row.isPerLanding) return emptyList()

    return row.plannedServices
        .filter { !it.isAircraftPerLanding && it.serviceId in allowedPerformedServiceIds }
        .map { service ->
            ServiceLineFormRow(
                localKey = nextKey(),
                serviceId = service.serviceId,
                serviceName = service.name,
            )
        }
}

internal fun currentWorkOrderTimestamp(clock: Clock, flightAnchorIso: String?): String {
    val offset = flightAnchorIso
        ?.let { runCatching { parseOffsetDateTime(it).offset }.getOrNull() }
        ?: ZoneOffset.UTC
    return OffsetDateTime.now(clock)
        .withOffsetSameInstant(offset)
        .truncatedTo(ChronoUnit.MINUTES)
        .toString()
}

internal fun initializeBlankFromTimes(
    form: CreateWorkOrderFormState,
    step: WorkOrderWizardStep,
    timestampIso: String,
): CreateWorkOrderFormState = when (step) {
    WorkOrderWizardStep.ServiceLines -> form.copy(
        serviceLines = form.serviceLines.map { row ->
            if (row.fromIso.isBlank()) row.copy(fromIso = timestampIso) else row
        },
    )
    WorkOrderWizardStep.Tasks -> form.copy(
        tasks = form.tasks.map { row ->
            if (row.fromIso.isBlank()) row.copy(fromIso = timestampIso) else row
        },
    )
    WorkOrderWizardStep.Flight,
    WorkOrderWizardStep.Signature,
    -> form
}

internal fun finalizeBlankToTimes(
    form: CreateWorkOrderFormState,
    step: WorkOrderWizardStep,
    timestampIso: String,
): CreateWorkOrderFormState = when (step) {
    WorkOrderWizardStep.ServiceLines -> form.copy(
        serviceLines = form.serviceLines.map { row ->
            if (row.toIso.isBlank()) row.copy(toIso = timestampIso) else row
        },
    )
    WorkOrderWizardStep.Tasks -> form.copy(
        tasks = form.tasks.map { row ->
            if (row.toIso.isBlank()) row.copy(toIso = timestampIso) else row
        },
    )
    WorkOrderWizardStep.Flight,
    WorkOrderWizardStep.Signature,
    -> form
}

internal fun newServiceLineAt(
    localKey: Long,
    employeeIds: List<String>,
    timestampIso: String,
): ServiceLineFormRow = ServiceLineFormRow(
    localKey = localKey,
    employeeIds = employeeIds,
    fromIso = timestampIso,
    toIso = "",
)

internal fun newTaskAt(
    localKey: Long,
    employeeIds: List<String>,
    timestampIso: String,
): TaskFormRow = TaskFormRow(
    localKey = localKey,
    employeeIds = employeeIds,
    fromIso = timestampIso,
    toIso = "",
)

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
    private val catalogsRepository: CatalogsRepository,
    private val employeesRepository: EmployeesRepository,
    private val clock: Clock = Clock.systemUTC(),
) : ViewModel() {

    /** Stable local id for draft + future “create ad hoc flight” API; not a server flight id yet. */
    private val adHocScratchFlightId: String = UUID.randomUUID().toString()

    private val _state = MutableStateFlow(CreateWorkOrderUiState())
    val state: StateFlow<CreateWorkOrderUiState> = _state.asStateFlow()

    private var nextLocalKey = 1L
    private var workflowGeneration = 0L
    private var sessionDraftId: String? = null
    private val initializedFromSteps = mutableSetOf<WorkOrderWizardStep>()

    private fun allocKey(): Long = nextLocalKey++

    private fun currentTimestamp(): String =
        currentWorkOrderTimestamp(clock, _state.value.flight?.std)

    private fun resetEditBaseline(activeDraftId: String?) {
        workflowGeneration += 1
        sessionDraftId = activeDraftId
        initializedFromSteps.clear()
        _state.update {
            it.copy(
                activeDraftId = activeDraftId,
                editState = WorkOrderEditState(),
                persistenceState = WorkOrderPersistenceState.Idle,
                wizardStep = WorkOrderWizardStep.Flight,
                isAtdDialogVisible = false,
            )
        }
    }

    private fun updateEditedState(
        transform: (CreateWorkOrderUiState) -> CreateWorkOrderUiState,
    ) {
        _state.update { current ->
            if (current.isSubmitting) return@update current
            val next = transform(current)
            if (next == current) {
                current
            } else {
                next.copy(
                    editState = current.editState.afterEdit(),
                    submitFieldErrors = null,
                    submitValidationResult = null,
                )
            }
        }
    }

    internal fun onWizardStepEntered(step: WorkOrderWizardStep) {
        val timestamp = currentTimestamp()
        updateForm { initializeBlankFromTimes(it, step, timestamp) }
    }

    fun selectCompletedWizardStep(step: WorkOrderWizardStep) {
        _state.update { current ->
            if (
                current.persistenceState.canStartWrite() &&
                !current.isAtdDialogVisible &&
                step.ordinal < current.wizardStep.ordinal
            ) {
                current.copy(wizardStep = step)
            } else {
                current
            }
        }
    }

    fun moveToPreviousWizardStep() {
        _state.update { current ->
            if (
                current.persistenceState.canStartWrite() &&
                !current.isAtdDialogVisible &&
                current.wizardStep != WorkOrderWizardStep.Flight
            ) {
                current.copy(
                    wizardStep = WorkOrderWizardStep.entries[current.wizardStep.ordinal - 1],
                )
            } else {
                current
            }
        }
    }

    fun advanceWizardStepAfterCheckpoint(fromStep: WorkOrderWizardStep) {
        if (fromStep == WorkOrderWizardStep.Signature) return
        val nextStep = WorkOrderWizardStep.entries[fromStep.ordinal + 1]
        var advanced = false
        _state.update { current ->
            if (
                current.persistenceState.canStartWrite() &&
                current.wizardStep == fromStep &&
                !current.isAtdDialogVisible
            ) {
                advanced = true
                current.copy(wizardStep = nextStep)
            } else {
                current
            }
        }
        if (
            advanced &&
            (nextStep == WorkOrderWizardStep.ServiceLines || nextStep == WorkOrderWizardStep.Tasks) &&
            initializedFromSteps.add(nextStep)
        ) {
            onWizardStepEntered(nextStep)
        }
    }

    fun showAtdDialog() {
        _state.update { current ->
            if (
                current.persistenceState.canStartWrite() &&
                current.wizardStep == WorkOrderWizardStep.Signature
            ) {
                current.copy(isAtdDialogVisible = true)
            } else {
                current
            }
        }
    }

    fun dismissAtdDialog() {
        _state.update { current ->
            if (current.persistenceState.canStartWrite()) {
                current.copy(isAtdDialogVisible = false)
            } else {
                current
            }
        }
    }

    fun routeToFirstWizardError(errors: CreateWorkOrderSubmitFieldErrors) {
        _state.update {
            it.copy(
                wizardStep = firstWizardStepWithErrors(errors),
                isAtdDialogVisible = false,
            )
        }
    }

    /**
     * Applies the automatic end-time fallback before the current step is validated and checkpointed.
     */
    internal fun prepareWizardStepForNext(step: WorkOrderWizardStep): Boolean {
        val timestamp = currentTimestamp()
        updateForm { finalizeBlankToTimes(it, step, timestamp) }
        return validateWizardStep(step)
    }

    init {
        viewModelScope.launch {
            hydrateLoggedInEmployeeFromCache()
        }
        viewModelScope.launch {
            catalogsRepository.servicesFlow().collect { list ->
                _state.update { it.copy(catalogServices = list.filterNot(ServiceEntity::isAircraftPerLanding)) }
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
                restoreLegacyScratchCustomer(list)
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

    fun selectCustomer(customerId: String?) {
        if (customerId == null) {
            updateEditedState { state ->
                state.copy(
                    selectedCustomerId = null,
                    flight = state.flight?.copy(customerName = "", customerIataCode = null),
                    form = state.form.copy(draftCustomerId = null),
                )
            }
            return
        }
        val customer = _state.value.catalogCustomers.firstOrNull { it.customerId == customerId } ?: return
        updateEditedState { s ->
            val f = s.flight ?: return@updateEditedState s
            s.copy(
                selectedCustomerId = customerId,
                flight = f.copy(customerName = customer.name, customerIataCode = customer.iataCode),
                form = s.form.copy(draftCustomerId = customerId),
            )
        }
    }

    /** Best-effort recovery for legacy scratch drafts that predate persisted customer ids. */
    private fun restoreLegacyScratchCustomer(customers: List<CustomerEntity>) {
        _state.update { s ->
            if (!s.isAdHocScratch || s.selectedCustomerId != null) return@update s
            val flight = s.flight ?: return@update s
            val match = customers.firstOrNull { customer ->
                customer.name.equals(flight.customerName, ignoreCase = true) &&
                    customer.iataCode.orEmpty().equals(flight.customerIataCode.orEmpty(), ignoreCase = true)
            } ?: return@update s
            s.copy(
                selectedCustomerId = match.customerId,
                form = s.form.copy(draftCustomerId = match.customerId),
            )
        }
    }

    /**
     * Persists current form + flight snapshot to Room. Subsequent saves reuse [CreateWorkOrderUiState.activeDraftId].
     */
    fun saveDraft(onFinished: (DraftSaveResult) -> Unit) {
        val initial = _state.value
        if (!initial.persistenceState.canStartWrite()) {
            onFinished(DraftSaveResult.Failed("A save or submission is already in progress."))
            return
        }
        val flight = initial.flight
        if (flight == null || initial.flightLoad != WorkOrderFlightLoadState.Ready) {
            onFinished(DraftSaveResult.Failed("Finish loading before saving a draft."))
            return
        }
        val draftId = stableDraftId(
            activeDraftId = initial.activeDraftId,
            sessionDraftId = sessionDraftId,
            createId = { UUID.randomUUID().toString() },
        ).also { sessionDraftId = it }
        val capturedRevision = initial.editState.revision
        val capturedGeneration = workflowGeneration
        _state.update { it.copy(persistenceState = WorkOrderPersistenceState.SavingDraft) }

        viewModelScope.launch {
            try {
                val normalizedForm = normalizedFormIdentifiers(
                    initial.form.copy(
                        draftSubmissionMode = when {
                            initial.isAdHocScratch -> WorkOrderDraftSubmissionMode.ScratchAdHoc
                            initial.isUpdatingCachedUnderReviewWorkOrder -> WorkOrderDraftSubmissionMode.UpdateExisting
                            else -> WorkOrderDraftSubmissionMode.ForFlight
                        },
                        draftCustomerId = initial.selectedCustomerId,
                        draftWorkOrderId = initial.updatingWorkOrderId,
                        draftWorkOrderRowVersion = initial.updatingWorkOrderRowVersion,
                    ),
                )
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
                if (workflowGeneration != capturedGeneration) return@launch
                var saveIsCurrent = false
                _state.update { current ->
                    val nextEditState = current.editState.afterSuccessfulSave(capturedRevision)
                    saveIsCurrent = !nextEditState.hasUnsavedChanges
                    current.copy(
                        activeDraftId = draftId,
                        editState = nextEditState,
                        persistenceState = WorkOrderPersistenceState.Idle,
                    )
                }
                val baseMessage = if (initial.activeDraftId == null) {
                    "Draft saved locally."
                } else {
                    "Draft updated."
                }
                onFinished(
                    DraftSaveResult.Saved(
                        message = if (saveIsCurrent) {
                            baseMessage
                        } else {
                            "$baseMessage Newer changes are still unsaved."
                        },
                        isCurrent = saveIsCurrent,
                    ),
                )
            } catch (_: Exception) {
                if (workflowGeneration == capturedGeneration) {
                    _state.update { it.copy(persistenceState = WorkOrderPersistenceState.Idle) }
                }
                onFinished(DraftSaveResult.Failed("Could not save draft."))
            }
        }
    }

    fun saveDraftWithAtd(
        atdIso: String,
        onFinished: (DraftSaveResult) -> Unit,
    ) {
        updateForm { it.copy(atdIso = atdIso.trim()) }
        saveDraft(onFinished)
    }

    private suspend fun loadFlight(flightId: String) {
        _state.update {
            it.copy(
                flightLoad = WorkOrderFlightLoadState.Loading,
                flight = null,
                activeDraftId = null,
                isAdHocScratch = false,
                isUpdatingCachedUnderReviewWorkOrder = false,
                updatingWorkOrderId = null,
                updatingWorkOrderRowVersion = null,
                selectedCustomerId = null,
                editState = WorkOrderEditState(),
                persistenceState = WorkOrderPersistenceState.Idle,
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
        val allowedPerformedServiceIds = catalogsRepository.servicesSnapshot().allowedPerformedServiceIds()

        val formBase = if (underReview) {
            wo!!.toPrefilledCreateFormState(::allocKey).copy(
                scheduledArrivalIso = row.sta,
                scheduledDepartureIso = row.std,
                draftSubmissionMode = WorkOrderDraftSubmissionMode.UpdateExisting,
                draftWorkOrderId = wo.id,
                draftWorkOrderRowVersion = wo.rowVersion,
            )
        } else {
            // Normal flights copy planned services into seeded lines. Per-Landing flights start
            // empty so the user adds only services that were actually performed.
            CreateWorkOrderFormState(
                flightNumber = row.flightNumber,
                aircraftTypeId = presetTypeId,
                scheduledArrivalIso = row.sta,
                scheduledDepartureIso = row.std,
                ataIso = row.sta,
                atdIso = "",
                serviceLines = serviceLinesToPrefill(row, allowedPerformedServiceIds, ::allocKey),
                tasks = emptyList(),
                draftSubmissionMode = WorkOrderDraftSubmissionMode.ForFlight,
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
                updatingWorkOrderId = if (underReview) wo?.id else null,
                updatingWorkOrderRowVersion = if (underReview) wo?.rowVersion else null,
                selectedCustomerId = null,
                form = formNormalized,
                submitFieldErrors = null,
                submitValidationResult = null,
            )
        }
        applyDefaultPerformersAcrossForm(_state.value)
        resetEditBaseline(activeDraftId = null)
    }

    private suspend fun loadAdHocScratch() {
        val employees = employeesRepository.snapshot()
        val employeeId = tokenStore.getEmployeeId()
        val stationCode = tokenStore.getStationCode()?.trim().orEmpty()
        val scheduledArrival = OffsetDateTime.now(clock)
            .withOffsetSameInstant(ZoneOffset.UTC)
            .withMinute(0)
            .withSecond(0)
            .withNano(0)
        val scheduledDeparture = scheduledArrival.plusHours(2)
        val sta = scheduledArrival.toString()
        val std = scheduledDeparture.toString()
        val row = WorkOrderFlightRow(
            id = adHocScratchFlightId,
            flightNumber = "",
            operationTypeName = "Ad Hoc",
            sta = sta,
            std = std,
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
                updatingWorkOrderId = null,
                updatingWorkOrderRowVersion = null,
                selectedCustomerId = null,
                loggedInEmployeeId = employeeId ?: it.loggedInEmployeeId,
                catalogEmployees = employees.ifEmpty { it.catalogEmployees },
                form = normalizedFormIdentifiers(
                    CreateWorkOrderFormState(
                        flightNumber = "",
                        aircraftTypeId = null,
                        scheduledArrivalIso = sta,
                        scheduledDepartureIso = std,
                        ataIso = sta,
                        atdIso = "",
                        serviceLines = emptyList(),
                        tasks = emptyList(),
                        draftSubmissionMode = WorkOrderDraftSubmissionMode.ScratchAdHoc,
                    ),
                ),
                submitFieldErrors = null,
                submitValidationResult = null,
            )
        }
        applyDefaultPerformersAcrossForm(_state.value)
        resetEditBaseline(activeDraftId = null)
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
                updatingWorkOrderId = null,
                updatingWorkOrderRowVersion = null,
                selectedCustomerId = null,
                editState = WorkOrderEditState(),
                persistenceState = WorkOrderPersistenceState.Idle,
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
        val cachedEditableWorkOrder = flight.cachedMyWorkOrder?.takeIf {
            WorkOrderStatusKind.fromWire(it.status)?.isEditable == true
        }
        val persistedMode = form.draftSubmissionMode
        val serverFlightStillExists = if (WorkOrderDraftSubmissionMode.isKnown(persistedMode)) {
            null
        } else {
            flightsRepository.findWorkOrderFlight(flight.id)
        }
        val inferredMode = when {
            WorkOrderDraftSubmissionMode.isKnown(persistedMode) -> persistedMode
            cachedEditableWorkOrder != null || form.draftWorkOrderId != null ->
                WorkOrderDraftSubmissionMode.UpdateExisting
            flight.isAdHoc && serverFlightStillExists == null -> WorkOrderDraftSubmissionMode.ScratchAdHoc
            else -> WorkOrderDraftSubmissionMode.ForFlight
        }
        val isScratch = inferredMode == WorkOrderDraftSubmissionMode.ScratchAdHoc
        val updateWorkOrderId = form.draftWorkOrderId ?: cachedEditableWorkOrder?.id
        val updateRowVersion = form.draftWorkOrderRowVersion ?: cachedEditableWorkOrder?.rowVersion

        val schedule = normalizedSchedule(
            arrivalIso = form.scheduledArrivalIso.ifBlank { flight.sta },
            departureIso = form.scheduledDepartureIso.ifBlank { flight.std },
            requirePositiveWindow = isScratch,
        )
        val formNormalized = normalizedFormIdentifiers(
            form.copy(
                scheduledArrivalIso = schedule.first,
                scheduledDepartureIso = schedule.second,
                ataIso = form.ataIso.ifBlank { schedule.first },
                atdIso = form.atdIso,
                draftSubmissionMode = inferredMode,
                draftCustomerId = form.draftCustomerId,
                draftWorkOrderId = updateWorkOrderId,
                draftWorkOrderRowVersion = updateRowVersion,
            ),
        )
        val hydratedFlight = flight.copy(sta = schedule.first, std = schedule.second)
        reconcileNextLocalKeyFromForm(formNormalized)
        _state.update {
            it.copy(
                flightLoad = WorkOrderFlightLoadState.Ready,
                flight = hydratedFlight,
                form = formNormalized,
                activeDraftId = draftId,
                showSaveAsDraftButton = false,
                isAdHocScratch = isScratch,
                isUpdatingCachedUnderReviewWorkOrder = inferredMode == WorkOrderDraftSubmissionMode.UpdateExisting,
                updatingWorkOrderId = updateWorkOrderId,
                updatingWorkOrderRowVersion = updateRowVersion,
                selectedCustomerId = form.draftCustomerId,
                submitFieldErrors = null,
                submitValidationResult = null,
            )
        }
        restoreLegacyScratchCustomer(_state.value.catalogCustomers)
        applyDefaultPerformersAcrossForm(_state.value)
        resetEditBaseline(activeDraftId = draftId)
    }

    private fun normalizedSchedule(
        arrivalIso: String,
        departureIso: String,
        requirePositiveWindow: Boolean,
    ): Pair<String, String> {
        if (!requirePositiveWindow) return arrivalIso to departureIso
        val arrival = runCatching { parseOffsetDateTime(arrivalIso) }.getOrNull() ?: return arrivalIso to departureIso
        val departure = runCatching { parseOffsetDateTime(departureIso) }.getOrNull()
        return if (departure == null || !departure.isAfter(arrival)) {
            arrivalIso to arrival.plusHours(2).toString()
        } else {
            arrivalIso to departureIso
        }
    }

    private fun reconcileNextLocalKeyFromForm(form: CreateWorkOrderFormState) {
        val keys = form.serviceLines.map { it.localKey } + form.tasks.map { it.localKey }
        val maxKey = keys.maxOrNull() ?: 0L
        nextLocalKey = maxOf(maxKey + 1, nextLocalKey)
    }

    /**
     * When `/me` and the synced roster agree:
     * - Service lines with no performers get a one-person default (`employeeIds` empty only).
     * - Tasks with no performers yet get a one-person default (`employeeIds` empty only).
     * Never overwrites explicit user edits.
     */
    private fun applyDefaultPerformersAcrossForm(snapshot: CreateWorkOrderUiState) {
        val presetId = resolvedDefaultPerformingEmployeeId(snapshot) ?: return
        _state.update { s ->
            val newLines = s.form.serviceLines.map { line ->
                if (line.employeeIds.isEmpty()) line.copy(employeeIds = listOf(presetId)) else line
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
    private fun defaultEmployeeIdsFromState(): List<String> =
        resolvedDefaultPerformingEmployeeId(_state.value)?.let { listOf(it) } ?: emptyList()

    fun updateForm(transform: (CreateWorkOrderFormState) -> CreateWorkOrderFormState) {
        updateEditedState { s ->
            val next = transform(s.form)
            if (next == s.form) s else s.copy(form = next)
        }
    }

    fun updateFlightNumber(raw: String) {
        val normalized = normalizeWorkOrderFlightNumberInput(raw).take(WorkOrderFormLimits.FlightNumber)
        updateEditedState { s ->
            val nextForm = s.form.copy(flightNumber = normalized)
            if (nextForm == s.form && (!s.isAdHocScratch || s.flight?.flightNumber == normalized)) {
                return@updateEditedState s
            }
            val nextFlight = if (s.isAdHocScratch) s.flight?.copy(flightNumber = normalized) else s.flight
            s.copy(form = nextForm, flight = nextFlight)
        }
    }

    fun updateAircraftTailNumber(raw: String) {
        updateForm {
            it.copy(
                aircraftTailNumber = normalizeWorkOrderAircraftTailInput(raw)
                    .take(WorkOrderFormLimits.AircraftTailNumber),
            )
        }
    }

    fun setAircraftType(id: String?) {
        updateForm { it.copy(aircraftTypeId = id) }
    }

    fun updateScratchScheduledArrival(iso: String) {
        updateEditedState { s ->
            if (!s.isAdHocScratch) return@updateEditedState s
            val previous = s.form.scheduledArrivalIso
            val nextForm = s.form.copy(
                scheduledArrivalIso = iso,
                ataIso = if (s.form.ataIso.isBlank() || s.form.ataIso == previous) iso else s.form.ataIso,
            )
            s.copy(
                flight = s.flight?.copy(sta = iso),
                form = nextForm,
            )
        }
    }

    fun updateScratchScheduledDeparture(iso: String) {
        updateEditedState { s ->
            if (!s.isAdHocScratch) return@updateEditedState s
            val nextForm = s.form.copy(scheduledDepartureIso = iso)
            s.copy(
                flight = s.flight?.copy(std = iso),
                form = nextForm,
            )
        }
    }

    fun addServiceLine() {
        val presetEmployees = defaultEmployeeIdsFromState()
        val from = currentTimestamp()
        updateForm {
            it.copy(
                serviceLines = it.serviceLines + newServiceLineAt(
                    localKey = allocKey(),
                    employeeIds = presetEmployees,
                    timestampIso = from,
                ),
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
            f.copy(
                serviceLines = f.serviceLines.map { current ->
                    if (current.localKey == row.localKey) {
                        // Attachment capture completes asynchronously; retain the freshest
                        // attachment lists when a field callback was composed from stale state.
                        current.mergeNonAttachmentEdit(row)
                    } else {
                        current
                    }
                },
            )
        }
    }

    fun addServiceLineAttachment(localKey: Long, attachment: TaskAttachmentDraft) {
        updateForm { f -> f.withServiceLineAttachmentAdded(localKey, attachment) }
    }

    fun removeServiceLineAttachment(localKey: Long, attachment: TaskAttachmentDraft) {
        updateForm { f -> f.withServiceLineAttachmentRemoved(localKey, attachment) }
    }

    fun addTask() {
        val presetEmployees = defaultEmployeeIdsFromState()
        val from = currentTimestamp()
        updateForm {
            it.copy(
                tasks = it.tasks + newTaskAt(
                    localKey = allocKey(),
                    employeeIds = presetEmployees,
                    timestampIso = from,
                ),
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
            f.copy(
                tasks = f.tasks.map { current ->
                    if (current.localKey == row.localKey) {
                        // Attachments complete asynchronously; a field callback must never replace
                        // a newer attachment list captured after this row was composed.
                        current.mergeNonAttachmentEdit(row)
                    } else {
                        current
                    }
                },
            )
        }
    }

    fun addTaskAttachment(localKey: Long, attachment: TaskAttachmentDraft) {
        updateForm { f -> f.withTaskAttachmentAdded(localKey, attachment) }
    }

    fun removeTaskAttachment(localKey: Long, attachment: TaskAttachmentDraft) {
        updateForm { f -> f.withTaskAttachmentRemoved(localKey, attachment) }
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
    fun validateBeforeAtdDialog(): CreateWorkOrderSubmitFieldErrors? {
        val f = normalizedFormIdentifiers(_state.value.form)
        val snap = _state.value
        val errors = computeCreateWorkOrderSubmitErrors(
            form = f,
            dialogAtdIso = null,
            validationPhase = WorkOrderValidationPhase.BeforeAtd,
            isAdHocScratch = snap.isAdHocScratch,
            selectedCustomerId = snap.selectedCustomerId,
            allowedPerformedServiceIds = snap.catalogServices.allowedPerformedServiceIds(),
        )
        _state.update {
            it.copy(
                form = f,
                submitFieldErrors = errors,
                submitValidationResult = null,
            )
        }
        return errors
    }

    fun confirmSubmitWithAtd(atdIso: String): CreateWorkOrderSubmitFieldErrors? {
        val f = normalizedFormIdentifiers(_state.value.form)
        val snap = _state.value
        val errors = computeCreateWorkOrderSubmitErrors(
            f,
            atdIso,
            validationPhase = WorkOrderValidationPhase.Submission,
            isAdHocScratch = snap.isAdHocScratch,
            selectedCustomerId = snap.selectedCustomerId,
            allowedPerformedServiceIds = snap.catalogServices.allowedPerformedServiceIds(),
        )
        if (errors == null) {
            val confirmedAtd = atdIso.trim()
            updateForm { current ->
                normalizedFormIdentifiers(current).copy(atdIso = confirmedAtd)
            }
            return null
        }
        _state.update { it.copy(submitFieldErrors = errors, submitValidationResult = null) }
        return errors
    }

    /**
     * Queues the validated work-order submission for offline delivery.
     *
     * Attachment persistence and the Room insert run on [applicationScope] + [Dispatchers.IO]
     * so screen lifecycle cancellation cannot interrupt them. Navigation happens only after the
     * durable outbox row exists; otherwise the form remains visible and can surface the failure.
     *
     * Call only after [confirmSubmitWithAtd] returns `null`.
     *
     * @param onEnqueuedNavigate invoked on the main thread after the durable enqueue succeeds.
     * @param onFinished called on the main thread after the background write
     *        succeeds or fails. For [SubmitOfflineResult.Enqueued] the list’s
     *        Flow already shows the chip; a snackbar is optional. For [Failed],
     *        show a toast with application context (the create screen may be gone).
     */
    fun enqueueSubmission(
        flightKindOverride: Int? = null,
        onEnqueuedNavigate: () -> Unit,
        onFinished: (SubmitOfflineResult) -> Unit,
    ) {
        val snapshot = _state.value
        if (!snapshot.persistenceState.canStartWrite()) {
            onFinished(SubmitOfflineResult.Failed("A save or submission is already in progress."))
            return
        }
        val flight = snapshot.flight
        if (flight == null || snapshot.flightLoad != WorkOrderFlightLoadState.Ready) {
            onFinished(SubmitOfflineResult.Failed("Finish loading the flight before submitting."))
            return
        }
        val currentErrors = computeCreateWorkOrderSubmitErrors(
            form = normalizedFormIdentifiers(snapshot.form),
            dialogAtdIso = snapshot.form.atdIso,
            validationPhase = WorkOrderValidationPhase.Submission,
            isAdHocScratch = snapshot.isAdHocScratch,
            selectedCustomerId = snapshot.selectedCustomerId,
            allowedPerformedServiceIds = snapshot.catalogServices.allowedPerformedServiceIds(),
        )
        if (currentErrors != null) {
            _state.update { it.copy(submitFieldErrors = currentErrors, submitValidationResult = null) }
            onFinished(SubmitOfflineResult.Failed("Fix the highlighted fields before submitting."))
            return
        }

        val mutationId = UUID.randomUUID().toString()
        val isScratch = snapshot.isAdHocScratch
        val flightKind = flightKindOverride ?: resolveFlightKind(snapshot, flight)
        val isUpdateExisting = snapshot.isUpdatingCachedUnderReviewWorkOrder && !isScratch
        val knownServerWorkOrderId = if (isUpdateExisting) {
            snapshot.updatingWorkOrderId ?: flight.cachedMyWorkOrder?.id
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
        if (isUpdateExisting &&
            (snapshot.updatingWorkOrderRowVersion ?: flight.cachedMyWorkOrder?.rowVersion).isNullOrBlank()
        ) {
            onFinished(
                SubmitOfflineResult.Failed(
                    "This work order is missing its base revision. Refresh the flight before updating it.",
                ),
            )
            return
        }
        val payload = try {
            buildOutboxPayload(snapshot, flight, isScratch, isUpdateExisting)
        } catch (e: IllegalStateException) {
            onFinished(SubmitOfflineResult.Failed(e.message ?: "Could not prepare submission."))
            return
        }
        val attachmentsToPersist = collectAttachmentsInServiceThenTaskOrder(snapshot.form)
        val request = EnqueueRequest(
            clientMutationId = mutationId,
            flightId = flight.id,
            flightKind = flightKind,
            clientFlightId = if (isScratch) flight.id else null,
            payload = payload,
            attachmentsToPersist = attachmentsToPersist,
            knownServerWorkOrderId = knownServerWorkOrderId,
            draftIdToDelete = snapshot.activeDraftId,
        )
        workflowGeneration += 1
        _state.update { it.copy(persistenceState = WorkOrderPersistenceState.Submitting) }

        applicationScope.launch(Dispatchers.IO) {
            try {
                outboxRepository.enqueue(request)
                withContext(Dispatchers.Main.immediate) {
                    _state.update { it.copy(persistenceState = WorkOrderPersistenceState.Idle) }
                    onEnqueuedNavigate()
                    onFinished(SubmitOfflineResult.Enqueued(clientMutationId = mutationId))
                }
            } catch (e: Exception) {
                withContext(Dispatchers.Main.immediate) {
                    _state.update { it.copy(persistenceState = WorkOrderPersistenceState.Idle) }
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
        val ataIso = form.ataIso.trim().takeIf { it.isNotEmpty() }
        val atdIso = form.atdIso.trim().takeIf { it.isNotEmpty() }

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
                    id = row.serverId,
                    serviceId = row.serviceId
                        ?: error("Service line missing serviceId — validation should have caught this"),
                    performedByStaffMemberIds = row.employeeIds,
                    fromIso = row.fromIso,
                    toIso = row.toIso,
                    description = row.description.takeIf { it.isNotBlank() },
                    // The repository replaces these slots with durable file-path references.
                    attachments = row.attachments.map { attachment ->
                        attachment.toOutboxPlaceholder()
                    },
                    isReturnToRamp = row.returnToRamp,
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
                    tools = task.toolIds.map {
                        OutboxPayload.ResourceInput(it, resourceQuantity(task.toolQuantities, it))
                    },
                    materials = task.materialIds.map {
                        OutboxPayload.ResourceInput(it, resourceQuantity(task.materialQuantities, it))
                    },
                    generalSupports = task.generalSupportIds.map {
                        OutboxPayload.ResourceInput(it, resourceQuantity(task.generalSupportQuantities, it))
                    },
                    // Each task carries `task.attachments.size` slots; the repository
                    // re-stitches them after writing the durable copies to disk.
                    attachments = task.attachments.map { attachment ->
                        attachment.toOutboxPlaceholder()
                    },
                    isReturnToRamp = task.returnToRamp,
                )
            },
            customerSignaturePngBase64 = form.customerSignaturePng,
            serviceLineIdentityVersion = if (isUpdateExisting) form.serviceLineIdentityVersion else 0,
        )

        val scratch = if (isScratch) {
            val customerId = snapshot.selectedCustomerId
                ?: error("Ad-hoc-scratch submission missing customer — validation should have caught this")
            OutboxPayload.ScratchFlightInput(
                customerId = customerId,
                flightNumber = form.flightNumber.ifBlank { flight.flightNumber },
                staIso = form.scheduledArrivalIso,
                stdIso = form.scheduledDepartureIso,
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
            baseRowVersion = if (isUpdateExisting) {
                snapshot.updatingWorkOrderRowVersion
                    ?: flight.cachedMyWorkOrder?.rowVersion
            } else null,
        )
    }

    private fun collectAttachmentsInServiceThenTaskOrder(
        form: CreateWorkOrderFormState,
    ): List<EnqueueAttachment> = collectAttachmentsForOutbox(form)

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

    /** Validates only the fields visible on one portal-equivalent wizard step. */
    internal fun validateWizardStep(step: WorkOrderWizardStep): Boolean {
        _state.update { s ->
            val nextForm = normalizedFormIdentifiers(s.form)
            if (nextForm == s.form) s else s.copy(form = nextForm, submitFieldErrors = null)
        }
        val snap = _state.value
        val allErrors = computeCreateWorkOrderSubmitErrors(
            form = snap.form,
            dialogAtdIso = null,
            validationPhase = WorkOrderValidationPhase.BeforeAtd,
            isAdHocScratch = snap.isAdHocScratch,
            selectedCustomerId = snap.selectedCustomerId,
            allowedPerformedServiceIds = snap.catalogServices.allowedPerformedServiceIds(),
        )
        val stepErrors = submitErrorsForWizardStep(allErrors, step)
        _state.update {
            it.copy(
                submitFieldErrors = stepErrors,
                submitValidationResult = null,
            )
        }
        return stepErrors == null
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
        val errors = computeCreateWorkOrderSubmitErrors(
            f,
            dialogAtdIso = null,
            validationPhase = WorkOrderValidationPhase.BeforeAtd,
            isAdHocScratch = snap.isAdHocScratch,
            selectedCustomerId = snap.selectedCustomerId,
            allowedPerformedServiceIds = snap.catalogServices.allowedPerformedServiceIds(),
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
