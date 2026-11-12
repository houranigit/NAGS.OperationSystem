package com.nags.operations.ui.flights

import com.nags.operations.R
import com.nags.operations.data.*
import com.nags.operations.data.db.entities.FlightServicePendingEntity
import java.time.ZoneId
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import org.junit.Assert.*
import org.junit.Test

class FlightServiceFormStateTest {
    private val serviceId = "11111111-1111-1111-1111-111111111111"
    private val secondServiceId = "22222222-2222-2222-2222-222222222222"
    private val staffId = "33333333-3333-3333-3333-333333333333"
    private val otherStaffId = "44444444-4444-4444-4444-444444444444"
    private val recordId = "55555555-5555-5555-5555-555555555555"
    private val performerId = "66666666-6666-6666-6666-666666666666"
    private val zone = ZoneId.of("Etc/UTC")
    private val catalog = FlightOperationsCatalogs(
        services = listOf(ServiceCatalogItem(serviceId, "Service", false), ServiceCatalogItem(secondServiceId, "Second", false)),
        stationStaff = listOf(StaffCatalogItem(staffId, "Current employee", "E1", "station", "AAA", "Engineer")))
    private val flight = FlightDetail("flight", "SV100", "customer", customerName = "Airline", stationId = "station", stationIata = "AAA",
        stationName = "Station", operationTypeId = "operation", operationTypeName = "Transit", scheduledArrivalUtc = "2026-11-11T10:00:00Z",
        scheduledDepartureUtc = "2026-11-11T20:00:00Z", isPerLanding = false, createdAtUtc = "2026-11-11T08:00:00Z",
        rowVersion = "AAAAAAAAAAE=", stationTimeZoneId = zone.id, currentStaffMemberId = staffId,
        plannedServices = listOf(PlannedService(serviceId, "Service", false), PlannedService(secondServiceId, "Second", false)),
        canSaveServices = true, allowedServiceIds = listOf(serviceId, secondServiceId))
    private val record = FlightServiceRecord(recordId, serviceId, "Service", "2026-11-11T11:00:00Z", createdByUserId = "creator",
        createdAtUtc = "2026-11-11T11:00:00Z", performers = listOf(FlightServicePerformerRecord(performerId, otherStaffId,
            "Original employee", "E2", "2026-11-11T11:00:00Z")), canEdit = false, canSetEnd = true)

    @Test fun explicitServiceReviewMergesUnchangedConcurrentEndAndPreservesIntentionalPermittedChanges() {
        val draft = initialServiceEditorDraft(flight.copy(services = listOf(record)), catalog, emptyList(), null, zone)
            .services.first { it.id == recordId }.copy(remarks = "Core correction")
        val latest = record.copy(endUtc = "2026-11-11T12:00:00Z", endedByUserId = "teammate", canEdit = true, canSetEnd = true)
        val merged = reviewServiceEnds(listOf(draft), listOf(record), listOf(latest), zone)
        assertTrue(merged.conflictedServiceIds.isEmpty())
        assertEquals("2026-11-11T12:00", merged.items.single().period.end)
        assertEquals(draft.remarks, merged.items.single().remarks)
        val deliberate = draft.copy(period = draft.period.copy(end = "2026-11-11T12:30"))
        assertEquals(deliberate, reviewServiceEnds(listOf(deliberate), listOf(record), listOf(latest), zone).items.single())
        val closed = record.copy(endUtc = latest.endUtc)
        val closedDraft = draft.copy(period = draft.period.copy(end = "2026-11-11T12:00", originalEndUtc = latest.endUtc))
        assertEquals("", reviewServiceEnds(listOf(closedDraft), listOf(closed), listOf(latest.copy(endUtc = null)), zone).items.single().period.end)
    }

    @Test fun explicitServiceReviewKeepsConflictingProtectedEndUntilRecordedEndIsChosen() {
        val draft = FlightServiceDraft(id = recordId, period = FlightWorkPeriod("2026-11-11T11:00", "2026-11-11T12:30"), remarks = "Keep notes")
        val latest = record.copy(endUtc = "2026-11-11T12:00:00Z", endedByUserId = "teammate", canSetEnd = false)
        val conflict = reviewServiceEnds(listOf(draft), listOf(record), listOf(latest), zone)
        assertEquals(setOf(recordId), conflict.conflictedServiceIds)
        assertEquals(draft, conflict.items.single())
        val confirmed = reviewServiceEnds(listOf(draft), listOf(record), listOf(latest), zone, useRecordedEnds = true)
        assertTrue(confirmed.conflictedServiceIds.isEmpty())
        assertEquals("2026-11-11T12:00", confirmed.items.single().period.end)
        assertEquals("Keep notes", confirmed.items.single().remarks)
    }

    @Test fun publishedReopenKeepsPdfAndExplicitPlannedRemovalsWithoutCreatingASaveChange() {
        val pdf = FlightServiceAttachmentRecord("77777777-7777-7777-7777-777777777777", 2, "service.pdf",
            "application/pdf", 594, uploadedAtUtc = "2026-11-11T11:01:00Z")
        val published = record.copy(attachments = listOf(pdf))
        val stored = FlightServiceEditorDraft(zone.id, suppressedPlannedServiceIds = listOf(serviceId, secondServiceId))
        val reopened = initialServiceEditorDraft(flight.copy(services = listOf(published)), catalog, emptyList(),
            Json.encodeToString(stored), zone)
        assertEquals(listOf(recordId), reopened.services.map { it.id })
        assertEquals(pdf.id, reopened.services.single().attachments.single().id)
        assertEquals(pdf.uploadedAtUtc, reopened.services.single().attachments.single().uploadedAtUtc)
        val baseline = reopened.services.associate { it.id to serviceInputFingerprint(it.serviceInput(zone)) }
        assertTrue(serviceEditorChanges(reopened.services, setOf(recordId), baseline, emptyList(), emptySet(), emptySet(), zone).isEmpty())
        val hydrated = reopened.services.map { it.copy(attachments = it.attachments.map { file -> file.copy(uri = "content://private-preview") }) }
        assertEquals(serviceEditorFingerprint(reopened.services, reopened.tasks, reopened.suppressedPlannedServiceIds),
            serviceEditorFingerprint(hydrated, reopened.tasks, reopened.suppressedPlannedServiceIds))
    }

    @Test fun pendingNullAttachmentListRetainsPublishedMediaWhileExplicitEmptyListRemovesIt() {
        val pdf = FlightServiceAttachmentRecord("77777777-7777-7777-7777-777777777777", 2, "service.pdf",
            "application/pdf", 594, uploadedAtUtc = "2026-11-11T11:01:00Z")
        val published = record.copy(attachments = listOf(pdf))
        fun pending(attachments: List<FlightServiceAttachmentInput>?) = FlightServicePendingEntity("owner", flight.id, recordId,
            flight.flightNumber, "change", "save", Json.encodeToString(published.input().copy(attachments = attachments)),
            flight.rowVersion, Json.encodeToString(published), "now")
        val retained = initialServiceEditorDraft(flight.copy(services = listOf(published)), catalog, listOf(pending(null)), null, zone)
        assertEquals(pdf.id, retained.services.first { it.id == recordId }.attachments.single().id)
        val removed = initialServiceEditorDraft(flight.copy(services = listOf(published)), catalog, listOf(pending(emptyList())), null, zone)
        assertTrue(removed.services.first { it.id == recordId }.attachments.isEmpty())
    }

    @Test fun desiredParentBoundsValidateUnchangedWorkAndCorrectionsBeforeTheCombinedSave() {
        val original = initialServiceEditorDraft(flight.copy(services = listOf(record.copy(endUtc = "2026-11-11T14:00:00Z"))),
            catalog, emptyList(), null, zone).services.filter { it.id == recordId }
        val errors = serviceActualBoundsValidation(original, zone, "2026-11-11T12:00:00Z", "2026-11-11T13:00:00Z")
        assertEquals(R.string.service_validation_before_ata, errors[recordId]?.get("startUtc"))
        assertEquals(R.string.service_validation_after_atd, errors[recordId]?.get("endUtc"))
        assertEquals(R.string.service_validation_before_ata, errors[recordId]?.get("performers.$performerId.startUtc"))
        val corrected = original.map { it.copy(period = FlightWorkPeriod("2026-11-11T12:00", "2026-11-11T13:00"),
            employees = it.employees.map { employee -> employee.copy(period = FlightWorkPeriod("2026-11-11T12:00", "2026-11-11T13:00")) }) }
        assertTrue(serviceActualBoundsValidation(corrected, zone, "2026-11-11T12:00:00Z", "2026-11-11T13:00:00Z").isEmpty())
        assertTrue(serviceActualBoundsValidation(emptyList(), zone, "2026-11-11T12:00:00Z", "2026-11-11T13:00:00Z").isEmpty())
        assertTrue(parentTimeRelaxesBounds("2026-11-11T13:00:00Z", "2026-11-11T14:00:00Z", arrival = false))
        assertFalse(parentTimeRelaxesBounds("2026-11-11T13:00:00Z", "2026-11-11T12:00:00Z", arrival = false))
        assertFalse(parentTimeRelaxesBounds(null, "2026-11-11T12:00:00Z", arrival = true))
    }

    @Test fun plannedPrefillIsEligibleUnstartedAndGetsIndependentCurrentEmployeeRows() {
        val state = initialServiceEditorDraft(flight, catalog, emptyList(), null, zone)
        assertEquals(setOf(serviceId, secondServiceId), state.services.map { it.serviceId }.toSet())
        assertTrue(state.services.all { it.period.start.isBlank() && it.employees.single().period.start.isBlank() })
        assertTrue(state.services.all { it.employees.single().employeeId == staffId })
        assertEquals(2, state.services.map { it.employees.single().id }.distinct().size)
        assertTrue(serviceEditorChanges(state.services, emptySet(), emptyMap(), emptyList(), emptySet(), emptySet(), zone).isEmpty())
        assertTrue(initialServiceEditorDraft(flight.copy(isPerLanding = true), catalog, emptyList(), null, zone).services.isEmpty())
        assertEquals(listOf(serviceId), initialServiceEditorDraft(flight.copy(allowedServiceIds = listOf(serviceId)), catalog,
            emptyList(), null, zone).services.map { it.serviceId })
    }

    @Test fun savedAndPendingServicesKeepTheirOriginalPerformersAndDoNotCreateAnotherPlannedOccurrence() {
        val input = record.input().copy(notes = "Pending correction")
        val pending = FlightServicePendingEntity("owner", flight.id, recordId, flight.flightNumber, "change", "save",
            Json.encodeToString(input), flight.rowVersion, Json.encodeToString(record), "now")
        val state = initialServiceEditorDraft(flight.copy(services = listOf(record)), catalog, listOf(pending), null, zone)
        assertEquals(1, state.services.count { it.serviceId == serviceId })
        val edited = state.services.first { it.id == recordId }
        assertEquals("Pending correction", edited.remarks)
        assertEquals(otherStaffId, edited.employees.single().employeeId)
        assertEquals("Original employee", edited.employees.single().employeeName)
        assertEquals(performerId, edited.employees.single().id)
    }

    @Test fun opaqueLocalDraftPreservesRawInvalidDatesTasksAndExplicitPlannedRemovals() {
        val local = FlightServiceDraft(recordId, secondServiceId, "Second", FlightWorkPeriod("invalid date", "2026-11-11T12:00"))
        val task = FlightTaskDraft(period = FlightWorkPeriod("also invalid"), remarks = "Task remains local", tools = listOf(
            FlightResourceDraft(period = FlightWorkPeriod("2026-11-11T12:00"))))
        val stored = FlightServiceEditorDraft(zone.id, listOf(local), listOf(task), listOf(serviceId))
        val restored = initialServiceEditorDraft(flight, catalog, emptyList(), Json.encodeToString(stored), ZoneId.of("Asia/Riyadh"))
        assertEquals("invalid date", restored.services.single().period.start)
        assertEquals("2026-11-11T15:00", restored.services.single().period.end)
        assertEquals("also invalid", restored.tasks.single().period.start)
        assertEquals("2026-11-11T15:00", restored.tasks.single().tools.single().period.start)
        assertEquals(listOf(serviceId), restored.suppressedPlannedServiceIds)
        assertEquals(restored, decodeServiceEditorDraft(Json.encodeToString(restored)))
    }

    @Test fun missingOrInvalidStartedValuesCannotDisappearIntoAnUnstartedDraftOrSkipValidation() {
        val unstarted = FlightServiceDraft(recordId, serviceId, "Service")
        val baseline = mapOf(recordId to serviceInputFingerprint(unstarted.serviceInput(zone)))
        val invalid = unstarted.copy(period = FlightWorkPeriod("not a date"))
        val changes = serviceEditorChanges(listOf(invalid), emptySet(), baseline, emptyList(), emptySet(), emptySet(), zone)
        assertEquals(1, changes.size)
        assertEquals(R.string.service_validation_start, serviceEditorValidation(invalid, changes.single().input!!, flight,
            catalog, flight.rowVersion, null, null)["startUtc"])
        val clearedKnown = serviceEditorChanges(listOf(unstarted), setOf(recordId), emptyMap(), emptyList(), emptySet(), emptySet(), zone)
        assertNotNull(clearedKnown.single().input)
        assertEquals(listOf(FlightServiceChange(recordId, null)), serviceEditorChanges(emptyList(), setOf(recordId), baseline,
            emptyList(), emptySet(), emptySet(), zone))
    }

    @Test fun hydrationDoesNotDirtyTheEditorAndAmbiguousChangedTimesRemainInvalid() {
        val item = FlightServiceDraft(recordId, serviceId, "Service", attachments = listOf(FlightAttachmentDraft("media", "", "photo.png", "image/png")))
        val hydrated = item.copy(serviceName = "Updated display name", attachments = item.attachments.map {
            it.copy(uri = "content://private/preview", ownedCapture = false, uploadedAtUtc = "version") })
        assertEquals(serviceEditorFingerprint(listOf(item), emptyList(), emptyList()), serviceEditorFingerprint(listOf(hydrated), emptyList(), emptyList()))
        assertNotEquals(serviceEditorFingerprint(listOf(item), emptyList(), emptyList()), serviceEditorFingerprint(listOf(item.copy(remarks = "Changed")), emptyList(), emptyList()))
        val fold = item.copy(period = FlightWorkPeriod("2026-11-01T01:30", originalStartUtc = "2026-11-01T05:30:00Z"))
        assertEquals("2026-11-01T05:30:00Z", fold.serviceInput(ZoneId.of("America/New_York")).startUtc)
        assertEquals("", fold.copy(period = fold.period.copy(start = "2026-11-01T01:45")).serviceInput(ZoneId.of("America/New_York")).startUtc)
    }

    @Test fun actualBoundsAndPerformerProblemsAreAttachedToTheAffectedFields() {
        val item = FlightServiceDraft(recordId, serviceId, "Service", period = FlightWorkPeriod("2026-11-11T11:00", "2026-11-11T14:00"),
            reference = "x".repeat(201), employees = listOf(FlightEmployeeDraft(performerId, staffId, period = FlightWorkPeriod("2026-11-11T10:00", "2026-11-11T15:00"))))
        val errors = serviceEditorValidation(item, item.serviceInput(zone), flight, catalog, flight.rowVersion,
            "2026-11-11T12:00:00Z", "2026-11-11T13:00:00Z")
        assertEquals(R.string.service_validation_before_ata, errors["startUtc"])
        assertEquals(R.string.service_validation_after_atd, errors["endUtc"])
        assertEquals(R.string.service_validation_reference, errors["customerReferenceNumber"])
        assertEquals(R.string.service_validation_before_ata, errors["performers.$performerId.startUtc"])
        assertEquals(R.string.service_validation_after_atd, errors["performers.$performerId.endUtc"])
    }

    @Test fun realRiyadhBeforeArrivalCaseKeepsValidStaffAndFlagsOnlyTheirStartTime() {
        val riyadh = ZoneId.of("Asia/Riyadh")
        val arrival = "2026-11-12T06:00:31.187126Z"
        val item = FlightServiceDraft(recordId, serviceId, "Service", period = FlightWorkPeriod("2026-11-12T02:20"),
            employees = listOf(FlightEmployeeDraft(performerId, staffId, "Current employee", period = FlightWorkPeriod("2026-11-12T02:20"))))
        val request = item.serviceInput(riyadh)
        assertEquals("2026-11-11T23:20:00Z", request.performers.single().startUtc)
        val errors = serviceEditorValidation(item, request, flight.copy(stationTimeZoneId = riyadh.id), catalog,
            flight.rowVersion, arrival, null)
        assertEquals(mapOf("startUtc" to R.string.service_validation_before_ata,
            "performers.$performerId.startUtc" to R.string.service_validation_before_ata), errors)
        assertFalse("performers" in errors)
        assertFalse("performers.$performerId.staffMemberId" in errors)
    }

    @Test fun staffHeaderIsReservedForEmptyOrDuplicateAssignments() {
        val employee = FlightEmployeeDraft(performerId, staffId, period = FlightWorkPeriod("2026-11-11T11:00"))
        val item = FlightServiceDraft(recordId, serviceId, "Service", period = FlightWorkPeriod("2026-11-11T11:00"))
        val emptyErrors = serviceEditorValidation(item, item.serviceInput(zone), flight, catalog, flight.rowVersion, null, null)
        assertEquals(mapOf("performers" to R.string.service_validation_performers), emptyErrors)
        val duplicate = item.copy(employees = listOf(employee, employee.copy(id = "77777777-7777-7777-7777-777777777777")))
        assertEquals(mapOf("performers" to R.string.service_validation_performers),
            serviceEditorValidation(duplicate, duplicate.serviceInput(zone), flight, catalog, flight.rowVersion, null, null))
    }

    @Test fun invalidStaffAndRecordIdentitiesAreAttachedToTheirOwnChooserInsteadOfTheStaffHeader() {
        val item = FlightServiceDraft(recordId, serviceId, "Service", period = FlightWorkPeriod("2026-11-11T11:00"),
            employees = listOf(FlightEmployeeDraft(performerId, "invalid-staff", period = FlightWorkPeriod("2026-11-11T11:00"))))
        assertEquals(mapOf("performers.$performerId.staffMemberId" to R.string.service_validation_staff),
            serviceEditorValidation(item, item.serviceInput(zone), flight, catalog, flight.rowVersion, null, null))
        val badRecord = item.copy(employees = item.employees.map { it.copy(id = "invalid-row", employeeId = staffId) })
        assertEquals(mapOf("performers.invalid-row.staffMemberId" to R.string.service_validation_version),
            serviceEditorValidation(badRecord, badRecord.serviceInput(zone), flight, catalog, flight.rowVersion, null, null))
    }

    @Test fun reorderedRequestsKeepInvalidTimesOnTheStableEmployeeRow() {
        val second = "00000000-0000-0000-0000-000000000001"
        val available = catalog.copy(stationStaff = catalog.stationStaff + StaffCatalogItem(otherStaffId, "Other employee", "E2", "station", "AAA", "Engineer"))
        val item = FlightServiceDraft(recordId, serviceId, "Service", period = FlightWorkPeriod("2026-11-11T11:00", "2026-11-11T14:00"),
            employees = listOf(
                FlightEmployeeDraft(performerId, staffId, period = FlightWorkPeriod("invalid time", "2026-11-11T15:00")),
                FlightEmployeeDraft(second, otherStaffId, period = FlightWorkPeriod("2026-11-11T11:00", "2026-11-11T12:00"))))
        val request = item.serviceInput(zone)
        assertEquals(second, request.performers.first().id)
        assertEquals(mapOf("performers.$performerId.startUtc" to R.string.service_validation_staff_period,
            "performers.$performerId.endUtc" to R.string.service_validation_after_atd),
            serviceEditorValidation(item, request, flight, available, flight.rowVersion, null, "2026-11-11T14:00:00Z"))
    }

    @Test fun actualTimeEqualityIncludingFractionalArrivalSecondsIsAllowed() {
        val riyadh = ZoneId.of("Asia/Riyadh")
        val bound = "2026-11-12T06:00:31.187126Z"
        val period = FlightWorkPeriod("2026-11-12T09:00:31.187126", "2026-11-12T09:00:31.187126")
        val item = FlightServiceDraft(recordId, serviceId, "Service", period = period,
            employees = listOf(FlightEmployeeDraft(performerId, staffId, period = period)))
        assertTrue(serviceEditorValidation(item, item.serviceInput(riyadh), flight.copy(stationTimeZoneId = riyadh.id), catalog,
            flight.rowVersion, bound, bound).isEmpty())
        assertTrue(serviceActualBoundsValidation(listOf(item), riyadh, bound, bound).isEmpty())
    }

    @Test fun departureRequiresServiceAndStaffEndsButAnOrdinaryWorkSaveStillAllowsBothOpen() {
        val open = record.copy(canEdit = true)
        assertTrue(open.input().validationErrors(flight.rowVersion).isEmpty())
        val errors = departureServiceCompletionValidation(emptyList(), listOf(open), emptyList(), emptyList(), zone)
        assertEquals(mapOf("endUtc" to R.string.departure_service_end_required,
            "performers.$performerId.endUtc" to R.string.departure_staff_end_required), errors[recordId])
    }

    @Test fun endingServiceFlagsItsEndAndOnlyStaffPeriodsStillOpen() {
        val secondPerformer = "77777777-7777-7777-7777-777777777777"
        val item = FlightServiceDraft(recordId, serviceId, "Service", period = FlightWorkPeriod("2026-11-11T11:00", "2026-11-11T14:00"),
            employees = listOf(FlightEmployeeDraft(performerId, staffId, period = FlightWorkPeriod("2026-11-11T11:00")),
                FlightEmployeeDraft(secondPerformer, otherStaffId, period = FlightWorkPeriod("2026-11-11T12:00", "2026-11-11T13:00"))))
        val available = catalog.copy(stationStaff = catalog.stationStaff + StaffCatalogItem(otherStaffId, "Other employee", "E2", "station", "AAA", "Engineer"))
        assertEquals(mapOf("endUtc" to R.string.service_validation_open_staff,
            "performers.$performerId.endUtc" to R.string.service_validation_staff_end_required),
            serviceEditorValidation(item, item.serviceInput(zone), flight, available, flight.rowVersion, null, null))
        val closed = item.copy(employees = item.employees.map { it.copy(period = it.period.copy(end = "2026-11-11T13:00")) })
        assertTrue(serviceEditorValidation(closed, closed.serviceInput(zone), flight, available, flight.rowVersion, null, null).isEmpty())
        val reopened = item.copy(period = item.period.copy(end = ""))
        assertTrue(serviceEditorValidation(reopened, reopened.serviceInput(zone), flight, available, flight.rowVersion, null, null).isEmpty())
    }

    @Test fun departureUsesLatestQueuedAndUnsavedClosuresAndHonorsRemovals() {
        val open = record.copy(canEdit = true)
        val closed = open.input().copy(endUtc = "2026-11-11T14:00:00Z",
            performers = open.input().performers.map { it.copy(endUtc = "2026-11-11T13:00:00Z") })
        fun queued(input: SaveFlightServiceRequest?) = FlightServicePendingEntity("owner", flight.id, recordId, flight.flightNumber,
            "change", if (input == null) "delete" else "save", input?.let { Json.encodeToString(it) }, flight.rowVersion,
            Json.encodeToString(open), "now")
        assertTrue(departureServiceCompletionValidation(emptyList(), listOf(open), listOf(queued(closed)), emptyList(), zone).isEmpty())
        assertTrue(departureServiceCompletionValidation(emptyList(), listOf(open), listOf(queued(open.input())),
            listOf(FlightServiceChange(recordId, closed)), zone).isEmpty())
        assertTrue(departureServiceCompletionValidation(emptyList(), listOf(open), emptyList(),
            listOf(FlightServiceChange(recordId, null)), zone).isEmpty())
        assertTrue(departureServiceCompletionValidation(emptyList(), listOf(open), listOf(queued(null)), emptyList(), zone).isEmpty())
        val reopened = closed.copy(performers = closed.performers.map { it.copy(endUtc = null) })
        assertEquals(mapOf("performers.$performerId.endUtc" to R.string.departure_staff_end_required),
            departureServiceCompletionValidation(emptyList(), listOf(open), listOf(queued(closed)),
                listOf(FlightServiceChange(recordId, reopened)), zone)[recordId])
    }

    @Test fun unstartedPlannedRowsDoNotBlockDepartureButNewStartedWorkDoes() {
        val unstarted = FlightServiceDraft(recordId, serviceId, "Planned service", employees = listOf(
            FlightEmployeeDraft(performerId, staffId)))
        assertTrue(departureServiceCompletionValidation(listOf(unstarted), emptyList(), emptyList(), emptyList(), zone).isEmpty())
        val started = unstarted.copy(period = FlightWorkPeriod("2026-11-11T11:00"),
            employees = unstarted.employees.map { it.copy(period = FlightWorkPeriod("2026-11-11T11:00")) })
        assertEquals(setOf("endUtc", "performers.$performerId.endUtc"),
            departureServiceCompletionValidation(listOf(started), emptyList(), emptyList(), emptyList(), zone)[recordId]?.keys)
    }

    @Test fun anotherOperatorClosingOnlyTheServiceCannotBypassOpenCreatorStaffPeriods() {
        val closeService = record.input().copy(endUtc = "2026-11-11T14:00:00Z")
        assertFalse(record.canEdit)
        assertTrue(record.canSetEnd)
        assertEquals(mapOf("performers.$performerId.endUtc" to R.string.departure_staff_end_required),
            departureServiceCompletionValidation(emptyList(), listOf(record), emptyList(), listOf(FlightServiceChange(recordId, closeService)), zone)[recordId])
        val staffFinished = record.copy(performers = record.performers.map { it.copy(endUtc = "2026-11-11T13:00:00Z") })
        assertTrue(departureServiceCompletionValidation(emptyList(), listOf(staffFinished), emptyList(),
            listOf(FlightServiceChange(recordId, staffFinished.input().copy(endUtc = "2026-11-11T14:00:00Z"))), zone).isEmpty())
    }

    @Test fun recordedDepartureRequiresOnlyChangedSavesToBeCompleteDuringIncrementalRepair() {
        val other = record.copy(id = "77777777-7777-7777-7777-777777777777")
        val closed = record.input().copy(endUtc = record.startUtc,
            performers = record.input().performers.map { it.copy(endUtc = it.startUtc) })
        val changes = listOf(FlightServiceChange(recordId, closed))
        assertTrue(departureServiceCompletionValidation(emptyList(), listOf(record, other), emptyList(), changes, zone,
            onlyRecordIds = setOf(recordId)).isEmpty())
        assertEquals(setOf(other.id), departureServiceCompletionValidation(emptyList(), listOf(record, other), emptyList(), changes, zone).keys)
        val malformed = closed.copy(endUtc = "invalid", performers = closed.performers.map { it.copy(endUtc = "invalid") })
        assertEquals(mapOf("endUtc" to R.string.service_validation_end,
            "performers.$performerId.endUtc" to R.string.service_validation_staff_period),
            departureServiceCompletionValidation(emptyList(), listOf(record), emptyList(), listOf(FlightServiceChange(recordId, malformed)), zone)[recordId])
    }

    @Test fun unchangedHistoricalStaffCanRemainWhileNewOrReplacedSelectionsMustBeCurrent() {
        val reviewed = flight.copy(services = listOf(record))
        val item = initialServiceEditorDraft(reviewed, catalog, emptyList(), null, zone).services.first { it.id == recordId }
        val closing = record.input().copy(endUtc = "2026-11-11T14:00:00Z",
            performers = record.input().performers.map { it.copy(endUtc = "2026-11-11T13:00:00Z") })
        assertTrue(catalog.stationStaff.none { it.id == otherStaffId })
        assertTrue(serviceEditorValidation(item, closing, reviewed, catalog, flight.rowVersion, null, null).isEmpty())
        val replacementId = "88888888-8888-8888-8888-888888888888"
        val replaced = item.copy(employees = item.employees.map { it.copy(employeeId = replacementId) })
        assertEquals(R.string.service_validation_staff, serviceEditorValidation(replaced, replaced.serviceInput(zone),
            reviewed, catalog, flight.rowVersion, null, null)["performers.$performerId.staffMemberId"])
        val recreated = item.copy(employees = item.employees.map { it.copy(id = "99999999-9999-9999-9999-999999999999") })
        assertEquals(R.string.service_validation_staff, serviceEditorValidation(recreated, recreated.serviceInput(zone),
            reviewed, catalog, flight.rowVersion, null, null)["performers.99999999-9999-9999-9999-999999999999.staffMemberId"])
    }
}
