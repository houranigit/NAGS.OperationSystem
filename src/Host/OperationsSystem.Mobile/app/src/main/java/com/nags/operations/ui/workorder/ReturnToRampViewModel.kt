package com.nags.operations.ui.workorder

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.nags.operations.data.ResourceCalculationType
import com.nags.operations.data.api.MobileApi
import com.nags.operations.data.db.entities.EmployeeEntity
import com.nags.operations.data.db.entities.GeneralSupportEntity
import com.nags.operations.data.db.entities.MaterialEntity
import com.nags.operations.data.db.entities.ServiceEntity
import com.nags.operations.data.db.entities.ToolEntity
import com.nags.operations.data.db.entities.WorkOrderOutboxEntity
import com.nags.operations.data.db.entities.allowedPerformedServiceIds
import com.nags.operations.data.outbox.EnqueueRequest
import com.nags.operations.data.outbox.OutboxPayload
import com.nags.operations.data.outbox.WorkOrderOutboxRepository
import com.nags.operations.data.repo.CatalogsRepository
import com.nags.operations.data.repo.EmployeesRepository
import com.nags.operations.data.repo.FlightsRepository
import com.nags.operations.data.repo.WorkOrderFlightRow
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
    val occurrence: ReturnToRampFormRow? = null,
    val submitErrors: ReturnToRampSubmitFieldErrors? = null,
    val loggedInEmployeeId: String? = null,
    val isSubmitting: Boolean = false,
)

/** Append-only editor for one canonical, independently auditable RTR occurrence. */
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
                applyDefaultPerformers()
            }
        }
        viewModelScope.launch {
            catalogsRepository.servicesFlow().collect { list ->
                _state.update { it.copy(catalogServices = list.filterNot(ServiceEntity::isAircraftPerLanding)) }
            }
        }
        viewModelScope.launch {
            employeesRepository.observe().collect { list ->
                _state.update { it.copy(catalogEmployees = list) }
                applyDefaultPerformers()
            }
        }
        viewModelScope.launch {
            catalogsRepository.toolsFlow().collect { list -> _state.update { it.copy(catalogTools = list) } }
        }
        viewModelScope.launch {
            catalogsRepository.materialsFlow().collect { list -> _state.update { it.copy(catalogMaterials = list) } }
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
        val workOrder = row.cachedMyWorkOrder
        val from = workOrder?.actualArrivalUtc ?: row.sta
        val to = workOrder?.actualDepartureUtc ?: row.std
        _state.update {
            it.copy(
                flightLoad = WorkOrderFlightLoadState.Ready,
                flight = row,
                occurrence = ReturnToRampFormRow(
                    localKey = allocKey(),
                    fromIso = from,
                    toIso = to,
                ),
                submitErrors = null,
            )
        }
        applyDefaultPerformers()
    }

    private fun applyDefaultPerformers() {
        val snapshot = _state.value
        val employeeId = snapshot.loggedInEmployeeId ?: return
        if (snapshot.catalogEmployees.none { it.staffMemberId == employeeId }) return
        updateOccurrenceInternal { occurrence ->
            occurrence.copy(
                serviceLines = occurrence.serviceLines.map { line ->
                    if (line.employeeIds.isEmpty()) line.copy(employeeIds = listOf(employeeId)) else line
                },
                tasks = occurrence.tasks.map { task ->
                    if (task.employeeIds.isEmpty()) task.copy(employeeIds = listOf(employeeId)) else task
                },
            )
        }
    }

    fun updateOccurrence(transform: (ReturnToRampFormRow) -> ReturnToRampFormRow) {
        updateOccurrenceInternal(transform)
    }

    private fun updateOccurrenceInternal(transform: (ReturnToRampFormRow) -> ReturnToRampFormRow) {
        _state.update { state ->
            val current = state.occurrence ?: return@update state
            state.copy(
                occurrence = current.mergeNonAttachmentEdit(transform(current)),
                submitErrors = null,
            )
        }
    }

    private fun defaultEmployeeIds(): List<String> = _state.value.loggedInEmployeeId
        ?.takeIf { id -> _state.value.catalogEmployees.any { it.staffMemberId == id } }
        ?.let(::listOf)
        .orEmpty()

    fun addServiceLine() = updateOccurrence { occurrence ->
        occurrence.copy(
            serviceLines = occurrence.serviceLines + ServiceLineFormRow(
                localKey = allocKey(),
                employeeIds = defaultEmployeeIds(),
                fromIso = occurrence.fromIso,
                toIso = occurrence.toIso,
            ),
        )
    }

    fun removeServiceLine(key: Long) = updateOccurrence { occurrence ->
        occurrence.copy(serviceLines = occurrence.serviceLines.filterNot { it.localKey == key })
    }

    fun replaceServiceLine(row: ServiceLineFormRow) = updateOccurrence { occurrence ->
        occurrence.copy(serviceLines = occurrence.serviceLines.map { current ->
            if (current.localKey == row.localKey) current.mergeNonAttachmentEdit(row) else current
        })
    }

    fun addServiceLineAttachment(key: Long, attachment: TaskAttachmentDraft) {
        _state.update { state ->
            val occurrence = state.occurrence ?: return@update state
            val form = CreateWorkOrderFormState(returnToRamps = listOf(occurrence))
                .withReturnToRampServiceAttachmentAdded(occurrence.localKey, key, attachment)
            state.copy(occurrence = form.returnToRamps.single(), submitErrors = null)
        }
    }

    fun removeServiceLineAttachment(key: Long, attachment: TaskAttachmentDraft) {
        _state.update { state ->
            val occurrence = state.occurrence ?: return@update state
            val form = CreateWorkOrderFormState(returnToRamps = listOf(occurrence))
                .withReturnToRampServiceAttachmentRemoved(occurrence.localKey, key, attachment)
            state.copy(occurrence = form.returnToRamps.single(), submitErrors = null)
        }
    }

    fun addTask() = updateOccurrence { occurrence ->
        occurrence.copy(
            tasks = occurrence.tasks + TaskFormRow(
                localKey = allocKey(),
                employeeIds = defaultEmployeeIds(),
                fromIso = occurrence.fromIso,
                toIso = occurrence.toIso,
            ),
        )
    }

    fun removeTask(key: Long) = updateOccurrence { occurrence ->
        occurrence.copy(tasks = occurrence.tasks.filterNot { it.localKey == key })
    }

    fun replaceTask(row: TaskFormRow) = updateOccurrence { occurrence ->
        occurrence.copy(tasks = occurrence.tasks.map { current ->
            if (current.localKey == row.localKey) current.mergeNonAttachmentEdit(row) else current
        })
    }

    fun addTaskAttachment(key: Long, attachment: TaskAttachmentDraft) {
        _state.update { state ->
            val occurrence = state.occurrence ?: return@update state
            val form = CreateWorkOrderFormState(returnToRamps = listOf(occurrence))
                .withReturnToRampTaskAttachmentAdded(occurrence.localKey, key, attachment)
            state.copy(occurrence = form.returnToRamps.single(), submitErrors = null)
        }
    }

    fun removeTaskAttachment(key: Long, attachment: TaskAttachmentDraft) {
        _state.update { state ->
            val occurrence = state.occurrence ?: return@update state
            val form = CreateWorkOrderFormState(returnToRamps = listOf(occurrence))
                .withReturnToRampTaskAttachmentRemoved(occurrence.localKey, key, attachment)
            state.copy(occurrence = form.returnToRamps.single(), submitErrors = null)
        }
    }

    fun submitValidateAndEnqueue(
        onEnqueuedNavigate: () -> Unit,
        onFinished: (SubmitOfflineResult) -> Unit,
    ) {
        val snapshot = _state.value
        val flight = snapshot.flight
        val occurrence = snapshot.occurrence
        if (flight == null || occurrence == null || snapshot.flightLoad != WorkOrderFlightLoadState.Ready) {
            onFinished(SubmitOfflineResult.Failed("Finish loading the flight before submitting."))
            return
        }
        val errors = computeReturnToRampErrors(
            occurrence,
            snapshot.catalogServices.allowedPerformedServiceIds(),
        )
        if (errors != null) {
            _state.update { it.copy(submitErrors = errors) }
            onFinished(SubmitOfflineResult.Failed("Fix the highlighted fields."))
            return
        }

        val mutationId = UUID.randomUUID().toString()
        val payload = OutboxPayload(
            kind = OutboxPayload.Kind.ReturnToRamp,
            // Null workOrder is deliberate: completed flights need no caller-owned editable WO.
            workOrder = null,
            returnToRamp = occurrence.toOutboxInput(snapshot),
        )
        val attachmentForm = CreateWorkOrderFormState(returnToRamps = listOf(occurrence))
        val request = EnqueueRequest(
            clientMutationId = mutationId,
            flightId = flight.id,
            flightKind = resolveFlightKind(flight),
            clientFlightId = null,
            payload = payload,
            attachmentsToPersist = collectAttachmentsForOutbox(attachmentForm),
            knownServerWorkOrderId = null,
        )
        _state.update { it.copy(isSubmitting = true) }
        applicationScope.launch(Dispatchers.IO) {
            try {
                outboxRepository.enqueue(request)
                withContext(Dispatchers.Main.immediate) {
                    _state.update { it.copy(isSubmitting = false) }
                    onEnqueuedNavigate()
                    onFinished(SubmitOfflineResult.Enqueued(mutationId))
                }
            } catch (exception: Exception) {
                withContext(Dispatchers.Main.immediate) {
                    _state.update { it.copy(isSubmitting = false) }
                    onFinished(SubmitOfflineResult.Failed(exception.message ?: exception.javaClass.simpleName))
                }
            }
        }
    }

    private fun resolveFlightKind(flight: WorkOrderFlightRow): Int = when {
        flight.isPerLanding -> WorkOrderOutboxEntity.FLIGHT_KIND_PER_LANDING
        flight.isAdHoc -> WorkOrderOutboxEntity.FLIGHT_KIND_AD_HOC
        else -> WorkOrderOutboxEntity.FLIGHT_KIND_MY
    }

    private fun ReturnToRampFormRow.toOutboxInput(
        snapshot: ReturnToRampUiState,
    ) = OutboxPayload.ReturnToRampInput(
        id = null,
        fromIso = fromIso,
        toIso = toIso,
        description = description.takeIf { it.isNotBlank() },
        serviceLines = serviceLines.map { row ->
            OutboxPayload.ServiceLineInput(
                serviceId = row.serviceId ?: error("Service line missing serviceId"),
                performedByStaffMemberIds = row.employeeIds,
                fromIso = row.fromIso,
                toIso = row.toIso,
                description = row.description.takeIf { it.isNotBlank() },
                attachments = row.attachments.map(TaskAttachmentDraft::toOutboxPlaceholder),
                isReturnToRamp = false,
            )
        },
        tasks = tasks.map { it.toOutboxInput(snapshot) },
    )

    private fun TaskFormRow.toOutboxInput(snapshot: ReturnToRampUiState) = OutboxPayload.TaskInput(
        taskType = taskType,
        description = description.takeIf { it.isNotBlank() },
        fromIso = fromIso,
        toIso = toIso,
        employeeIds = employeeIds,
        tools = toolIds.map { id ->
            resourceUsage(
                id,
                toolUsages,
                toolQuantities,
                snapshot.catalogTools.firstOrNull { it.toolId == id }?.calculationType
                    ?: ResourceCalculationType.Duration,
                fromIso,
                toIso,
            ).toOutboxInput(id)
        },
        materials = materialIds.map { id ->
            resourceUsage(
                id,
                materialUsages,
                materialQuantities,
                snapshot.catalogMaterials.firstOrNull { it.materialId == id }?.calculationType
                    ?: ResourceCalculationType.Quantity,
                fromIso,
                toIso,
            ).toOutboxInput(id)
        },
        generalSupports = generalSupportIds.map { id ->
            resourceUsage(
                id,
                generalSupportUsages,
                generalSupportQuantities,
                snapshot.catalogGeneralSupports.firstOrNull { it.generalSupportId == id }?.calculationType
                    ?: ResourceCalculationType.Quantity,
                fromIso,
                toIso,
            ).toOutboxInput(id)
        },
        attachments = attachments.map(TaskAttachmentDraft::toOutboxPlaceholder),
        isReturnToRamp = false,
    )

    private fun ResourceUsageForm.toOutboxInput(itemId: String) =
        if (calculationType == ResourceCalculationType.Quantity) {
            OutboxPayload.ResourceInput(itemId = itemId, quantity = quantity)
        } else {
            OutboxPayload.ResourceInput(
                itemId = itemId,
                quantity = null,
                fromIso = fromIso,
                toIso = toIso?.takeIf { it.isNotBlank() },
            )
        }
}
