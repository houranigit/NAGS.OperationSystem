package com.nags.operations.ui.workorder

import com.nags.operations.data.TaskTypeKind
import com.nags.operations.ui.util.parseOffsetDateTime
import java.time.OffsetDateTime

internal object WorkOrderFormLimits {
    const val FlightNumber = 12
    const val AircraftTailNumber = 20
    const val Remarks = 2_000
    const val LineDescription = 2_000
    const val CancellationReason = 1_000
    const val TaskAttachments = 10
}

/** String values are persisted in draft JSON; keep names stable and tolerate Unknown legacy rows. */
internal object WorkOrderDraftSubmissionMode {
    const val Unknown = "Unknown"
    const val ForFlight = "ForFlight"
    const val ScratchAdHoc = "ScratchAdHoc"
    const val UpdateExisting = "UpdateExisting"

    fun isKnown(value: String): Boolean =
        value == ForFlight || value == ScratchAdHoc || value == UpdateExisting
}

/** Mirrors the portal's completion work-order wizard progression. */
internal enum class WorkOrderWizardStep {
    Flight,
    ServiceLines,
    Tasks,
    Signature,
}

internal fun resourceQuantity(quantities: Map<String, Double>, itemId: String): Double =
    quantities[itemId] ?: 1.0

internal fun quantitiesForSelection(
    selectedIds: List<String>,
    current: Map<String, Double>,
): Map<String, Double> = selectedIds.associateWith { id -> resourceQuantity(current, id) }

internal fun isValidResourceQuantity(value: Double): Boolean = value.isFinite() && value > 0.0

internal data class WorkOrderLineValidation(
    val services: Map<Long, ServiceLineSubmitFieldErrors>,
    val tasks: Map<Long, TaskLineSubmitFieldErrors>,
)

internal fun computeWorkOrderLineErrors(
    form: CreateWorkOrderFormState,
    ataIso: String?,
    atdIso: String?,
    allowedPerformedServiceIds: Set<String>,
): WorkOrderLineValidation {
    val ataDt = safeParseOffset(ataIso)
    val atdDt = safeParseOffset(atdIso)

    val serviceMap = LinkedHashMap<Long, ServiceLineSubmitFieldErrors>()
    form.serviceLines.forEach { row ->
        val service = when {
            row.serviceId.isNullOrBlank() -> "Service type is required."
            row.serviceId !in allowedPerformedServiceIds ->
                "This service is no longer allowed for your manpower type. Remove or replace it."
            else -> null
        }
        val performer = if (row.employeeIds.isEmpty() || row.employeeIds.any { it.isBlank() }) {
            "Choose at least one person."
        } else null
        var from = if (row.fromIso.isBlank()) "From date and time is required." else null
        var to = if (row.toIso.isBlank()) "To date and time is required." else null
        val description = if (row.description.trim().length > WorkOrderFormLimits.LineDescription) {
            "Description must be at most ${WorkOrderFormLimits.LineDescription} characters."
        } else null

        val fromDt = safeParseOffset(row.fromIso)
        val toDt = safeParseOffset(row.toIso)
        if (row.fromIso.isNotBlank() && fromDt == null) from = mergeValidationMessage(from, "Invalid From date or time.")
        if (row.toIso.isNotBlank() && toDt == null) to = mergeValidationMessage(to, "Invalid To date or time.")
        if (fromDt != null && toDt != null && toDt.isBefore(fromDt)) {
            to = mergeValidationMessage(to, "Must be on or after From.")
        }
        if (ataDt != null && fromDt != null && fromDt.isBefore(ataDt)) {
            from = mergeValidationMessage(from, "Can't be before actual arrival (ATA).")
        }
        if (atdDt != null && toDt != null && toDt.isAfter(atdDt)) {
            to = mergeValidationMessage(to, "Can't be after departure (ATD).")
        }

        if (service != null || performer != null || from != null || to != null || description != null) {
            serviceMap[row.localKey] = ServiceLineSubmitFieldErrors(
                serviceType = service,
                performer = performer,
                from = from,
                to = to,
                description = description,
            )
        }
    }

    val taskMap = LinkedHashMap<Long, TaskLineSubmitFieldErrors>()
    form.tasks.forEach { row ->
        val taskType = if (row.taskType != TaskTypeKind.Major && row.taskType != TaskTypeKind.Minor) {
            "Task type must be Major or Minor."
        } else null
        val performers = if (row.employeeIds.isEmpty() || row.employeeIds.any { it.isBlank() }) {
            "Choose at least one person."
        } else null
        var from = if (row.fromIso.isBlank()) "From date and time is required." else null
        var to = if (row.toIso.isBlank()) "To date and time is required." else null
        val description = if (row.description.trim().length > WorkOrderFormLimits.LineDescription) {
            "Description must be at most ${WorkOrderFormLimits.LineDescription} characters."
        } else null
        val tools = resourceRowsError(row.toolIds, row.toolQuantities, "Tool")
        val materials = resourceRowsError(row.materialIds, row.materialQuantities, "Material")
        val generalSupports = resourceRowsError(
            row.generalSupportIds,
            row.generalSupportQuantities,
            "General support",
        )
        val attachments = if (
            row.existingAttachmentNames.size + row.attachments.size > WorkOrderFormLimits.TaskAttachments
        ) {
            "A task can have at most ${WorkOrderFormLimits.TaskAttachments} attachments."
        } else null

        val fromDt = safeParseOffset(row.fromIso)
        val toDt = safeParseOffset(row.toIso)
        if (row.fromIso.isNotBlank() && fromDt == null) from = mergeValidationMessage(from, "Invalid From date or time.")
        if (row.toIso.isNotBlank() && toDt == null) to = mergeValidationMessage(to, "Invalid To date or time.")
        if (fromDt != null && toDt != null && toDt.isBefore(fromDt)) {
            to = mergeValidationMessage(to, "Must be on or after From.")
        }
        if (ataDt != null && fromDt != null && fromDt.isBefore(ataDt)) {
            from = mergeValidationMessage(from, "Can't be before actual arrival (ATA).")
        }
        if (atdDt != null && toDt != null && toDt.isAfter(atdDt)) {
            to = mergeValidationMessage(to, "Can't be after departure (ATD).")
        }

        if (
            taskType != null || performers != null || from != null || to != null || description != null ||
            tools != null || materials != null || generalSupports != null || attachments != null
        ) {
            taskMap[row.localKey] = TaskLineSubmitFieldErrors(
                taskType = taskType,
                performers = performers,
                from = from,
                to = to,
                description = description,
                tools = tools,
                materials = materials,
                generalSupports = generalSupports,
                attachments = attachments,
            )
        }
    }

    return WorkOrderLineValidation(serviceMap, taskMap)
}

internal fun computeCreateWorkOrderSubmitErrors(
    form: CreateWorkOrderFormState,
    dialogAtdIso: String?,
    isAdHocScratch: Boolean,
    selectedCustomerId: String?,
    allowedPerformedServiceIds: Set<String>,
): CreateWorkOrderSubmitFieldErrors? {
    val customer = if (isAdHocScratch && selectedCustomerId.isNullOrBlank()) "Customer is required." else null
    val normalizedFlightNumber = form.flightNumber.trim()
    val flightNumber = when {
        normalizedFlightNumber.isBlank() -> "Flight number is required."
        normalizedFlightNumber.length > WorkOrderFormLimits.FlightNumber ->
            "Flight number must be at most ${WorkOrderFormLimits.FlightNumber} characters."
        else -> null
    }
    val aircraft = if (form.aircraftTypeId.isNullOrBlank()) "Aircraft type is required." else null
    val tail = if (form.aircraftTailNumber.trim().length > WorkOrderFormLimits.AircraftTailNumber) {
        "Tail number must be at most ${WorkOrderFormLimits.AircraftTailNumber} characters."
    } else null
    val remarks = if (form.remarks.trim().length > WorkOrderFormLimits.Remarks) {
        "Remarks must be at most ${WorkOrderFormLimits.Remarks} characters."
    } else null

    var scheduledArrival: String? = null
    var scheduledDeparture: String? = null
    if (isAdHocScratch) {
        val sta = safeParseOffset(form.scheduledArrivalIso)
        val std = safeParseOffset(form.scheduledDepartureIso)
        scheduledArrival = when {
            form.scheduledArrivalIso.isBlank() -> "Scheduled arrival is required."
            sta == null -> "Invalid scheduled arrival date or time."
            else -> null
        }
        scheduledDeparture = when {
            form.scheduledDepartureIso.isBlank() -> "Scheduled departure is required."
            std == null -> "Invalid scheduled departure date or time."
            sta != null && !std.isAfter(sta) -> "Scheduled departure must be after scheduled arrival."
            else -> null
        }
    }

    var ata = if (form.ataIso.isBlank()) "ATA is required." else null
    val ataDt = safeParseOffset(form.ataIso)
    if (ata == null && ataDt == null) ata = "Invalid ATA date or time."

    val rawAtd = (dialogAtdIso ?: form.atdIso).trim()
    var atd = if (rawAtd.isBlank()) "ATD is required." else null
    val atdDt = safeParseOffset(rawAtd)
    if (atd == null && atdDt == null) atd = "Invalid ATD date or time."
    if (atd == null && ataDt != null && atdDt != null && atdDt.isBefore(ataDt)) {
        atd = "Departure (ATD) can't be before arrival (ATA)."
    }

    val lineErrors = computeWorkOrderLineErrors(
        form,
        form.ataIso,
        rawAtd,
        allowedPerformedServiceIds,
    )
    if (ataDt != null) {
        form.serviceLines.mapNotNull { safeParseOffset(it.fromIso) }.filter { ataDt.isAfter(it) }.forEach {
            ata = mergeValidationMessage(ata, "Can't be after a service line start time.")
        }
        form.tasks.mapNotNull { safeParseOffset(it.fromIso) }.filter { ataDt.isAfter(it) }.forEach {
            ata = mergeValidationMessage(ata, "Can't be after a task start time.")
        }
    }

    val hasProblems = customer != null || flightNumber != null || aircraft != null || tail != null ||
        scheduledArrival != null || scheduledDeparture != null || ata != null || atd != null || remarks != null ||
        lineErrors.services.isNotEmpty() || lineErrors.tasks.isNotEmpty()
    if (!hasProblems) return null

    return CreateWorkOrderSubmitFieldErrors(
        customer = customer,
        flightNumber = flightNumber,
        aircraftType = aircraft,
        aircraftTailNumber = tail,
        scheduledArrival = scheduledArrival,
        scheduledDeparture = scheduledDeparture,
        ata = ata,
        atd = atd,
        remarks = remarks,
        serviceLinesByKey = lineErrors.services,
        tasksByKey = lineErrors.tasks,
    )
}

internal fun isBlankSubmitErrors(errors: CreateWorkOrderSubmitFieldErrors): Boolean =
    errors.customer == null && errors.flightNumber == null && errors.aircraftType == null &&
        errors.aircraftTailNumber == null && errors.scheduledArrival == null &&
        errors.scheduledDeparture == null && errors.ata == null && errors.atd == null &&
        errors.remarks == null && errors.serviceLinesByKey.isEmpty() && errors.tasksByKey.isEmpty()

/** Keeps only errors rendered on the requested wizard step. */
internal fun submitErrorsForWizardStep(
    errors: CreateWorkOrderSubmitFieldErrors?,
    step: WorkOrderWizardStep,
): CreateWorkOrderSubmitFieldErrors? {
    if (errors == null || step == WorkOrderWizardStep.Signature) return null
    val filtered = when (step) {
        WorkOrderWizardStep.Flight -> errors.copy(
            serviceLinesByKey = emptyMap(),
            tasksByKey = emptyMap(),
        )
        WorkOrderWizardStep.ServiceLines -> CreateWorkOrderSubmitFieldErrors(
            serviceLinesByKey = errors.serviceLinesByKey,
        )
        WorkOrderWizardStep.Tasks -> CreateWorkOrderSubmitFieldErrors(
            tasksByKey = errors.tasksByKey,
        )
        WorkOrderWizardStep.Signature -> return null
    }
    return filtered.takeUnless(::isBlankSubmitErrors)
}

internal fun firstWizardStepWithErrors(
    errors: CreateWorkOrderSubmitFieldErrors,
): WorkOrderWizardStep = WorkOrderWizardStep.entries.firstOrNull {
    submitErrorsForWizardStep(errors, it) != null
} ?: WorkOrderWizardStep.Signature

private fun safeParseOffset(value: String?): OffsetDateTime? =
    value?.trim()?.takeIf { it.isNotEmpty() }?.let { runCatching { parseOffsetDateTime(it) }.getOrNull() }

private fun mergeValidationMessage(existing: String?, next: String): String = when {
    existing.isNullOrBlank() -> next
    existing.contains(next) -> existing
    else -> "$existing\n$next"
}

private fun resourceRowsError(
    selectedIds: List<String>,
    quantities: Map<String, Double>,
    label: String,
): String? = when {
    selectedIds.any { it.isBlank() } -> "Every ${label.lowercase()} row needs an item."
    selectedIds.any { !isValidResourceQuantity(resourceQuantity(quantities, it)) } ->
        "$label quantities must be greater than zero."
    else -> null
}
