package com.nags.operations.ui.workorder

import com.nags.operations.data.TaskTypeKind
import com.nags.operations.data.ResourceCalculationType
import com.nags.operations.data.WellKnownMasterDataIds
import com.nags.operations.ui.util.parseOffsetDateTime
import java.math.BigDecimal
import java.time.Instant
import java.time.OffsetDateTime

internal object WorkOrderFormLimits {
    const val FlightNumber = 12
    const val AircraftTailNumber = 20
    const val Remarks = 2_000
    const val LineDescription = 2_000
    const val CancellationReason = 1_000
    const val AttachmentsPerLine = 10
    const val ServiceAttachments = AttachmentsPerLine
    const val TaskAttachments = AttachmentsPerLine
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
enum class WorkOrderWizardStep {
    Flight,
    ServiceLines,
    Tasks,
    ReturnToRamps,
    Signature,
}

/**
 * Return-to-ramp occurrences are authored from the dedicated mobile flow after creation. Existing
 * editable work orders retain the step so their already-recorded occurrences remain reviewable.
 */
internal fun workOrderWizardSteps(
    includeReturnToRamps: Boolean,
): List<WorkOrderWizardStep> = if (includeReturnToRamps) {
    WorkOrderWizardStep.entries
} else {
    WorkOrderWizardStep.entries.filterNot { it == WorkOrderWizardStep.ReturnToRamps }
}

internal fun nextWorkOrderWizardStep(
    currentStep: WorkOrderWizardStep,
    includeReturnToRamps: Boolean,
): WorkOrderWizardStep? {
    val steps = workOrderWizardSteps(includeReturnToRamps)
    val currentIndex = steps.indexOf(currentStep)
    return if (currentIndex < 0) null else steps.getOrNull(currentIndex + 1)
}

internal fun previousWorkOrderWizardStep(
    currentStep: WorkOrderWizardStep,
    includeReturnToRamps: Boolean,
): WorkOrderWizardStep? {
    val steps = workOrderWizardSteps(includeReturnToRamps)
    val currentIndex = steps.indexOf(currentStep)
    return if (currentIndex <= 0) null else steps[currentIndex - 1]
}

internal fun isEarlierWorkOrderWizardStep(
    candidate: WorkOrderWizardStep,
    currentStep: WorkOrderWizardStep,
    includeReturnToRamps: Boolean,
): Boolean {
    val steps = workOrderWizardSteps(includeReturnToRamps)
    val candidateIndex = steps.indexOf(candidate)
    val currentIndex = steps.indexOf(currentStep)
    return candidateIndex >= 0 && currentIndex >= 0 && candidateIndex < currentIndex
}

private fun ServiceLineFormRow.isExactCanonicalAliasOf(
    occurrences: List<ReturnToRampFormRow>,
): Boolean {
    val identity = serverId?.takeIf { it.isNotBlank() } ?: return false
    val matches = occurrences
        .filterNot { it.serverId.isNullOrBlank() }
        .flatMap(ReturnToRampFormRow::serviceLines)
        .filter { it.serverId == identity }
    if (matches.size != 1) return false
    val canonical = matches.single()
    return copy(localKey = canonical.localKey, returnToRamp = false) ==
        canonical.copy(returnToRamp = false)
}

private fun TaskFormRow.isExactCanonicalAliasOf(
    occurrences: List<ReturnToRampFormRow>,
): Boolean {
    val identity = serverId?.takeIf { it.isNotBlank() } ?: return false
    val matches = occurrences
        .filterNot { it.serverId.isNullOrBlank() }
        .flatMap(ReturnToRampFormRow::tasks)
        .filter { it.serverId == identity }
    if (matches.size != 1) return false
    val canonical = matches.single()
    return copy(localKey = canonical.localKey, returnToRamp = false) ==
        canonical.copy(returnToRamp = false)
}

/**
 * Normalizes both the current grouped model and drafts from the former flat `returnToRamp` model.
 * Creation drops all hidden RTR activity. Update mode removes compatibility aliases; idless
 * unsaved legacy rows can be grouped locally, while server-owned rows require canonical identity
 * reconciliation before this function is called.
 */
internal fun formForWorkOrderWizardMode(
    form: CreateWorkOrderFormState,
    includeReturnToRamps: Boolean,
): CreateWorkOrderFormState {
    val legacyServiceLines = form.serviceLines.filter(ServiceLineFormRow::returnToRamp)
    val legacyTasks = form.tasks.filter(TaskFormRow::returnToRamp)
    val normalServiceLines = form.serviceLines.filterNot(ServiceLineFormRow::returnToRamp)
    val normalTasks = form.tasks.filterNot(TaskFormRow::returnToRamp)

    if (!includeReturnToRamps) {
        if (
            form.returnToRamps.isEmpty() &&
            legacyServiceLines.isEmpty() &&
            legacyTasks.isEmpty()
        ) return form
        return form.copy(
            serviceLines = normalServiceLines,
            tasks = normalTasks,
            returnToRamps = emptyList(),
        )
    }

    if (returnToRampRowsNeedIdentityResolution(form)) return form

    // Exact flat duplicates came from a short-lived compatibility response and can be removed.
    // Any remaining idless rows are unsaved work and must be grouped rather than discarded.
    val legacyServiceLinesToGroup = legacyServiceLines.filterNot {
        it.isExactCanonicalAliasOf(form.returnToRamps)
    }
    val legacyTasksToGroup = legacyTasks.filterNot {
        it.isExactCanonicalAliasOf(form.returnToRamps)
    }
    val normalizedOccurrences = form.returnToRamps.map { occurrence ->
        occurrence.copy(
            serviceLines = occurrence.serviceLines.map { it.copy(returnToRamp = false) },
            tasks = occurrence.tasks.map { it.copy(returnToRamp = false) },
        )
    }
    if (legacyServiceLinesToGroup.isEmpty() && legacyTasksToGroup.isEmpty()) {
        val normalized = form.copy(
            serviceLines = normalServiceLines,
            tasks = normalTasks,
            returnToRamps = normalizedOccurrences,
        )
        return if (normalized == form) form else normalized
    }

    val fromFallback = form.ataIso.ifBlank { form.scheduledArrivalIso }
    val toFallback = form.atdIso.ifBlank { form.scheduledDepartureIso }.ifBlank { fromFallback }
    val usedKeys = buildSet {
        addAll(form.serviceLines.map(ServiceLineFormRow::localKey))
        addAll(form.tasks.map(TaskFormRow::localKey))
        form.returnToRamps.forEach { occurrence ->
            add(occurrence.localKey)
            addAll(occurrence.serviceLines.map(ServiceLineFormRow::localKey))
            addAll(occurrence.tasks.map(TaskFormRow::localKey))
        }
    }
    val occurrenceKey = ((usedKeys.maxOrNull() ?: 0L) + 1L)
        .takeUnless { it in usedKeys }
        ?: -1L
    val fromCandidates = legacyServiceLinesToGroup.map(ServiceLineFormRow::fromIso) +
        legacyTasksToGroup.map(TaskFormRow::fromIso)
    val toCandidates = legacyServiceLinesToGroup.map(ServiceLineFormRow::toIso) +
        legacyTasksToGroup.map(TaskFormRow::toIso)
    val migratedOccurrence = ReturnToRampFormRow(
        localKey = occurrenceKey,
        fromIso = earliestWorkOrderTimestamp(fromCandidates, fromFallback),
        toIso = latestWorkOrderTimestamp(toCandidates, toFallback),
        serviceLines = legacyServiceLinesToGroup.map { it.copy(returnToRamp = false) },
        tasks = legacyTasksToGroup.map { it.copy(returnToRamp = false) },
    )
    return form.copy(
        serviceLines = normalServiceLines,
        tasks = normalTasks,
        returnToRamps = normalizedOccurrences + migratedOccurrence,
    )
}

internal fun returnToRampRowsNeedIdentityResolution(
    form: CreateWorkOrderFormState,
): Boolean {
    val unresolvedFlatServiceLine = form.serviceLines.any { row ->
        row.returnToRamp &&
            (row.serverId != null || row.existingAttachmentNames.isNotEmpty()) &&
            !row.isExactCanonicalAliasOf(form.returnToRamps)
    }
    val unresolvedFlatTask = form.tasks.any { row ->
        row.returnToRamp &&
            (row.serverId != null || row.existingAttachmentNames.isNotEmpty()) &&
            !row.isExactCanonicalAliasOf(form.returnToRamps)
    }
    val unresolvedGroupedOccurrence = form.returnToRamps.any { occurrence ->
        occurrence.serverId.isNullOrBlank() && (
            occurrence.serviceLines.any { row ->
                row.serverId != null || row.existingAttachmentNames.isNotEmpty()
            } ||
                occurrence.tasks.any { row ->
                    row.serverId != null || row.existingAttachmentNames.isNotEmpty()
                }
            )
    }
    return unresolvedFlatServiceLine || unresolvedFlatTask || unresolvedGroupedOccurrence
}

/**
 * Restores parent occurrence ids for drafts written by the former flat RTR model. Returns null
 * unless every legacy child can be matched to exactly one current canonical occurrence.
 */
internal fun reconcileLegacyReturnToRampRows(
    form: CreateWorkOrderFormState,
    canonicalOccurrences: List<ReturnToRampFormRow>,
): CreateWorkOrderFormState? {
    if (!returnToRampRowsNeedIdentityResolution(form)) return form
    if (canonicalOccurrences.isEmpty() || canonicalOccurrences.any { it.serverId.isNullOrBlank() }) {
        return null
    }

    val unsafeGroupedOccurrences = form.returnToRamps.filter { occurrence ->
        occurrence.serverId.isNullOrBlank()
    }
    val hasServerOwnedFlatRows =
        form.serviceLines.any { row ->
            row.returnToRamp && (row.serverId != null || row.existingAttachmentNames.isNotEmpty())
        } ||
            form.tasks.any { row ->
                row.returnToRamp && (row.serverId != null || row.existingAttachmentNames.isNotEmpty())
            }
    if (
        form.returnToRamps.any { !it.serverId.isNullOrBlank() } &&
        (unsafeGroupedOccurrences.isNotEmpty() || hasServerOwnedFlatRows)
    ) return null
    val legacyServiceLines = form.serviceLines.filter(ServiceLineFormRow::returnToRamp) +
        unsafeGroupedOccurrences.flatMap(ReturnToRampFormRow::serviceLines)
    val legacyTasks = form.tasks.filter(TaskFormRow::returnToRamp) +
        unsafeGroupedOccurrences.flatMap(ReturnToRampFormRow::tasks)
    val legacyServiceIds = legacyServiceLines.mapNotNull(ServiceLineFormRow::serverId)
    val legacyTaskIds = legacyTasks.mapNotNull(TaskFormRow::serverId)
    if (
        legacyServiceIds.size != legacyServiceLines.size ||
        legacyTaskIds.size != legacyTasks.size ||
        legacyServiceIds.distinct().size != legacyServiceIds.size ||
        legacyTaskIds.distinct().size != legacyTaskIds.size
    ) return null

    val canonicalServiceIdList = canonicalOccurrences.flatMap { occurrence ->
        occurrence.serviceLines.mapNotNull(ServiceLineFormRow::serverId)
    }
    val canonicalTaskIdList = canonicalOccurrences.flatMap { occurrence ->
        occurrence.tasks.mapNotNull(TaskFormRow::serverId)
    }
    if (
        canonicalServiceIdList.distinct().size != canonicalServiceIdList.size ||
        canonicalTaskIdList.distinct().size != canonicalTaskIdList.size
    ) return null
    val canonicalServiceIds = canonicalServiceIdList.toSet()
    val canonicalTaskIds = canonicalTaskIdList.toSet()
    if (!canonicalServiceIds.containsAll(legacyServiceIds) || !canonicalTaskIds.containsAll(legacyTaskIds)) {
        return null
    }

    val reconciledOccurrences = canonicalOccurrences.map { occurrence ->
        val serviceIds = occurrence.serviceLines.mapNotNull(ServiceLineFormRow::serverId).toSet()
        val taskIds = occurrence.tasks.mapNotNull(TaskFormRow::serverId).toSet()
        occurrence.copy(
            serviceLines = legacyServiceLines
                .filter { it.serverId in serviceIds }
                .map { it.copy(returnToRamp = false) },
            tasks = legacyTasks
                .filter { it.serverId in taskIds }
                .map { it.copy(returnToRamp = false) },
        )
    }
    return form.copy(
        serviceLines = form.serviceLines.filterNot(ServiceLineFormRow::returnToRamp),
        tasks = form.tasks.filterNot(TaskFormRow::returnToRamp),
        returnToRamps = reconciledOccurrences,
    )
}

private fun earliestWorkOrderTimestamp(values: List<String>, fallback: String): String =
    values.filter(String::isNotBlank).minByOrNull { value ->
        safeParseOffset(value)?.toInstant() ?: Instant.MAX
    } ?: fallback

private fun latestWorkOrderTimestamp(values: List<String>, fallback: String): String =
    values.filter(String::isNotBlank).maxByOrNull { value ->
        safeParseOffset(value)?.toInstant() ?: Instant.MIN
    } ?: fallback

internal enum class WorkOrderValidationPhase {
    BeforeAtd,
    Submission,
}

internal fun resourceQuantity(quantities: Map<String, Double>, itemId: String): Double =
    quantities[itemId] ?: 1.0

internal fun quantitiesForSelection(
    selectedIds: List<String>,
    current: Map<String, Double>,
): Map<String, Double> = selectedIds.associateWith { id -> resourceQuantity(current, id) }

internal fun resourceUsage(
    itemId: String,
    usages: Map<String, ResourceUsageForm>,
    legacyQuantities: Map<String, Double>,
    calculationType: ResourceCalculationType,
    taskFromIso: String,
    taskToIso: String,
): ResourceUsageForm = usages[itemId] ?: when (calculationType) {
    ResourceCalculationType.Quantity -> ResourceUsageForm(
        calculationType = calculationType,
        quantity = resourceQuantity(legacyQuantities, itemId),
    )
    ResourceCalculationType.Duration -> ResourceUsageForm(
        calculationType = calculationType,
        quantity = null,
        fromIso = taskFromIso,
        toIso = taskToIso.takeIf { it.isNotBlank() },
    )
}

internal fun usagesForSelection(
    selectedIds: List<String>,
    current: Map<String, ResourceUsageForm>,
    legacyQuantities: Map<String, Double>,
    calculationTypes: Map<String, ResourceCalculationType>,
    defaultCalculationType: ResourceCalculationType,
    taskFromIso: String,
    taskToIso: String,
): Map<String, ResourceUsageForm> = selectedIds.associateWith { id ->
    resourceUsage(
        id,
        current,
        legacyQuantities,
        calculationTypes[id] ?: defaultCalculationType,
        taskFromIso,
        taskToIso,
    )
}

internal fun isValidResourceQuantity(value: Double): Boolean {
    if (!value.isFinite() || value <= 0.0) return false
    val decimal = BigDecimal.valueOf(value).stripTrailingZeros()
    val scale = maxOf(decimal.scale(), 0)
    val wholeDigits = decimal.precision() - decimal.scale()
    return scale <= 2 && wholeDigits <= 16
}

internal fun isBlankOrUnknownCustomer(customerId: String?): Boolean {
    val normalized = customerId?.trim()
    return normalized.isNullOrEmpty() ||
        normalized.equals(WellKnownMasterDataIds.UnknownCustomer, ignoreCase = true)
}

internal fun resolveScratchCustomerId(selectedCustomerId: String?): String =
    selectedCustomerId?.trim()?.takeIf(String::isNotEmpty)
        ?: WellKnownMasterDataIds.UnknownCustomer

internal data class WorkOrderLineValidation(
    val services: Map<Long, ServiceLineSubmitFieldErrors>,
    val tasks: Map<Long, TaskLineSubmitFieldErrors>,
)

internal fun computeWorkOrderLineErrors(
    form: CreateWorkOrderFormState,
    ataIso: String?,
    atdIso: String?,
    allowedPerformedServiceIds: Set<String>,
    lowerBoundLabel: String = "actual arrival (ATA)",
    upperBoundLabel: String = "departure (ATD)",
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
        val attachments = if (
            row.existingAttachmentNames.size + row.attachments.size >
            WorkOrderFormLimits.ServiceAttachments
        ) {
            "A service can have at most ${WorkOrderFormLimits.ServiceAttachments} attachments."
        } else null

        val fromDt = safeParseOffset(row.fromIso)
        val toDt = safeParseOffset(row.toIso)
        if (row.fromIso.isNotBlank() && fromDt == null) from = mergeValidationMessage(from, "Invalid From date or time.")
        if (row.toIso.isNotBlank() && toDt == null) to = mergeValidationMessage(to, "Invalid To date or time.")
        if (fromDt != null && toDt != null && toDt.isBefore(fromDt)) {
            to = mergeValidationMessage(to, "Must be on or after From.")
        }
        if (ataDt != null && fromDt != null && fromDt.isBefore(ataDt)) {
            from = mergeValidationMessage(from, "Can't be before $lowerBoundLabel.")
        }
        if (atdDt != null && toDt != null && toDt.isAfter(atdDt)) {
            to = mergeValidationMessage(to, "Can't be after $upperBoundLabel.")
        }

        if (
            service != null || performer != null || from != null || to != null ||
            description != null || attachments != null
        ) {
            serviceMap[row.localKey] = ServiceLineSubmitFieldErrors(
                serviceType = service,
                performer = performer,
                from = from,
                to = to,
                description = description,
                attachments = attachments,
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
        val tools = resourceRowsError(
            row.toolIds,
            row.toolUsages,
            row.toolQuantities,
            ResourceCalculationType.Duration,
            "Tool",
            row.fromIso,
            row.toIso,
        )
        val materials = resourceRowsError(
            row.materialIds,
            row.materialUsages,
            row.materialQuantities,
            ResourceCalculationType.Quantity,
            "Material",
            row.fromIso,
            row.toIso,
        )
        val generalSupports = resourceRowsError(
            row.generalSupportIds,
            row.generalSupportUsages,
            row.generalSupportQuantities,
            ResourceCalculationType.Quantity,
            "General support",
            row.fromIso,
            row.toIso,
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
            from = mergeValidationMessage(from, "Can't be before $lowerBoundLabel.")
        }
        if (atdDt != null && toDt != null && toDt.isAfter(atdDt)) {
            to = mergeValidationMessage(to, "Can't be after $upperBoundLabel.")
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

internal fun computeReturnToRampErrors(
    row: ReturnToRampFormRow,
    allowedPerformedServiceIds: Set<String>,
): ReturnToRampSubmitFieldErrors? {
    var from = if (row.fromIso.isBlank()) "From date and time is required." else null
    var to = if (row.toIso.isBlank()) "To date and time is required." else null
    val fromDt = safeParseOffset(row.fromIso)
    val toDt = safeParseOffset(row.toIso)
    if (row.fromIso.isNotBlank() && fromDt == null) from = "Invalid From date or time."
    if (row.toIso.isNotBlank() && toDt == null) to = "Invalid To date or time."
    if (fromDt != null && toDt != null && toDt.isBefore(fromDt)) {
        to = mergeValidationMessage(to, "Must be on or after From.")
    }
    val description = if (row.description.trim().length > WorkOrderFormLimits.LineDescription) {
        "Description must be at most ${WorkOrderFormLimits.LineDescription} characters."
    } else null
    val activity = if (row.serviceLines.isEmpty() && row.tasks.isEmpty()) {
        "Add at least one service or task."
    } else null
    val nested = computeWorkOrderLineErrors(
        form = CreateWorkOrderFormState(
            serviceLines = row.serviceLines,
            tasks = row.tasks,
        ),
        ataIso = row.fromIso,
        atdIso = row.toIso,
        allowedPerformedServiceIds = allowedPerformedServiceIds,
        lowerBoundLabel = "return-to-ramp From",
        upperBoundLabel = "return-to-ramp To",
    )
    if (
        from == null && to == null && description == null && activity == null &&
        nested.services.isEmpty() && nested.tasks.isEmpty()
    ) return null

    return ReturnToRampSubmitFieldErrors(
        from = from,
        to = to,
        description = description,
        activity = activity,
        serviceLinesByKey = nested.services,
        tasksByKey = nested.tasks,
    )
}

internal fun computeCreateWorkOrderSubmitErrors(
    form: CreateWorkOrderFormState,
    dialogAtdIso: String?,
    validationPhase: WorkOrderValidationPhase,
    isAdHocScratch: Boolean,
    selectedCustomerId: String?,
    allowedPerformedServiceIds: Set<String>,
): CreateWorkOrderSubmitFieldErrors? {
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
    val normalizedRemarks = form.remarks.trim()
    val remarks = when {
        normalizedRemarks.length > WorkOrderFormLimits.Remarks ->
            "Remarks must be at most ${WorkOrderFormLimits.Remarks} characters."
        isAdHocScratch && isBlankOrUnknownCustomer(selectedCustomerId) && normalizedRemarks.isBlank() ->
            "Remarks are required when the customer is blank or Unknown Customer."
        else -> null
    }

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
    var atd: String? = null
    val atdDt = if (validationPhase == WorkOrderValidationPhase.Submission) {
        safeParseOffset(rawAtd)
    } else {
        null
    }
    if (validationPhase == WorkOrderValidationPhase.Submission) {
        atd = when {
            rawAtd.isBlank() -> "ATD is required."
            atdDt == null -> "Invalid ATD date or time."
            ataDt != null && !atdDt.isAfter(ataDt) ->
                "Departure (ATD) must be after arrival (ATA)."
            else -> null
        }
    }

    val lineErrors = computeWorkOrderLineErrors(
        form = form,
        ataIso = form.ataIso,
        atdIso = if (validationPhase == WorkOrderValidationPhase.Submission) rawAtd else null,
        allowedPerformedServiceIds = allowedPerformedServiceIds,
    )
    val returnToRampErrors = form.returnToRamps.mapNotNull { occurrence ->
        computeReturnToRampErrors(occurrence, allowedPerformedServiceIds)
            ?.let { occurrence.localKey to it }
    }.toMap()
    val hasLineEndingAfterAtd = atdDt?.let { departure ->
        form.serviceLines.any { row -> safeParseOffset(row.toIso)?.isAfter(departure) == true } ||
            form.tasks.any { row -> safeParseOffset(row.toIso)?.isAfter(departure) == true }
    } == true
    if (validationPhase == WorkOrderValidationPhase.Submission && hasLineEndingAfterAtd) {
        atd = mergeValidationMessage(
            atd,
            "Departure (ATD) can't be before a service or task end time.",
        )
    }
    if (ataDt != null) {
        form.serviceLines.mapNotNull { safeParseOffset(it.fromIso) }.filter { ataDt.isAfter(it) }.forEach {
            ata = mergeValidationMessage(ata, "Can't be after a service line start time.")
        }
        form.tasks.mapNotNull { safeParseOffset(it.fromIso) }.filter { ataDt.isAfter(it) }.forEach {
            ata = mergeValidationMessage(ata, "Can't be after a task start time.")
        }
    }

    val hasProblems = flightNumber != null || aircraft != null || tail != null ||
        scheduledArrival != null || scheduledDeparture != null || ata != null || atd != null || remarks != null ||
        lineErrors.services.isNotEmpty() || lineErrors.tasks.isNotEmpty() || returnToRampErrors.isNotEmpty()
    if (!hasProblems) return null

    return CreateWorkOrderSubmitFieldErrors(
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
        returnToRampsByKey = returnToRampErrors,
    )
}

internal fun isBlankSubmitErrors(errors: CreateWorkOrderSubmitFieldErrors): Boolean =
    errors.customer == null && errors.flightNumber == null && errors.aircraftType == null &&
        errors.aircraftTailNumber == null && errors.scheduledArrival == null &&
        errors.scheduledDeparture == null && errors.ata == null && errors.atd == null &&
        errors.remarks == null && errors.serviceLinesByKey.isEmpty() && errors.tasksByKey.isEmpty() &&
        errors.returnToRampsByKey.isEmpty()

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
            returnToRampsByKey = emptyMap(),
        )
        WorkOrderWizardStep.ServiceLines -> CreateWorkOrderSubmitFieldErrors(
            serviceLinesByKey = errors.serviceLinesByKey,
        )
        WorkOrderWizardStep.Tasks -> CreateWorkOrderSubmitFieldErrors(
            tasksByKey = errors.tasksByKey,
        )
        WorkOrderWizardStep.ReturnToRamps -> CreateWorkOrderSubmitFieldErrors(
            returnToRampsByKey = errors.returnToRampsByKey,
        )
        WorkOrderWizardStep.Signature -> return null
    }
    return filtered.takeUnless(::isBlankSubmitErrors)
}

internal fun firstWizardStepWithErrors(
    errors: CreateWorkOrderSubmitFieldErrors,
    includeReturnToRamps: Boolean = true,
): WorkOrderWizardStep = workOrderWizardSteps(includeReturnToRamps).firstOrNull {
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
    usages: Map<String, ResourceUsageForm>,
    quantities: Map<String, Double>,
    defaultCalculationType: ResourceCalculationType,
    label: String,
    taskFromIso: String,
    taskToIso: String,
): String? {
    if (selectedIds.any { it.isBlank() }) return "Every ${label.lowercase()} row needs an item."
    if (selectedIds.distinct().size != selectedIds.size) return "Duplicate ${label.lowercase()} rows are not allowed."

    val taskFrom = safeParseOffset(taskFromIso)
    val taskTo = safeParseOffset(taskToIso)
    selectedIds.forEach { id ->
        val usage = resourceUsage(
            id,
            usages,
            quantities,
            usages[id]?.calculationType ?: defaultCalculationType,
            taskFromIso,
            taskToIso,
        )
        if (usage.calculationType == ResourceCalculationType.Quantity) {
            val quantity = usage.quantity
            if (quantity == null || !isValidResourceQuantity(quantity))
                return "$label quantities must be positive with at most 16 whole digits and 2 decimal places."
            if (usage.fromIso.isNotBlank() || !usage.toIso.isNullOrBlank())
                return "$label quantity usage cannot include duration times."
        } else {
            if (usage.quantity != null) return "$label duration usage cannot include a quantity."
            val from = safeParseOffset(usage.fromIso)
                ?: return "$label duration From date and time is required."
            val to = safeParseOffset(usage.toIso)
            if (!usage.toIso.isNullOrBlank() && to == null) return "$label duration To date or time is invalid."
            if (to != null && to.isBefore(from)) return "$label duration To must be on or after From."
            if (taskFrom != null && from.isBefore(taskFrom)) return "$label duration cannot start before its task."
            if (taskTo != null && to != null && to.isAfter(taskTo)) return "$label duration cannot end after its task."
        }
    }
    return null
}
