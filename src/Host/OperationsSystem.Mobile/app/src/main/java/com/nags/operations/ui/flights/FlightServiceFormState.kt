package com.nags.operations.ui.flights

import com.nags.operations.R
import com.nags.operations.data.*
import com.nags.operations.data.db.entities.FlightServicePendingEntity
import com.nags.operations.ui.util.operationLocalTime
import java.time.OffsetDateTime
import java.time.ZoneId
import kotlinx.serialization.Serializable
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json

/** Incomplete rows and task editor values are local drafts, never HTTP service records. */
@Serializable
internal data class FlightServiceEditorDraft(
    val stationTimeZoneId: String,
    val services: List<FlightServiceDraft> = emptyList(),
    val tasks: List<FlightTaskDraft> = emptyList(),
    val suppressedPlannedServiceIds: List<String> = emptyList(),
)

internal fun decodeServiceEditorDraft(json: String?): FlightServiceEditorDraft? = json?.let {
    runCatching { Json.decodeFromString<FlightServiceEditorDraft>(it) }.getOrNull()
}

internal fun defaultServiceEmployee(flight: FlightDetail, catalogs: FlightOperationsCatalogs): FlightEmployeeDraft? {
    val id = flight.currentStaffMemberId ?: return null
    val current = catalogs.stationStaff.firstOrNull { it.id.equals(id, true) }
    if (current != null) return FlightEmployeeDraft(employeeId = current.id, employeeName = current.fullName, employeeCode = current.employeeId)
    return flight.assignedEmployees.firstOrNull { it.staffMemberId.equals(id, true) }?.let {
        FlightEmployeeDraft(employeeId = it.staffMemberId, employeeName = it.fullName, employeeCode = it.employeeId)
    }
}

internal fun initialServiceEditorDraft(flight: FlightDetail, catalogs: FlightOperationsCatalogs,
    pending: List<FlightServicePendingEntity>, storedJson: String?, zone: ZoneId): FlightServiceEditorDraft {
    val stored = decodeServiceEditorDraft(storedJson)
    val oldZone = stored?.stationTimeZoneId?.let { runCatching { ZoneId.of(it) }.getOrNull() } ?: zone
    val records = flight.services.associateBy { it.id }.toMutableMap()
    val items = flight.services.associate { it.id to it.toEditorDraft(zone) }.toMutableMap()
    pending.forEach { row ->
        if (row.operation == "delete") items.remove(row.serviceRecordId)
        else row.inputJson?.let { raw ->
            runCatching { Json.decodeFromString<SaveFlightServiceRequest>(raw) }.getOrNull()?.let { input ->
                val baseline = records[row.serviceRecordId]
                val name = catalogs.services.firstOrNull { it.id.equals(input.serviceId, true) }?.name ?: baseline?.serviceName.orEmpty()
                items[row.serviceRecordId] = input.toEditorDraft(row.serviceRecordId, name, baseline, catalogs, zone)
            }
        }
    }
    stored?.services?.forEach { local -> if (local.id !in items && pending.none { it.serviceRecordId == local.id }) {
        items[local.id] = local.rezone(oldZone, zone)
    } }
    val suppressed = stored?.suppressedPlannedServiceIds.orEmpty()
    if (!flight.isPerLanding && flight.allowedServiceIds != null) {
        val allowed = flight.allowedServiceIds.map { it.lowercase() }.toSet()
        val existing = items.values.map { it.serviceId.lowercase() }.toSet() +
            pending.mapNotNull { it.reviewedServiceJson?.let { json -> runCatching { Json.decodeFromString<FlightServiceRecord>(json).serviceId.lowercase() }.getOrNull() } }
        flight.plannedServices.filter { !it.isAircraftPerLanding && it.serviceId.lowercase() in allowed &&
            it.serviceId.lowercase() !in existing && suppressed.none { hidden -> hidden.equals(it.serviceId, true) } }.forEach { planned ->
            val employee = defaultServiceEmployee(flight, catalogs)
            val draft = FlightServiceDraft(serviceId = planned.serviceId, serviceName = planned.name,
                employees = listOfNotNull(employee), plannedServiceId = planned.serviceId)
            items[draft.id] = draft
        }
    }
    return FlightServiceEditorDraft(zone.id, items.values.toList(), stored?.tasks.orEmpty().map { it.rezone(oldZone, zone) }, suppressed)
}

private fun FlightWorkPeriod.rezone(before: ZoneId, after: ZoneId): FlightWorkPeriod {
    if (before == after) return this
    fun value(raw: String, original: String?): Pair<String, String?> {
        if (raw.isBlank()) return raw to original
        val utc = arrivalInputTime(raw, original, before)
        return if (utc.isBlank()) raw to original else operationLocalTime(utc, after) to utc
    }
    val from = value(start, originalStartUtc); val to = value(end, originalEndUtc)
    return copy(start = from.first, end = to.first, originalStartUtc = from.second, originalEndUtc = to.second)
}
private fun FlightServiceDraft.rezone(before: ZoneId, after: ZoneId) = copy(period = period.rezone(before, after),
    employees = employees.map { it.copy(period = it.period.rezone(before, after)) })
private fun FlightTaskDraft.rezone(before: ZoneId, after: ZoneId) = copy(period = period.rezone(before, after),
    employees = employees.map { it.copy(period = it.period.rezone(before, after)) },
    tools = tools.map { it.copy(period = it.period.rezone(before, after)) },
    materials = materials.map { it.copy(period = it.period.rezone(before, after)) },
    generalSupports = generalSupports.map { it.copy(period = it.period.rezone(before, after)) })

private fun FlightServiceRecord.toEditorDraft(zone: ZoneId) = input().toEditorDraft(id, serviceName, this, FlightOperationsCatalogs(), zone)

private fun SaveFlightServiceRequest.toEditorDraft(id: String, name: String, baseline: FlightServiceRecord?,
    catalogs: FlightOperationsCatalogs, zone: ZoneId) = FlightServiceDraft(id, serviceId, name,
    FlightWorkPeriod(operationLocalTime(startUtc, zone), endUtc?.let { operationLocalTime(it, zone) }.orEmpty(), startUtc, endUtc),
    customerReferenceNumber.orEmpty(), notes.orEmpty(),
    performers.map { performer ->
        val saved = baseline?.performers?.firstOrNull { it.id == performer.id }
        val staff = catalogs.stationStaff.firstOrNull { it.id.equals(performer.staffMemberId, true) }
        FlightEmployeeDraft(performer.id, performer.staffMemberId, staff?.fullName ?: saved?.fullName.orEmpty(),
            staff?.employeeId ?: saved?.employeeId.orEmpty(), FlightWorkPeriod(operationLocalTime(performer.startUtc, zone),
                performer.endUtc?.let { operationLocalTime(it, zone) }.orEmpty(), performer.startUtc, performer.endUtc))
    }, (attachments ?: baseline?.attachments?.map {
        FlightServiceAttachmentInput(it.id, it.kind, it.fileName, it.contentType)
    }).orEmpty().map { attachment ->
        val uploaded = baseline?.attachments?.firstOrNull { it.id == attachment.id }
        FlightAttachmentDraft(attachment.id, "", attachment.fileName, attachment.contentType, uploadedAtUtc = uploaded?.uploadedAtUtc)
    })

internal fun FlightServiceDraft.serviceInput(zone: ZoneId, storedAttachmentIds: Set<String> = emptySet()): SaveFlightServiceRequest =
    SaveFlightServiceRequest(serviceId, arrivalInputTime(period.start, period.originalStartUtc, zone),
        period.end.takeIf { it.isNotBlank() }?.let { arrivalInputTime(it, period.originalEndUtc, zone) }, reference, remarks,
        employees.map { employee -> SaveFlightServicePerformerRequest(employee.id, employee.employeeId,
            arrivalInputTime(employee.period.start, employee.period.originalStartUtc, zone),
            employee.period.end.takeIf { it.isNotBlank() }?.let { arrivalInputTime(it, employee.period.originalEndUtc, zone) }) },
        attachments.map { it.serviceAttachmentInput(it.uploadedAtUtc != null || it.id in storedAttachmentIds) }).normalized()

internal fun FlightAttachmentDraft.serviceAttachmentInput(retained: Boolean) = FlightServiceAttachmentInput(id,
    when { mimeType.startsWith("image/", true) -> 0; mimeType.startsWith("audio/", true) -> 1; else -> 2 },
    name.substringAfterLast('/').substringAfterLast('\\').take(255), mimeType.trim().lowercase(),
    contentUri = uri.takeIf { !retained && it.isNotBlank() })

/** URI hydration and reference display names cannot manufacture an unsaved business edit. */
internal fun serviceEditorFingerprint(services: List<FlightServiceDraft>, tasks: List<FlightTaskDraft>, suppressed: List<String>): String {
    fun period(value: FlightWorkPeriod) = value.copy(originalStartUtc = null, originalEndUtc = null)
    fun media(items: List<FlightAttachmentDraft>) = items.map { it.copy(uri = "", ownedCapture = false, uploadedAtUtc = null) }
    fun employees(items: List<FlightEmployeeDraft>) = items.map { it.copy(employeeName = "", employeeCode = "", period = period(it.period)) }
    val value = FlightServiceEditorDraft("", services.map { it.copy(serviceName = "", reference = it.reference.trim(), remarks = it.remarks.trim(),
        period = period(it.period), employees = employees(it.employees), attachments = media(it.attachments)) },
        tasks.map { it.copy(period = period(it.period), employees = employees(it.employees), attachments = media(it.attachments),
            tools = it.tools.map { item -> item.copy(period = period(item.period)) },
            materials = it.materials.map { item -> item.copy(period = period(item.period)) },
            generalSupports = it.generalSupports.map { item -> item.copy(period = period(item.period)) }) }, suppressed.sorted())
    return Json.encodeToString(value)
}

internal fun serviceInputFingerprint(input: SaveFlightServiceRequest): String {
    val normalized = input.normalized()
    return Json.encodeToString(normalized.copy(attachments = normalized.attachments?.map { it.copy(content = null, contentUri = null) }))
}

internal fun serviceEditorValidation(item: FlightServiceDraft, request: SaveFlightServiceRequest, flight: FlightDetail,
    catalogs: FlightOperationsCatalogs, rowVersion: String, actualArrival: String?, actualDeparture: String?): Map<String, Int> = buildMap {
    val errors = request.validationErrors(rowVersion, actualArrival, actualDeparture, flight.allowedServiceIds?.toSet())
    if ("serviceId" in errors || flight.allowedServiceIds?.none { it.equals(request.serviceId, true) } != false ||
        catalogs.services.none { it.id.equals(request.serviceId, true) && !it.isAircraftPerLanding }) put("serviceId", R.string.service_validation_service)
    if ("startUtc" in errors) put("startUtc", R.string.service_validation_start)
    if ("endUtc" in errors) put("endUtc", R.string.service_validation_end)
    if ("customerReferenceNumber" in errors) put("customerReferenceNumber", R.string.service_validation_reference)
    if ("notes" in errors) put("notes", R.string.service_validation_notes)
    if (request.serviceId.equals("40000000-0000-0000-0000-000000000003", true) && request.notes.isNullOrBlank()) put("notes", R.string.service_validation_unknown_notes)
    if ("performers" in errors) put("performers", R.string.service_validation_performers)
    if ("attachments" in errors) put("attachments", R.string.service_validation_attachments)
    if ("rowVersion" in errors) put("rowVersion", R.string.service_validation_version)
    if (request.endUtc != null && request.performers.any { it.endUtc == null }) {
        put("endUtc", R.string.service_validation_open_staff)
        request.performers.filter { it.endUtc == null }.forEach {
            put("performers.${it.id}.endUtc", R.string.service_validation_staff_end_required)
        }
    }
    fun instant(raw: String?) = raw?.let { runCatching { OffsetDateTime.parse(it).toInstant() }.getOrNull() }
    val from = instant(request.startUtc); val to = instant(request.endUtc)
    val ata = instant(actualArrival); val atd = instant(actualDeparture)
    serviceActualTimeError(from, ata, atd)?.let { put("startUtc", it) }
    serviceActualTimeError(to, ata, atd)?.let { put("endUtc", it) }
    item.employees.forEach { employee ->
        val value = request.performers.firstOrNull { it.id == employee.id }
        val prefix = "performers.${employee.id}."
        val retainedStaff = value != null && flight.services.firstOrNull { it.id == item.id }?.performers?.any {
            it.id == value.id && it.staffMemberId.equals(value.staffMemberId, true)
        } == true
        if (value == null || prefix + "staffMemberId" in errors || !retainedStaff && catalogs.stationStaff.none { it.id.equals(employee.employeeId, true) } &&
            !employee.employeeId.equals(flight.currentStaffMemberId, true)) put(prefix + "staffMemberId", R.string.service_validation_staff)
        if (prefix + "id" in errors) put(prefix + "staffMemberId", R.string.service_validation_version)
        val start = instant(value?.startUtc); val end = instant(value?.endUtc)
        if (value == null || prefix + "startUtc" in errors)
            put(prefix + "startUtc", R.string.service_validation_staff_period)
        if (prefix + "endUtc" in errors && value?.endUtc != null)
            put(prefix + "endUtc", R.string.service_validation_staff_period)
        serviceActualTimeError(start, ata, atd)?.let { put(prefix + "startUtc", it) }
        serviceActualTimeError(end, ata, atd)?.let { put(prefix + "endUtc", it) }
    }
}

private fun serviceActualTimeError(value: java.time.Instant?, arrival: java.time.Instant?, departure: java.time.Instant?): Int? = when {
    value == null -> null
    arrival != null && value < arrival -> R.string.service_validation_before_ata
    departure != null && value > departure -> R.string.service_validation_after_atd
    else -> null
}

/** Departure closes recorded/queued/started work, never unstarted planned rows or task drafts. */
internal fun departureServiceCompletionValidation(items: List<FlightServiceDraft>, saved: List<FlightServiceRecord>,
    pending: List<FlightServicePendingEntity>, changes: List<FlightServiceChange>, zone: ZoneId,
    onlyRecordIds: Set<String>? = null): Map<String, Map<String, Int>> {
    val effective = mutableMapOf<String, SaveFlightServiceRequest?>()
    saved.forEach { effective[it.id] = it.input() }
    val removed = mutableSetOf<String>()
    fun apply(id: String, input: SaveFlightServiceRequest?, delete: Boolean) {
        if (delete) { effective.remove(id); removed += id }
        else { effective[id] = input; removed -= id }
    }
    pending.forEach { row -> apply(row.serviceRecordId, row.inputJson?.let {
        runCatching { Json.decodeFromString<SaveFlightServiceRequest>(it) }.getOrNull()
    }, row.operation == "delete") }
    changes.forEach { apply(it.serviceRecordId, it.input, it.input == null) }
    items.filter { it.period.start.isNotBlank() && it.id !in effective && it.id !in removed }
        .forEach { effective[it.id] = it.serviceInput(zone) }
    fun instant(raw: String?) = raw?.let { runCatching { OffsetDateTime.parse(it).takeIf { value ->
        value.year in 1..9999 && value.toInstant() != java.time.Instant.parse("0001-01-01T00:00:00Z")
    }?.toInstant() }.getOrNull() }
    return effective.filterKeys { onlyRecordIds == null || it in onlyRecordIds }.mapNotNull { (id, input) ->
        val errors = buildMap {
            if (input == null) put("rowVersion", R.string.service_validation_version)
            else {
                val start = instant(input.startUtc); val end = instant(input.endUtc)
                if (input.endUtc.isNullOrBlank()) put("endUtc", R.string.departure_service_end_required)
                else if (end == null || start != null && end < start) put("endUtc", R.string.service_validation_end)
                if (input.performers.isEmpty()) put("performers", R.string.service_validation_performers)
                input.performers.forEach { performer ->
                    val prefix = "performers.${performer.id}."
                    val from = instant(performer.startUtc); val to = instant(performer.endUtc)
                    if (performer.endUtc.isNullOrBlank()) put(prefix + "endUtc", R.string.departure_staff_end_required)
                    else if (to == null || from != null && to < from || end != null && to > end)
                        put(prefix + "endUtc", R.string.service_validation_staff_period)
                }
            }
        }
        if (errors.isEmpty()) null else id to errors
    }.toMap()
}

/** Parent time edits must include unchanged work as well as this save's corrections/deletions. */
internal fun serviceActualBoundsValidation(items: List<FlightServiceDraft>, zone: ZoneId,
    actualArrival: String?, actualDeparture: String?): Map<String, Map<String, Int>> {
    fun instant(value: String?) = value?.let { runCatching { OffsetDateTime.parse(it).toInstant() }.getOrNull() }
    val arrival = instant(actualArrival); val departure = instant(actualDeparture)
    fun boundsError(raw: String?) = serviceActualTimeError(instant(raw), arrival, departure)
    return items.filter { it.period.start.isNotBlank() }.mapNotNull { item ->
        val input = item.serviceInput(zone)
        val errors = buildMap {
            boundsError(input.startUtc)?.let { put("startUtc", it) }
            boundsError(input.endUtc)?.let { put("endUtc", it) }
            input.performers.forEach { performer ->
                boundsError(performer.startUtc)?.let { put("performers.${performer.id}.startUtc", it) }
                boundsError(performer.endUtc)?.let { put("performers.${performer.id}.endUtc", it) }
            }
        }
        if (errors.isEmpty()) null else item.id to errors
    }.toMap()
}

/** Prefilled rows stay local until started; removing a known record targets only that stable identity. */
internal fun serviceEditorChanges(items: List<FlightServiceDraft>, knownIds: Set<String>, baseline: Map<String, String>,
    pending: List<FlightServicePendingEntity>, forceIds: Set<String>, storedMedia: Set<String>, zone: ZoneId): List<FlightServiceChange> {
    val currentIds = items.map { it.id }.toSet()
    val changed = items.filter { it.id in knownIds || it.period.start.isNotBlank() }.mapNotNull { item ->
        val retained = item.attachments.filter { "${item.id}/${it.id}" in storedMedia }.map { it.id }.toSet()
        val input = item.serviceInput(zone, retained)
        if (serviceInputFingerprint(input) != baseline[item.id] || item.id in forceIds || input.startUtc.isBlank())
            FlightServiceChange(item.id, input) else null
    }
    val removed = (knownIds - currentIds).filter { id ->
        pending.none { it.serviceRecordId == id && it.operation == "delete" && it.status == "pending" } || id in forceIds
    }.map { FlightServiceChange(it, null) }
    return changed + removed
}
