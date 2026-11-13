package com.nags.operations.ui.flights

import com.nags.operations.R
import com.nags.operations.data.*
import com.nags.operations.data.db.entities.FlightTaskPendingEntity
import com.nags.operations.ui.util.operationLocalTime
import java.math.BigDecimal
import java.time.Instant
import java.time.OffsetDateTime
import java.time.ZoneId
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json

/** Match main's catalog-driven quantity/duration entry without rewriting older period drafts. */
internal fun taskResourceQuantityReviewRequired(item: FlightResourceDraft, calculationType: Int?) =
    calculationType == 0 && (item.period.start.isNotBlank() || item.period.end.isNotBlank())

internal fun taskResourceQuantityEdited(item: FlightResourceDraft, quantity: String): FlightResourceDraft {
    val reviewed = validTaskQuantity(quantity)
    return if ((item.calculationType == null || item.calculationType == 0) && reviewed)
        item.copy(quantity = quantity, calculationType = 0, period = FlightWorkPeriod())
    else item.copy(quantity = quantity)
}

private fun validTaskQuantity(raw: String): Boolean {
    if (raw.length > 64 || !Regex("[0-9]+(?:\\.[0-9]+)?").matches(raw)) return false
    val value = raw.toBigDecimalOrNull() ?: return false
    return value > BigDecimal.ZERO && value <= BigDecimal("9999999999999999.99") && value.stripTrailingZeros().scale() <= 2
}

internal fun taskResourceSelection(item: FlightResourceDraft, selected: ResourceCatalogItem, parent: FlightWorkPeriod,
    zone: ZoneId, bounds: FlightWorkTimeBounds): FlightResourceDraft {
    if (item.resourceId.equals(selected.id, true) && item.calculationType != null) return item
    val type = selected.taskCalculationType()
    return item.copy(resourceId = selected.id, resourceName = selected.name, calculationType = type,
        quantity = if (type == 0) "1" else "",
        period = if (type == 0) FlightWorkPeriod() else seedBlankWorkStart(parent, zone, bounds))
}

internal fun taskPeriodWithInitialStaff(task: FlightTaskDraft, period: FlightWorkPeriod,
    followingStaffIds: Set<String>) = task.copy(period = period, employees = task.employees.map { employee ->
    if (employee.id in followingStaffIds) employee.copy(period = employee.period.copy(
        start = period.start, originalStartUtc = period.originalStartUtc)) else employee
})

internal fun taskResourceCatalogs(catalogs: FlightOperationsCatalogs) = FlightTaskResourceCatalogs(
    catalogs.tools.mapNotNull { item -> item.taskCalculationType()?.let { item.id to it } }.toMap(),
    catalogs.materials.mapNotNull { item -> item.taskCalculationType()?.let { item.id to it } }.toMap(),
    catalogs.generalSupports.mapNotNull { item -> item.taskCalculationType()?.let { item.id to it } }.toMap(),
)

internal fun FlightTaskDraft.taskInput(zone: ZoneId, storedAttachmentIds: Set<String> = emptySet()): SaveFlightTaskRequest {
    fun resources(items: List<FlightResourceDraft>) = items.map { item ->
        // Keep incompatible old periods visible to validation until an explicit quantity edit.
        SaveFlightTaskResourceRequest(item.id, item.resourceId, item.quantity.takeIf(String::isNotBlank),
            item.period.start.takeIf(String::isNotBlank)?.let { arrivalInputTime(it, item.period.originalStartUtc, zone) },
            item.period.end.takeIf(String::isNotBlank)?.let { arrivalInputTime(it, item.period.originalEndUtc, zone) }, item.remarks)
    }
    return SaveFlightTaskRequest(when (taskType) { "Major" -> 0; "Minor" -> 1; else -> -1 },
        arrivalInputTime(period.start, period.originalStartUtc, zone),
        period.end.takeIf(String::isNotBlank)?.let { arrivalInputTime(it, period.originalEndUtc, zone) },
        ataChapterId.takeIf(String::isNotBlank), reference, remarks,
        employees.map { employee -> SaveFlightTaskPerformerRequest(employee.id, employee.employeeId,
            arrivalInputTime(employee.period.start, employee.period.originalStartUtc, zone),
            employee.period.end.takeIf(String::isNotBlank)?.let { arrivalInputTime(it, employee.period.originalEndUtc, zone) }, employee.remarks) },
        resources(tools), resources(materials), resources(generalSupports),
        attachments.map { it.serviceAttachmentInput(it.uploadedAtUtc != null || it.id in storedAttachmentIds) }).normalized()
}

internal fun initialTaskEditorDraft(flight: FlightDetail, catalogs: FlightOperationsCatalogs,
    pending: List<FlightTaskPendingEntity>, storedJson: String?, zone: ZoneId): List<FlightTaskDraft> {
    val stored = decodeServiceEditorDraft(storedJson)
    val oldZone = stored?.stationTimeZoneId?.let { runCatching { ZoneId.of(it) }.getOrNull() } ?: zone
    val saved = flight.tasks.associateBy { it.id }
    val items = flight.tasks.associate { it.id to it.input().toTaskEditorDraft(it.id, it, catalogs, zone) }.toMutableMap()
    pending.forEach { row ->
        if (row.operation == "delete") items.remove(row.taskRecordId)
        else row.inputJson?.let { raw -> runCatching { Json.decodeFromString<SaveFlightTaskRequest>(raw) }.getOrNull()?.let {
            items[row.taskRecordId] = it.toTaskEditorDraft(row.taskRecordId, saved[row.taskRecordId] ?: row.reviewedRecord(), catalogs, zone)
        } }
    }
    stored?.tasks?.forEach { local -> if (local.id !in items && pending.none { it.taskRecordId == local.id }) {
        items[local.id] = local.rezoneTask(oldZone, zone)
    } }
    return items.values.toList()
}

internal fun SaveFlightTaskRequest.toTaskEditorDraft(id: String, baseline: FlightTaskRecord?,
    catalogs: FlightOperationsCatalogs, zone: ZoneId): FlightTaskDraft {
    fun period(start: String?, end: String?) = FlightWorkPeriod(start?.let { operationLocalTime(it, zone) }.orEmpty(),
        end?.let { operationLocalTime(it, zone) }.orEmpty(), start, end)
    fun resources(items: List<SaveFlightTaskResourceRequest>, saved: List<FlightTaskResourceRecord>, choices: List<ResourceCatalogItem>) = items.map { item ->
        val retained = saved.firstOrNull { it.id == item.id && it.resourceId.equals(item.resourceId, true) }
        val choice = choices.firstOrNull { it.id.equals(item.resourceId, true) }
        FlightResourceDraft(item.id, item.resourceId, retained?.name ?: choice?.name.orEmpty(), period(item.startUtc, item.endUtc),
            item.notes.orEmpty(), retained?.calculationType ?: choice?.taskCalculationType(), item.quantity.orEmpty())
    }
    val chapter = catalogs.ataChapters.firstOrNull { it.id.equals(ataChapterId, true) }
    val retainedChapter = baseline?.takeIf { it.ataChapterId.equals(ataChapterId, true) }
    return FlightTaskDraft(id, when (taskType) { 0 -> "Major"; 1 -> "Minor"; else -> "" }, ataChapterId.orEmpty(),
        retainedChapter?.ataChapterCode ?: chapter?.code.orEmpty(), retainedChapter?.ataChapterTitle ?: chapter?.title.orEmpty(),
        period(startUtc, endUtc), customerReferenceNumber.orEmpty(), notes.orEmpty(), performers.map { person ->
            val retained = baseline?.performers?.firstOrNull { it.id == person.id && it.staffMemberId.equals(person.staffMemberId, true) }
            val staff = catalogs.stationStaff.firstOrNull { it.id.equals(person.staffMemberId, true) }
            FlightEmployeeDraft(person.id, person.staffMemberId, retained?.fullName ?: staff?.fullName.orEmpty(),
                retained?.employeeId ?: staff?.employeeId.orEmpty(), period(person.startUtc, person.endUtc), person.notes.orEmpty())
        }, resources(tools, baseline?.tools.orEmpty(), catalogs.tools), resources(materials, baseline?.materials.orEmpty(), catalogs.materials),
        resources(generalSupports, baseline?.generalSupports.orEmpty(), catalogs.generalSupports),
        (attachments ?: baseline?.attachments?.map { FlightServiceAttachmentInput(it.id, it.kind, it.fileName, it.contentType) }).orEmpty().map { attachment ->
            val uploaded = baseline?.attachments?.firstOrNull { it.id == attachment.id }
            FlightAttachmentDraft(attachment.id, attachment.contentUri.orEmpty(), attachment.fileName, attachment.contentType, uploadedAtUtc = uploaded?.uploadedAtUtc)
        })
}

private fun FlightTaskDraft.rezoneTask(before: ZoneId, after: ZoneId): FlightTaskDraft {
    if (before == after) return this
    fun period(value: FlightWorkPeriod): FlightWorkPeriod {
        fun local(raw: String, original: String?): Pair<String, String?> {
            if (raw.isBlank()) return raw to original
            val utc = arrivalInputTime(raw, original, before)
            return if (utc.isBlank()) raw to original else operationLocalTime(utc, after) to utc
        }
        val start = local(value.start, value.originalStartUtc); val end = local(value.end, value.originalEndUtc)
        return FlightWorkPeriod(start.first, end.first, start.second, end.second)
    }
    return copy(period = period(period), employees = employees.map { it.copy(period = period(it.period)) },
        tools = tools.map { it.copy(period = period(it.period)) }, materials = materials.map { it.copy(period = period(it.period)) },
        generalSupports = generalSupports.map { it.copy(period = period(it.period)) })
}

internal fun taskEditorFingerprint(items: List<FlightTaskDraft>): String {
    fun period(value: FlightWorkPeriod) = value.copy(originalStartUtc = null, originalEndUtc = null)
    fun resources(values: List<FlightResourceDraft>) = values.map { it.copy(resourceName = "", remarks = it.remarks.trim(),
        period = period(it.period)) }
    return Json.encodeToString(items.map { it.copy(ataChapterCode = "", ataChapterTitle = "", reference = it.reference.trim(), remarks = it.remarks.trim(),
        period = period(it.period), employees = it.employees.map { staff -> staff.copy(employeeName = "", employeeCode = "", remarks = staff.remarks.trim(), period = period(staff.period)) },
        tools = resources(it.tools), materials = resources(it.materials), generalSupports = resources(it.generalSupports),
        attachments = it.attachments.map { media -> media.copy(uri = "", ownedCapture = false, uploadedAtUtc = null) }) })
}

internal fun taskInputFingerprint(input: SaveFlightTaskRequest): String {
    val normalized = input.normalized()
    return Json.encodeToString(normalized.copy(attachments = normalized.attachments?.map { it.copy(content = null, contentUri = null) }))
}

internal fun taskEditorValidation(item: FlightTaskDraft, request: SaveFlightTaskRequest, flight: FlightDetail,
    catalogs: FlightOperationsCatalogs, rowVersion: String, actualArrival: String?, actualDeparture: String?): Map<String, Int> = buildMap {
    val baseline = flight.tasks.firstOrNull { it.id == item.id }
    val errors = request.validationErrors(rowVersion, actualArrival, actualDeparture, taskResourceCatalogs(catalogs), baseline)
    if (actualArrival.isNullOrBlank()) put("startUtc", R.string.flight_arrival_before_work)
    if (baseline?.canCloseOnly == true && request.endUtc == null) put("endUtc", R.string.flight_complete_work_end_required)
    val general = mapOf("taskType" to R.string.task_validation_type, "ataChapterId" to R.string.task_validation_ata,
        "startUtc" to R.string.task_validation_start, "endUtc" to R.string.task_validation_end,
        "customerReferenceNumber" to R.string.task_validation_reference, "notes" to R.string.task_validation_notes,
        "performers" to R.string.task_validation_performers, "attachments" to R.string.task_validation_attachments,
        "rowVersion" to R.string.task_validation_version)
    general.forEach { (key, message) -> if (key in errors) put(key, message) }
    if (request.ataChapterId != null && !request.ataChapterId.equals(baseline?.ataChapterId, true) &&
        catalogs.ataChapters.none { it.id.equals(request.ataChapterId, true) }) put("ataChapterId", R.string.task_validation_ata)
    val closing = request.endUtc != null || taskTimeInstant(actualDeparture) != null
    if (closing) {
        if (request.endUtc == null) put("endUtc", R.string.departure_task_end_required)
        request.performers.filter { it.endUtc == null }.forEach {
            put("performers.${it.id}.endUtc", R.string.task_validation_staff_end_required)
        }
        if (request.endUtc != null && request.performers.any { it.endUtc == null })
            put("endUtc", R.string.task_validation_open_periods)
    }
    val ata = taskTimeInstant(actualArrival); val atd = taskTimeInstant(actualDeparture)
    taskActualError(taskTimeInstant(request.startUtc), ata, atd)?.let { put("startUtc", it) }
    taskActualError(taskTimeInstant(request.endUtc), ata, atd)?.let { put("endUtc", it) }
    item.employees.forEach { person ->
        val value = request.performers.firstOrNull { it.id == person.id }
        val prefix = "performers.${person.id}."
        val retained = value != null && baseline?.performers?.any { it.id == value.id && it.staffMemberId.equals(value.staffMemberId, true) } == true
        if (value == null || prefix + "staffMemberId" in errors || !retained && catalogs.stationStaff.none { it.id.equals(person.employeeId, true) } &&
            !person.employeeId.equals(flight.currentStaffMemberId, true)) put(prefix + "staffMemberId", R.string.task_validation_staff)
        if (prefix + "id" in errors) put(prefix + "staffMemberId", R.string.task_validation_version)
        if (value == null || prefix + "startUtc" in errors) put(prefix + "startUtc", R.string.task_validation_staff_period)
        if (prefix + "endUtc" in errors && value?.endUtc != null) put(prefix + "endUtc", R.string.task_validation_staff_period)
        if (prefix + "notes" in errors) put(prefix + "notes", R.string.task_validation_notes)
        taskActualError(taskTimeInstant(value?.startUtc), ata, atd)?.let { put(prefix + "startUtc", it) }
        taskActualError(taskTimeInstant(value?.endUtc), ata, atd)?.let { put(prefix + "endUtc", it) }
    }
    fun resources(kind: String, drafts: List<FlightResourceDraft>, values: List<SaveFlightTaskResourceRequest>,
        saved: List<FlightTaskResourceRecord>, choices: List<ResourceCatalogItem>) {
        if (kind in errors) put(kind, R.string.task_validation_resources)
        drafts.forEach { draft ->
            val prefix = "$kind.${draft.id}."
            val value = values.firstOrNull { it.id == draft.id }
            if (prefix + "id" in errors || prefix + "resourceId" in errors) put(prefix + "resourceId", R.string.task_validation_resource)
            if (prefix + "quantity" in errors) put(prefix + "quantity", R.string.task_validation_quantity)
            if (prefix + "startUtc" in errors) put(prefix + "startUtc", R.string.task_validation_resource_period)
            if (prefix + "endUtc" in errors) put(prefix + "endUtc", R.string.task_validation_resource_period)
            if (prefix + "notes" in errors) put(prefix + "notes", if (draft.remarks.isBlank()) R.string.task_validation_unknown_notes else R.string.task_validation_notes)
            val retained = saved.firstOrNull { it.id == draft.id && it.resourceId.equals(draft.resourceId, true) }
            val catalogType = choices.firstOrNull { it.id.equals(draft.resourceId, true) }?.taskCalculationType()
            val type = retained?.calculationType ?: draft.calculationType ?: catalogType
            if (retained == null && draft.calculationType != null && catalogType != null && draft.calculationType != catalogType)
                put(prefix + "resourceId", R.string.task_validation_resource_type_changed)
            if (closing && type == 1 && value?.endUtc == null) {
                put(prefix + "endUtc", R.string.task_validation_resource_end_required)
                if (request.endUtc != null) put("endUtc", R.string.task_validation_open_periods)
            }
            if (type == 1 && prefix + "quantity" in errors) put(prefix + "quantity", R.string.task_validation_duration_quantity)
            if (taskResourceQuantityReviewRequired(draft, type)) put(prefix + "quantity", R.string.task_validation_quantity_review)
        }
    }
    resources("tools", item.tools, request.tools, baseline?.tools.orEmpty(), catalogs.tools)
    resources("materials", item.materials, request.materials, baseline?.materials.orEmpty(), catalogs.materials)
    resources("generalSupports", item.generalSupports, request.generalSupports, baseline?.generalSupports.orEmpty(), catalogs.generalSupports)
}

private fun taskTimeInstant(raw: String?) = raw?.let { runCatching { OffsetDateTime.parse(it).takeIf { value ->
    value.year in 1..9999 && value.toInstant() != Instant.parse("0001-01-01T00:00:00Z")
}?.toInstant() }.getOrNull() }

private fun taskActualError(value: Instant?, arrival: Instant?, departure: Instant?) = when {
    value == null -> null
    arrival != null && value < arrival -> R.string.task_validation_before_ata
    departure != null && value > departure -> R.string.task_validation_after_atd
    else -> null
}

internal fun taskActualBoundsValidation(items: List<FlightTaskDraft>, zone: ZoneId, actualArrival: String?, actualDeparture: String?): Map<String, Map<String, Int>> {
    val arrival = taskTimeInstant(actualArrival); val departure = taskTimeInstant(actualDeparture)
    return items.filter { it.period.start.isNotBlank() }.mapNotNull { draft ->
        val input = draft.taskInput(zone)
        val errors = buildMap {
            fun check(key: String, raw: String?) { taskActualError(taskTimeInstant(raw), arrival, departure)?.let { put(key, it) } }
            check("startUtc", input.startUtc); check("endUtc", input.endUtc)
            input.performers.forEach { check("performers.${it.id}.startUtc", it.startUtc); check("performers.${it.id}.endUtc", it.endUtc) }
        }
        if (errors.isEmpty()) null else draft.id to errors
    }.toMap()
}

/** Departure closes started tasks, staff, and duration resources; quantity rows have no periods. */
internal fun departureTaskCompletionValidation(items: List<FlightTaskDraft>, saved: List<FlightTaskRecord>,
    pending: List<FlightTaskPendingEntity>, changes: List<FlightTaskChange>, zone: ZoneId,
    onlyRecordIds: Set<String>? = null): Map<String, Map<String, Int>> {
    val effective = saved.associate { it.id to it.input() }.toMutableMap<String, SaveFlightTaskRequest?>()
    val removed = mutableSetOf<String>()
    fun apply(id: String, input: SaveFlightTaskRequest?, delete: Boolean) {
        if (delete) { effective.remove(id); removed += id } else { effective[id] = input; removed -= id }
    }
    pending.forEach { row -> apply(row.taskRecordId, row.inputJson?.let { runCatching { Json.decodeFromString<SaveFlightTaskRequest>(it) }.getOrNull() }, row.operation == "delete") }
    changes.forEach { apply(it.taskRecordId, it.input, it.input == null) }
    items.filter { it.period.start.isNotBlank() && it.id !in effective && it.id !in removed }.forEach { effective[it.id] = it.taskInput(zone) }
    return effective.filterKeys { onlyRecordIds == null || it in onlyRecordIds }.mapNotNull { (id, input) ->
        val errors = buildMap {
            if (input == null) put("rowVersion", R.string.task_validation_version)
            else {
                val start = taskTimeInstant(input.startUtc); val end = taskTimeInstant(input.endUtc)
                if (input.endUtc.isNullOrBlank()) put("endUtc", R.string.departure_task_end_required)
                else if (end == null || start != null && end < start) put("endUtc", R.string.task_validation_end)
                if (input.performers.isEmpty()) put("performers", R.string.task_validation_performers)
                input.performers.forEach { person ->
                    val from = taskTimeInstant(person.startUtc); val to = taskTimeInstant(person.endUtc)
                    if (person.endUtc.isNullOrBlank()) put("performers.${person.id}.endUtc", R.string.departure_task_staff_end_required)
                    else if (to == null || from != null && to < from || end != null && to > end)
                        put("performers.${person.id}.endUtc", R.string.task_validation_staff_period)
                }
                listOf("tools" to input.tools, "materials" to input.materials, "generalSupports" to input.generalSupports)
                    .forEach { (kind, resources) -> resources.forEach { resource ->
                        val savedTask = saved.firstOrNull { it.id == id }
                        val draftTask = items.firstOrNull { it.id == id }
                        val savedResources = when (kind) { "tools" -> savedTask?.tools; "materials" -> savedTask?.materials; else -> savedTask?.generalSupports }
                        val draftResources = when (kind) { "tools" -> draftTask?.tools; "materials" -> draftTask?.materials; else -> draftTask?.generalSupports }
                        val type = savedResources?.firstOrNull { it.id == resource.id && it.resourceId.equals(resource.resourceId, true) }?.calculationType
                            ?: draftResources?.firstOrNull { it.id == resource.id && it.resourceId.equals(resource.resourceId, true) }?.calculationType
                        // Captured catalog types exclude Quantity; older desired input falls
                        // back to its retained duration branch until reviewed.
                        if (type == 1 || type == null && (resource.quantity == null || resource.startUtc != null || resource.endUtc != null)) {
                            val from = taskTimeInstant(resource.startUtc); val to = taskTimeInstant(resource.endUtc)
                            if (resource.endUtc.isNullOrBlank()) put("$kind.${resource.id}.endUtc", R.string.departure_task_resource_end_required)
                            else if (from == null || to == null || start != null && from < start || to < from || end != null && to > end)
                                put("$kind.${resource.id}.endUtc", R.string.task_validation_resource_period)
                        }
                    } }
            }
        }
        if (errors.isEmpty()) null else id to errors
    }.toMap()
}

internal data class TaskEndReview(val items: List<FlightTaskDraft>, val conflictedTaskIds: Set<String>)

/** Explicit review may adopt another closer's End without discarding a creator's core edits. */
internal fun reviewTaskEnds(items: List<FlightTaskDraft>, previousRecords: List<FlightTaskRecord>,
    latestRecords: List<FlightTaskRecord>, zone: ZoneId, useRecordedEnds: Boolean = false): TaskEndReview {
    fun canonical(raw: String?): String? = raw?.let { taskTimeInstant(it)?.toString() ?: it }
    val conflicts = mutableSetOf<String>()
    val reviewed = items.map { draft ->
        val latest = latestRecords.firstOrNull { it.id == draft.id } ?: return@map draft
        val previous = previousRecords.firstOrNull { it.id == draft.id }
        val desiredEnd = draft.period.end.takeIf(String::isNotBlank)?.let { arrivalInputTime(it, draft.period.originalEndUtc, zone) }
        val unchanged = canonical(desiredEnd) == canonical(previous?.endUtc)
        if (unchanged && canonical(latest.endUtc) != canonical(previous?.endUtc)) return@map draft.copy(period = draft.period.copy(
            end = latest.endUtc?.let { operationLocalTime(it, zone) }.orEmpty(), originalEndUtc = latest.endUtc))
        if (latest.canSetEnd || latest.endUtc == null) return@map draft
        val matchesRecorded = canonical(desiredEnd) == canonical(latest.endUtc)
        if (useRecordedEnds || unchanged || matchesRecorded) draft.copy(period = draft.period.copy(
            end = operationLocalTime(latest.endUtc, zone), originalEndUtc = latest.endUtc))
        else { conflicts += draft.id; draft }
    }
    return TaskEndReview(reviewed, conflicts)
}

internal fun taskEditorChanges(items: List<FlightTaskDraft>, knownIds: Set<String>, baseline: Map<String, String>,
    pending: List<FlightTaskPendingEntity>, forceIds: Set<String>, storedMedia: Set<String>, zone: ZoneId): List<FlightTaskChange> {
    val currentIds = items.map { it.id }.toSet()
    val changes = items.filter { it.id in knownIds || it.period.start.isNotBlank() }.mapNotNull { item ->
        val retained = item.attachments.filter { "${item.id}/${it.id}" in storedMedia }.map { it.id }.toSet()
        val input = item.taskInput(zone, retained)
        if (taskInputFingerprint(input) != baseline[item.id] || item.id in forceIds || input.startUtc.isBlank()) FlightTaskChange(item.id, input) else null
    }
    return changes + (knownIds - currentIds).filter { id ->
        pending.none { it.taskRecordId == id && it.operation == "delete" && it.status == "pending" } || id in forceIds
    }.map { FlightTaskChange(it, null) }
}
