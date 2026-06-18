package com.nags.operations.ui.workorder

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.nags.operations.data.WorkOrderStatusKind
import com.nags.operations.data.api.MobileApi
import com.nags.operations.data.db.entities.EmployeeEntity
import com.nags.operations.data.db.entities.GeneralSupportEntity
import com.nags.operations.data.db.entities.MaterialEntity
import com.nags.operations.data.db.entities.ServiceEntity
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
import com.nags.operations.ui.util.parseOffsetDateTime
import java.time.OffsetDateTime
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
    val loggedInEmployeeId: String? = null,
)

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
                _state.update { it.copy(loggedInEmployeeId = me.employeeId) }
                applyDefaultPerformers(_state.value)
            }
        }
        viewModelScope.launch {
            catalogsRepository.servicesFlow().collect { list ->
                _state.update { it.copy(catalogServices = list) }
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
        val underReview = wo != null &&
            WorkOrderStatusKind.fromValue(wo.status) == WorkOrderStatusKind.UnderReview
        if (!underReview) {
            _state.update {
                it.copy(
                    flightLoad = WorkOrderFlightLoadState.Error(
                        "Return to ramp needs your under-review work order on this flight. Create or update the work order first.",
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
            )
        }
        applyDefaultPerformers(_state.value)
    }

    private fun applyDefaultPerformers(snapshot: ReturnToRampUiState) {
        val empId = snapshot.loggedInEmployeeId ?: return
        val employees = snapshot.catalogEmployees
        if (employees.none { it.employeeId == empId }) return
        val form = snapshot.form
        val nextLines = form.serviceLines.map { line ->
            if (line.employeeId.isNullOrBlank()) line.copy(employeeId = empId) else line
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
        val preset = _state.value.loggedInEmployeeId
        updateForm {
            it.copy(
                serviceLines = it.serviceLines + ServiceLineFormRow(
                    localKey = allocKey(),
                    employeeId = preset,
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
            f.copy(serviceLines = f.serviceLines.map { if (it.localKey == row.localKey) row else it })
        }
    }

    fun addTask() {
        val preset = _state.value.loggedInEmployeeId?.let { listOf(it) } ?: emptyList()
        updateForm {
            it.copy(
                tasks = it.tasks + TaskFormRow(
                    localKey = allocKey(),
                    employeeIds = preset,
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
            f.copy(tasks = f.tasks.map { if (it.localKey == row.localKey) row else it })
        }
    }

    fun submitValidateAndEnqueue(
        onInstantNavigate: () -> Unit,
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
        val errors = validateLines(form, wo.ata, wo.atd)
        if (errors != null) {
            _state.update { it.copy(submitFieldErrors = errors, formLevelError = null) }
            onFinished(SubmitOfflineResult.Failed("Fix the highlighted fields."))
            return
        }

        val mutationId = UUID.randomUUID().toString()
        val flightKind = resolveFlightKind(flight)
        val payload = buildOutboxPayload(form)
        val attachments = collectAttachments(form)

        onInstantNavigate()
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
                    onFinished(SubmitOfflineResult.Enqueued(clientMutationId = mutationId))
                }
            } catch (e: Exception) {
                withContext(Dispatchers.Main.immediate) {
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
        when (flight.operationTypeCode.trim().uppercase()) {
            "AOG" -> WorkOrderOutboxEntity.FLIGHT_KIND_AOG
            "ADHOC" -> WorkOrderOutboxEntity.FLIGHT_KIND_AD_HOC
            else -> WorkOrderOutboxEntity.FLIGHT_KIND_MY
        }

    private fun buildOutboxPayload(form: CreateWorkOrderFormState): OutboxPayload {
        val workOrder = OutboxPayload.WorkOrderInput(
            flightNumber = "",
            aircraftTypeId = null,
            aircraftTailNumber = null,
            ata = null,
            atd = null,
            remarks = null,
            serviceLines = form.serviceLines.map { row ->
                OutboxPayload.ServiceLineInput(
                    serviceId = row.serviceId
                        ?: error("Service line missing serviceId"),
                    employeeId = row.employeeId
                        ?: error("Service line missing employeeId"),
                    fromIso = row.fromIso,
                    toIso = row.toIso,
                    description = row.description.takeIf { it.isNotBlank() },
                )
            },
            tasks = form.tasks.map { task ->
                OutboxPayload.TaskInput(
                    taskType = task.taskType,
                    description = task.description.takeIf { it.isNotBlank() },
                    fromIso = task.fromIso,
                    toIso = task.toIso,
                    employeeIds = task.employeeIds,
                    toolIds = task.toolIds,
                    materialIds = task.materialIds,
                    generalSupportIds = task.generalSupportIds,
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
            customerSignaturePngBase64 = null,
        )
        return OutboxPayload(
            kind = OutboxPayload.Kind.ReturnToRamp,
            workOrder = workOrder,
            scratchFlight = null,
        )
    }

    private fun collectAttachments(form: CreateWorkOrderFormState): List<EnqueueAttachment> =
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

    private fun validateLines(
        form: CreateWorkOrderFormState,
        woAta: String?,
        woAtd: String?,
    ): CreateWorkOrderSubmitFieldErrors? {
        fun safeParse(iso: String?): OffsetDateTime? =
            iso?.trim()?.takeIf { it.isNotEmpty() }?.let { runCatching { parseOffsetDateTime(it) }.getOrNull() }

        val ataDt = safeParse(woAta)
        val atdDt = safeParse(woAtd)

        fun mergeMsg(a: String?, b: String): String = when {
            a.isNullOrBlank() -> b
            a.contains(b) -> a
            else -> "$a\n$b"
        }

        val serviceMap = LinkedHashMap<Long, ServiceLineSubmitFieldErrors>()
        form.serviceLines.forEach { row ->
            var st = if (row.serviceId.isNullOrBlank()) "Service type is required." else null
            var perf = if (row.employeeId.isNullOrBlank()) "Performed by is required." else null
            var fromE = if (row.fromIso.isBlank()) "From date and time is required." else null
            var toE = if (row.toIso.isBlank()) "To date and time is required." else null

            val fromDt = row.fromIso.takeIf { it.isNotBlank() }?.let { safeParse(it) }
            val toDt = row.toIso.takeIf { it.isNotBlank() }?.let { safeParse(it) }
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
            val fromDt = row.fromIso.takeIf { it.isNotBlank() }?.let { safeParse(it) }
            val toDt = row.toIso.takeIf { it.isNotBlank() }?.let { safeParse(it) }
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
            if (atdDt != null && toDt != null && toDt.isAfter(atdDt)) {
                toE = mergeMsg(toE, "Can't be after departure (ATD).")
            }
            if (performers != null || fromE != null || toE != null) {
                taskMap[row.localKey] = TaskLineSubmitFieldErrors(performers, fromE, toE)
            }
        }

        val hasProblems = serviceMap.isNotEmpty() || taskMap.isNotEmpty()
        return if (!hasProblems) null
        else CreateWorkOrderSubmitFieldErrors(
            serviceLinesByKey = serviceMap,
            tasksByKey = taskMap,
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
