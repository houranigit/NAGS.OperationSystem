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
)

/** Populated after a failed submit — cleared when the form edits ([updateForm]) or reapplied on next submit. */
data class ServiceLineSubmitFieldErrors(
    val serviceType: String? = null,
    val performer: String? = null,
    val from: String? = null,
    val to: String? = null,
    val description: String? = null,
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
    val isSubmitting: Boolean = false,
)

sealed interface SubmitValidationResult {
    data object Passed : SubmitValidationResult
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
            _state.update { state ->
                state.copy(
                    selectedCustomerId = null,
                    flight = state.flight?.copy(customerName = "", customerIataCode = null),
                    form = state.form.copy(draftCustomerId = null),
                    submitFieldErrors = null,
                )
            }
            return
        }
        val customer = _state.value.catalogCustomers.firstOrNull { it.customerId == customerId } ?: return
        _state.update { s ->
            val f = s.flight ?: return@update s
            s.copy(
                selectedCustomerId = customerId,
                flight = f.copy(customerName = customer.name, customerIataCode = customer.iataCode),
                form = s.form.copy(draftCustomerId = customerId),
                submitFieldErrors = null,
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

    /** Planned services seeded into the form; Per-Landing flights instead begin with On Call. */
    private fun seededServiceLines(row: WorkOrderFlightRow): List<ServiceLineFormRow> {
        if (row.isPerLanding) {
            return listOf(
                ServiceLineFormRow(
                    localKey = allocKey(),
                    serviceId = ON_CALL_SERVICE_ID,
                    fromIso = row.sta,
                    toIso = row.std,
                ),
            )
        }
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
                val normalizedForm = normalizedFormIdentifiers(
                    snapshot.form.copy(
                        draftSubmissionMode = when {
                            snapshot.isAdHocScratch -> WorkOrderDraftSubmissionMode.ScratchAdHoc
                            snapshot.isUpdatingCachedUnderReviewWorkOrder -> WorkOrderDraftSubmissionMode.UpdateExisting
                            else -> WorkOrderDraftSubmissionMode.ForFlight
                        },
                        draftCustomerId = snapshot.selectedCustomerId,
                        draftWorkOrderId = snapshot.updatingWorkOrderId,
                        draftWorkOrderRowVersion = snapshot.updatingWorkOrderRowVersion,
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
                updatingWorkOrderId = null,
                updatingWorkOrderRowVersion = null,
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
            wo!!.toPrefilledCreateFormState(::allocKey).copy(
                scheduledArrivalIso = row.sta,
                scheduledDepartureIso = row.std,
                draftSubmissionMode = WorkOrderDraftSubmissionMode.UpdateExisting,
                draftWorkOrderId = wo.id,
                draftWorkOrderRowVersion = wo.rowVersion,
            )
        } else {
            // Planned services copied into seeded lines (skip Per-Landing); the user completes
            // each line's performer or removes it, and may add extra lines.
            CreateWorkOrderFormState(
                flightNumber = row.flightNumber,
                aircraftTypeId = presetTypeId,
                scheduledArrivalIso = row.sta,
                scheduledDepartureIso = row.std,
                ataIso = row.sta,
                atdIso = row.std,
                serviceLines = seededServiceLines(row),
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
    }

    private suspend fun loadAdHocScratch() {
        val employees = employeesRepository.snapshot()
        val employeeId = tokenStore.getEmployeeId()
        val stationCode = tokenStore.getStationCode()?.trim().orEmpty()
        val scheduledArrival = OffsetDateTime.now(ZoneOffset.UTC)
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
                        atdIso = std,
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
                atdIso = form.atdIso.ifBlank { schedule.second },
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
                s.copy(form = next, submitFieldErrors = null)
            }
        }
    }

    fun updateFlightNumber(raw: String) {
        val normalized = normalizeWorkOrderFlightNumberInput(raw).take(WorkOrderFormLimits.FlightNumber)
        _state.update { s ->
            val nextForm = s.form.copy(flightNumber = normalized)
            if (nextForm == s.form && (!s.isAdHocScratch || s.flight?.flightNumber == normalized)) {
                return@update s
            }
            val nextFlight = if (s.isAdHocScratch) s.flight?.copy(flightNumber = normalized) else s.flight
            s.copy(form = nextForm, flight = nextFlight, submitFieldErrors = null)
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
        _state.update { s ->
            if (!s.isAdHocScratch) return@update s
            val previous = s.form.scheduledArrivalIso
            val nextForm = s.form.copy(
                scheduledArrivalIso = iso,
                ataIso = if (s.form.ataIso.isBlank() || s.form.ataIso == previous) iso else s.form.ataIso,
            )
            s.copy(
                flight = s.flight?.copy(sta = iso),
                form = nextForm,
                submitFieldErrors = null,
            )
        }
    }

    fun updateScratchScheduledDeparture(iso: String) {
        _state.update { s ->
            if (!s.isAdHocScratch) return@update s
            val previous = s.form.scheduledDepartureIso
            val nextForm = s.form.copy(
                scheduledDepartureIso = iso,
                atdIso = if (s.form.atdIso.isBlank() || s.form.atdIso == previous) iso else s.form.atdIso,
            )
            s.copy(
                flight = s.flight?.copy(std = iso),
                form = nextForm,
                submitFieldErrors = null,
            )
        }
    }

    fun addServiceLine() {
        val preset = defaultSingleEmployeeIdFromState()
        val form = _state.value.form
        val flight = _state.value.flight
        val from = form.ataIso.ifBlank { flight?.sta.orEmpty() }
        val to = form.atdIso.ifBlank { flight?.std.orEmpty() }
        updateForm {
            it.copy(
                serviceLines = it.serviceLines + ServiceLineFormRow(
                    localKey = allocKey(),
                    employeeId = preset,
                    fromIso = from,
                    toIso = to,
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
            f.copy(serviceLines = f.serviceLines.map { if (it.localKey == row.localKey) row else it })
        }
    }

    fun addTask() {
        val presetEmployees = defaultEmployeeIdsFromState()
        val form = _state.value.form
        val flight = _state.value.flight
        val from = form.ataIso.ifBlank { flight?.sta.orEmpty() }
        val to = form.atdIso.ifBlank { flight?.std.orEmpty() }
        updateForm {
            it.copy(
                tasks = it.tasks + TaskFormRow(
                    localKey = allocKey(),
                    employeeIds = presetEmployees,
                    fromIso = from,
                    toIso = to,
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
    fun confirmSubmitWithAtd(atdIso: String?): CreateWorkOrderSubmitFieldErrors? {
        val f = normalizedFormIdentifiers(_state.value.form)
        val snap = _state.value
        val errors = computeCreateWorkOrderSubmitErrors(
            f,
            atdIso,
            isAdHocScratch = snap.isAdHocScratch,
            selectedCustomerId = snap.selectedCustomerId,
        )
        if (errors == null) {
            val confirmedAtd = atdIso?.trim()?.takeIf { it.isNotEmpty() } ?: f.atdIso
            _state.update {
                it.copy(
                    form = f.copy(atdIso = confirmedAtd),
                    submitFieldErrors = null,
                )
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
        _state.update { it.copy(isSubmitting = true) }

        applicationScope.launch(Dispatchers.IO) {
            try {
                outboxRepository.enqueue(request)
                draftId?.let { id -> runCatching { draftsRepository.deleteDraft(id) } }
                withContext(Dispatchers.Main.immediate) {
                    _state.update { it.copy(isSubmitting = false) }
                    onEnqueuedNavigate()
                    onFinished(SubmitOfflineResult.Enqueued(clientMutationId = mutationId))
                }
            } catch (e: Exception) {
                withContext(Dispatchers.Main.immediate) {
                    _state.update { it.copy(isSubmitting = false) }
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
            isAdHocScratch = snap.isAdHocScratch,
            selectedCustomerId = snap.selectedCustomerId,
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
        const val ON_CALL_SERVICE_ID = "40000000-0000-0000-0000-000000000002"

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
