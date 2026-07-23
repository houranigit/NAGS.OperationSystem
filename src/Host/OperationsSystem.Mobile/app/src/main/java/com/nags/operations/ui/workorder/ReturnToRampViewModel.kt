package com.nags.operations.ui.workorder

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.nags.operations.data.WorkOrderStatusKind
import com.nags.operations.data.api.MobileApi
import com.nags.operations.data.db.entities.EmployeeEntity
import com.nags.operations.data.db.entities.GeneralSupportEntity
import com.nags.operations.data.db.entities.MaterialEntity
import com.nags.operations.data.db.entities.ServiceEntity
import com.nags.operations.data.db.entities.allowedPerformedServiceIds
import com.nags.operations.data.db.entities.ToolEntity
import com.nags.operations.data.db.entities.WorkOrderOutboxEntity
import com.nags.operations.data.outbox.EnqueueAttachment
import com.nags.operations.data.outbox.EnqueueRequest
import com.nags.operations.data.outbox.OutboxPayload
import com.nags.operations.data.outbox.WorkOrderOutboxRepository
import com.nags.operations.data.repo.CatalogsRepository
import com.nags.operations.data.repo.EmployeesRepository
import com.nags.operations.data.repo.FlightsRepository
import com.nags.operations.data.repo.WorkOrderFlightRow
import com.nags.operations.ui.util.normalizeWorkOrderFlightNumberInput
import java.util.UUID
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

data class ReturnToRampUiState(
    val flightLoad: WorkOrderFlightLoadState = WorkOrderFlightLoadState.Loading,
    val flight: WorkOrderFlightRow? = null,
    val catalogServices: List<ServiceEntity> = emptyList(),
    val catalogEmployees: List<EmployeeEntity> = emptyList(),
    val catalogTools: List<ToolEntity> = emptyList(),
    val catalogMaterials: List<MaterialEntity> = emptyList(),
    val catalogGeneralSupports: List<GeneralSupportEntity> = emptyList(),
    val form: CreateWorkOrderFormState = CreateWorkOrderFormState(),
    val submitFieldErrors: CreateWorkOrderSubmitFieldErrors? = null,
    val formLevelError: String? = null,
    /** Existing saved lines revoked since the work order was last submitted must be corrected in Edit Work Order. */
    val existingAllowanceError: String? = null,
    val loggedInEmployeeId: String? = null,
    val isSubmitting: Boolean = false,
)

internal fun existingWorkOrderAllowanceError(
    existingServiceIds: Collection<String>,
    allowedPerformedServiceIds: Set<String>,
): String? = if (existingServiceIds.any { it !in allowedPerformedServiceIds }) {
    "This work order contains a service no longer allowed for your manpower type. " +
        "Open Edit Work Order and remove or replace it before recording Return to Ramp."
} else {
    null
}

/**
 * Minimal work-order editor for append-only return-to-ramp lines (services + tasks only).
 */
class ReturnToRampViewModel(
    private val flightId: String,
    private val flightsRepository: FlightsRepository,
    private val outboxRepository: WorkOrderOutboxRepository,
    private val mobileApi: MobileApi,
    private val applicationScope: CoroutineScope,
    catalogsRepository: CatalogsRepository,
    employeesRepository: EmployeesRepository,
) : ViewModel() {

    private var nextLocalKey = 1L
    private fun allocKey(): Long = nextLocalKey++

    private val _state = MutableStateFlow(ReturnToRampUiState())
    val state: StateFlow<ReturnToRampUiState> = _state.asStateFlow()

    init {
        viewModelScope.launch {
            runCatching { mobileApi.me() }.getOrNull()?.let { me ->
                _state.update { it.copy(loggedInEmployeeId = me.staffMemberId) }
                applyDefaultPerformers(_state.value)
            }
        }
        viewModelScope.launch {
            catalogsRepository.servicesFlow().collect { list ->
                _state.update { it.copy(catalogServices = list.filterNot(ServiceEntity::isAircraftPerLanding)) }
                refreshExistingAllowanceError()
            }
        }
        viewModelScope.launch {
            employeesRepository.observe().collect { list ->
                _state.update { it.copy(catalogEmployees = list) }
                applyDefaultPerformers(_state.value)
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
        viewModelScope.launch { loadFlight() }
    }

    fun retryLoadFlight() {
        viewModelScope.launch { loadFlight() }
    }

    private suspend fun loadFlight() {
        _state.update { it.copy(flightLoad = WorkOrderFlightLoadState.Loading, flight = null) }
        val row = flightsRepository.findWorkOrderFlight(flightId)
        if (row == null) {
            _state.update {
                it.copy(
                    flightLoad = WorkOrderFlightLoadState.Error(
                        "Flight not found in the offline cache. Refresh the flight list, then try again.",
                    ),
                )
            }
            return
        }
        val wo = row.cachedMyWorkOrder
        val editable = wo != null && WorkOrderStatusKind.fromWire(wo.status)?.isEditable == true
        if (!editable) {
            _state.update {
                it.copy(
                    flightLoad = WorkOrderFlightLoadState.Error(
                        "Return to ramp needs your editable work order on this flight. Create or update the work order first.",
                    ),
                )
            }
            return
        }
        _state.update {
            it.copy(
                flightLoad = WorkOrderFlightLoadState.Ready,
                flight = row,
                form = CreateWorkOrderFormState(),
                submitFieldErrors = null,
                formLevelError = null,
                existingAllowanceError = null,
            )
        }
        refreshExistingAllowanceError()
        applyDefaultPerformers(_state.value)
    }

    private fun refreshExistingAllowanceError() {
        _state.update { snapshot ->
            val existingServiceIds = snapshot.flight?.cachedMyWorkOrder?.serviceLines
                ?.map { it.serviceId }
                .orEmpty()
            val allowedIds = snapshot.catalogServices.allowedPerformedServiceIds()
            snapshot.copy(
                existingAllowanceError = existingWorkOrderAllowanceError(existingServiceIds, allowedIds),
            )
        }
    }

    private fun applyDefaultPerformers(snapshot: ReturnToRampUiState) {
        val empId = snapshot.loggedInEmployeeId ?: return
        val employees = snapshot.catalogEmployees
        if (employees.none { it.staffMemberId == empId }) return
        val form = snapshot.form
        val nextLines = form.serviceLines.map { line ->
            if (line.employeeIds.isEmpty()) line.copy(employeeIds = listOf(empId)) else line
        }
        val nextTasks = form.tasks.map { task ->
            if (task.employeeIds.isEmpty()) task.copy(employeeIds = listOf(empId)) else task
        }
        if (nextLines == form.serviceLines && nextTasks == form.tasks) return
        _state.update { it.copy(form = form.copy(serviceLines = nextLines, tasks = nextTasks)) }
    }

    fun updateForm(transform: (CreateWorkOrderFormState) -> CreateWorkOrderFormState) {
        _state.update { s ->
            s.copy(
                form = transform(s.form),
                submitFieldErrors = null,
                formLevelError = null,
            )
        }
    }

    fun addServiceLine() {
        val preset = _state.value.loggedInEmployeeId?.let(::listOf).orEmpty()
        val flight = _state.value.flight
        val from = flight?.cachedMyWorkOrder?.actualArrivalUtc ?: flight?.sta.orEmpty()
        val to = flight?.cachedMyWorkOrder?.actualDepartureUtc ?: flight?.std.orEmpty()
        updateForm {
            it.copy(
                serviceLines = it.serviceLines + ServiceLineFormRow(
                    localKey = allocKey(),
                    employeeIds = preset,
                    fromIso = from,
                    toIso = to,
                    returnToRamp = true,
                ),
            )
        }
    }

    fun removeServiceLine(key: Long) {
        updateForm { f -> f.copy(serviceLines = f.serviceLines.filterNot { it.localKey == key }) }
    }

    fun replaceServiceLine(row: ServiceLineFormRow) {
        updateForm { f ->
            f.copy(
                serviceLines = f.serviceLines.map { current ->
                    if (current.localKey == row.localKey) {
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
        val preset = _state.value.loggedInEmployeeId?.let { listOf(it) } ?: emptyList()
        val flight = _state.value.flight
        val from = flight?.cachedMyWorkOrder?.actualArrivalUtc ?: flight?.sta.orEmpty()
        val to = flight?.cachedMyWorkOrder?.actualDepartureUtc ?: flight?.std.orEmpty()
        updateForm {
            it.copy(
                tasks = it.tasks + TaskFormRow(
                    localKey = allocKey(),
                    employeeIds = preset,
                    fromIso = from,
                    toIso = to,
                    returnToRamp = true,
                ),
            )
        }
    }

    fun removeTask(key: Long) {
        updateForm { f -> f.copy(tasks = f.tasks.filterNot { it.localKey == key }) }
    }

    fun replaceTask(row: TaskFormRow) {
        updateForm { f ->
            f.copy(
                tasks = f.tasks.map { current ->
                    if (current.localKey == row.localKey) {
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

    fun submitValidateAndEnqueue(
        onEnqueuedNavigate: () -> Unit,
        onFinished: (SubmitOfflineResult) -> Unit,
    ) {
        val snapshot = _state.value
        val flight = snapshot.flight
        if (flight == null || snapshot.flightLoad != WorkOrderFlightLoadState.Ready) {
            onFinished(SubmitOfflineResult.Failed("Finish loading the flight before submitting."))
            return
        }
        val wo = flight.cachedMyWorkOrder
        if (wo == null) {
            onFinished(SubmitOfflineResult.Failed("Missing work order on this flight."))
            return
        }
        snapshot.existingAllowanceError?.let { message ->
            onFinished(SubmitOfflineResult.Failed(message))
            return
        }
        val form = normalizedFormIdentifiersPublic(snapshot.form)
        if (form.serviceLines.isEmpty() && form.tasks.isEmpty()) {
            _state.update {
                it.copy(
                    formLevelError = "Add at least one service line or task.",
                    submitFieldErrors = null,
                )
            }
            onFinished(SubmitOfflineResult.Failed("Add at least one service line or task."))
            return
        }
        val errors = validateLines(
            form,
            wo.actualArrivalUtc,
            wo.actualDepartureUtc,
            snapshot.catalogServices.allowedPerformedServiceIds(),
        )
        if (errors != null) {
            _state.update { it.copy(submitFieldErrors = errors, formLevelError = null) }
            onFinished(SubmitOfflineResult.Failed("Fix the highlighted fields."))
            return
        }

        val mutationId = UUID.randomUUID().toString()
        val flightKind = resolveFlightKind(flight)
        val payload = buildOutboxPayload(form)
        val attachments = collectAttachments(form)

        _state.update { it.copy(isSubmitting = true) }
        applicationScope.launch(Dispatchers.IO) {
            try {
                val request = EnqueueRequest(
                    clientMutationId = mutationId,
                    flightId = flight.id,
                    flightKind = flightKind,
                    clientFlightId = null,
                    payload = payload,
                    attachmentsToPersist = attachments,
                    knownServerWorkOrderId = wo.id,
                )
                outboxRepository.enqueue(request)
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
                            e.message ?: e.javaClass.simpleName,
                        ),
                    )
                }
            }
        }
    }

    private fun resolveFlightKind(flight: WorkOrderFlightRow): Int =
        when {
            flight.isPerLanding -> WorkOrderOutboxEntity.FLIGHT_KIND_PER_LANDING
            flight.isAdHoc -> WorkOrderOutboxEntity.FLIGHT_KIND_AD_HOC
            else -> WorkOrderOutboxEntity.FLIGHT_KIND_MY
        }

    private fun buildOutboxPayload(form: CreateWorkOrderFormState): OutboxPayload {
        val workOrder = OutboxPayload.WorkOrderInput(
            type = "Completion",
            actualFlightNumber = null,
            aircraftTypeId = null,
            aircraftTailNumber = null,
            ataIso = null,
            atdIso = null,
            remarks = null,
            serviceLines = form.serviceLines.map { row ->
                OutboxPayload.ServiceLineInput(
                    id = null,
                    serviceId = row.serviceId
                        ?: error("Service line missing serviceId"),
                    performedByStaffMemberIds = row.employeeIds,
                    fromIso = row.fromIso,
                    toIso = row.toIso,
                    description = row.description.takeIf { it.isNotBlank() },
                    attachments = row.attachments.map { attachment ->
                        attachment.toOutboxPlaceholder()
                    },
                    isReturnToRamp = row.returnToRamp,
                )
            },
            tasks = form.tasks.map { task ->
                OutboxPayload.TaskInput(
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
                    attachments = task.attachments.map { attachment ->
                        attachment.toOutboxPlaceholder()
                    },
                    isReturnToRamp = task.returnToRamp,
                )
            },
            customerSignaturePngBase64 = null,
        )
        return OutboxPayload(
            kind = OutboxPayload.Kind.ReturnToRamp,
            workOrder = workOrder,
            scratchFlight = null,
        )
    }

    private fun collectAttachments(form: CreateWorkOrderFormState): List<EnqueueAttachment> =
        collectAttachmentsForOutbox(form)

    private fun validateLines(
        form: CreateWorkOrderFormState,
        woAta: String?,
        woAtd: String?,
        allowedPerformedServiceIds: Set<String>,
    ): CreateWorkOrderSubmitFieldErrors? {
        val errors = computeWorkOrderLineErrors(form, woAta, woAtd, allowedPerformedServiceIds)
        return if (errors.services.isEmpty() && errors.tasks.isEmpty()) null
        else CreateWorkOrderSubmitFieldErrors(
            serviceLinesByKey = errors.services,
            tasksByKey = errors.tasks,
        )
    }

    companion object {
        /** Exposes normalisation used by the full work-order screen for consistent ids. */
        fun normalizedFormIdentifiersPublic(form: CreateWorkOrderFormState): CreateWorkOrderFormState =
            form.copy(
                flightNumber = normalizeWorkOrderFlightNumberInput(form.flightNumber),
                aircraftTailNumber = com.nags.operations.ui.util.normalizeWorkOrderAircraftTailInput(form.aircraftTailNumber),
            )
    }
}
