package com.nags.operations.ui.flights

import com.nags.operations.data.*
import com.nags.operations.data.db.entities.*
import java.time.ZoneId
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import org.junit.Assert.*
import org.junit.Test

class FlightReviewNavigationTest {
    private val start = "2026-11-10T12:00:00Z"
    private val end = "2026-11-10T12:10:00Z"
    private val zone = ZoneId.of("Asia/Riyadh")
    private fun id(value: Int) = "00000000-0000-0000-0000-${value.toString().padStart(12, '0')}"

    @Test fun cachedReviewSourceSurvivesSavedNavigationEvenThoughFlightCacheFlagIsTransient() {
        val cached = FlightDetail(id(1), "SV100", "customer", customerName = "Customer", stationId = "station",
            stationIata = "AAA", stationName = "Station", operationTypeId = "operation", operationTypeName = "Transit",
            scheduledArrivalUtc = start, scheduledDepartureUtc = end, isPerLanding = false,
            createdAtUtc = start, rowVersion = "AAAAAAAAAAE=", stationTimeZoneId = zone.id, fromCache = true)
        val snapshot = FlightWorkUpdateSnapshot(cached, FlightWorkDrafts(services = listOf(FlightServiceDraft(id = id(2)))),
            isService = true, reviewMode = true, fromCache = cached.fromCache)

        val restored = Json.decodeFromString<FlightWorkUpdateSnapshot>(Json.encodeToString(snapshot))

        assertTrue(restored.reviewMode)
        assertTrue(restored.fromCache)
        assertEquals(id(2), restored.recordId)
        assertFalse(restored.flight.fromCache)
        assertEquals(cached.rowVersion, restored.flight.rowVersion)
    }

    @Test fun reviewNavigationIncludesOnlyFailedCardsInPhysicalOrderWithExactRecordAndChangeIds() {
        val arrival = FlightArrivalPendingEntity("owner", "flight", "SV100", "arrival-change", "{}", "version", start, status = "conflict")
        val departure = FlightDeparturePendingEntity("owner", "flight", "SV100", "staff", "departure-change", "{}", "version", "[]", start, status = "failed")
        fun service(record: String, change: String, status: String) = FlightServicePendingEntity("owner", "flight", record,
            "SV100", change, "save", "{}", "version", null, start, status = status)
        fun task(record: String, change: String, status: String) = FlightTaskPendingEntity("owner", "flight", record,
            "SV100", change, "save", "{}", "version", null, start, status = status)

        assertEquals(listOf(FlightOperationReviewTarget("arrival", changeId = "arrival-change"),
            FlightOperationReviewTarget("service", "service-two", "service-two-change"),
            FlightOperationReviewTarget("service", "service-three", "service-three-change"),
            FlightOperationReviewTarget("task", "task-one", "task-one-change"),
            FlightOperationReviewTarget("departure", changeId = "departure-change")), flightReviewTargets(arrival, departure,
            listOf(service("service-one", "service-one-change", "pending"), service("service-two", "service-two-change", "failed"),
                service("service-three", "service-three-change", "conflict")),
            listOf(task("task-one", "task-one-change", "conflict"), task("task-two", "task-two-change", "pending"))))
        assertTrue(flightReviewTargets(arrival.copy(status = "pending"), departure.copy(status = "pending"), emptyList(), emptyList()).isEmpty())
    }

    @Test fun servicePatchSurvivesEditorRoundTripWithOnlineFieldThreeCompletedEndsStaffIdsAndPrivateMedia() {
        val onlineFile = FlightServiceAttachmentInput(id(31), 2, "recorded.pdf", "application/pdf")
        val privateFile = FlightServiceAttachmentInput(id(32), 2, "local.pdf", "application/pdf")
        val selectedFile = FlightServiceAttachmentInput(id(33), 0, "new.png", "image/png", contentUri = "content://selected/new")
        val online = SaveFlightServiceRequest(id(1), start, end, "online field three", "recorded notes",
            performers = listOf(SaveFlightServicePerformerRequest(id(11), id(21), start, end)), attachments = listOf(onlineFile))
        val local = online.copy(endUtc = null, customerReferenceNumber = " ", notes = "local notes",
            performers = listOf(online.performers.single().copy(id = id(99), endUtc = null)), attachments = listOf(privateFile, selectedFile))
        val merged = mergeServiceReview(online, local)

        val roundTrip = merged.toEditorDraft(id(2), "Service", null, FlightOperationsCatalogs(), zone)
            .serviceInput(zone, setOf(onlineFile.id, privateFile.id))

        assertEquals(merged, roundTrip)
        assertEquals("ONLINE FIELD THREE", roundTrip.customerReferenceNumber)
        assertEquals(end, roundTrip.endUtc)
        assertEquals(id(11), roundTrip.performers.single().id)
        assertEquals(end, roundTrip.performers.single().endUtc)
        assertEquals("content://selected/new", roundTrip.attachments?.single { it.id == selectedFile.id }?.contentUri)
    }

    @Test fun taskPatchSurvivesEditorRoundTripWithCompletedResourcesAndRetainedMedia() {
        val onlineFile = FlightServiceAttachmentInput(id(31), 2, "recorded.pdf", "application/pdf")
        val selectedFile = FlightServiceAttachmentInput(id(32), 0, "new.png", "image/png", contentUri = "content://selected/task")
        val duration = SaveFlightTaskResourceRequest(id(51), id(61), startUtc = start, endUtc = end, notes = "recorded resource notes")
        val online = SaveFlightTaskRequest(1, start, end, id(41), "online field three", "recorded notes",
            performers = listOf(SaveFlightTaskPerformerRequest(id(11), id(21), start, end, "staff notes")),
            tools = listOf(duration), materials = listOf(SaveFlightTaskResourceRequest(id(52), id(62), "2.50")), attachments = listOf(onlineFile))
        val local = online.copy(taskType = 0, endUtc = null, ataChapterId = null, customerReferenceNumber = null, notes = "local notes",
            performers = listOf(online.performers.single().copy(id = id(99), endUtc = null, notes = "")),
            tools = listOf(duration.copy(id = id(98), endUtc = null)), materials = emptyList(), attachments = listOf(selectedFile))
        val merged = mergeTaskReview(online, local)

        val roundTrip = merged.toTaskEditorDraft(id(2), null, FlightOperationsCatalogs(), zone)
            .taskInput(zone, setOf(onlineFile.id))

        assertEquals(merged, roundTrip)
        assertEquals("ONLINE FIELD THREE", roundTrip.customerReferenceNumber)
        assertEquals(0, roundTrip.taskType)
        assertEquals(end, roundTrip.tools.single().endUtc)
        assertEquals(id(51), roundTrip.tools.single().id)
        assertEquals("2.5", roundTrip.materials.single().quantity)
        assertEquals("content://selected/task", roundTrip.attachments?.single { it.id == selectedFile.id }?.contentUri)
    }

    @Test fun pendingNavigationSelectsExactQueuedOccurrencesInPhysicalOrderWithoutEnteringReview() {
        val arrival = FlightArrivalPendingEntity("owner", "flight", "SV100", "arrival-change", "{}", "version", start)
        val departure = FlightDeparturePendingEntity("owner", "flight", "SV100", "staff", "departure-change", "{}", "version", "[]", start)
        fun service(record: String, status: String = "pending") = FlightServicePendingEntity("owner", "flight", record,
            "SV100", "$record-change", "save", "{}", "version", null, start, status = status)
        fun task(record: String, status: String = "pending") = FlightTaskPendingEntity("owner", "flight", record,
            "SV100", "$record-change", "save", "{}", "version", null, start, status = status)

        val targets = flightPendingTargets(arrival, departure,
            listOf(service("first-service"), service("failed-service", "failed"), service("second-service")),
            listOf(task("conflicted-task", "conflict"), task("queued-task")))

        assertEquals(listOf("arrival", "service:first-service", "service:second-service", "task:queued-task", "departure"),
            targets.map { it.navigationKey() })
        assertEquals("second-service-change", targets[2].changeId)
        assertEquals("second-service", targets[2].recordId)
        assertTrue(targets.all { !it.useLocalSource && it.stagedInputJson == null })
        assertEquals("departure", flightPendingTargets(null, departure, emptyList(), emptyList()).first().navigationKey())
        assertTrue(flightPendingTargets(arrival.copy(status = "conflict"), departure.copy(status = "failed"), emptyList(), emptyList()).isEmpty())
    }

    @Test fun openingServiceReviewAppliesSparseLocalInputWhilePreservingOnlineReferenceEndsAndSibling() {
        val recorded = FlightServiceRecord(id(2), id(3), "Recorded service", start, end, "online reference", "recorded notes",
            createdByUserId = id(90), createdAtUtc = start, performers = listOf(FlightServicePerformerRecord(id(11), id(21), "Recorded employee", "E1", start, end)))
        val sibling = recorded.copy(id = id(4), notes = "Sibling notes")
        val flight = FlightDetail(id(1), "SV100", "customer", customerName = "Customer", stationId = "station",
            stationIata = "AAA", stationName = "Station", operationTypeId = "operation", operationTypeName = "Transit",
            scheduledArrivalUtc = start, scheduledDepartureUtc = end, isPerLanding = false,
            createdAtUtc = start, rowVersion = "AAAAAAAAAAE=", stationTimeZoneId = zone.id, services = listOf(recorded, sibling))
        val source = SaveFlightServiceRequest("", "", notes = "Local notes",
            performers = listOf(SaveFlightServicePerformerRequest(id(99), id(21), "")))
        val snapshot = FlightWorkUpdateSnapshot(flight, FlightWorkDrafts(services = listOf(source.toEditorDraft(recorded.id,
            recorded.serviceName, recorded, FlightOperationsCatalogs(), zone))), isService = true, reviewMode = true)

        val staged = flightWorkReviewDrafts(snapshot, FlightOperationsCatalogs(), zone, serviceSource = source)
        val input = staged.services.single().serviceInput(zone)

        assertEquals("Local notes", input.notes)
        assertEquals("ONLINE REFERENCE", input.customerReferenceNumber)
        assertEquals(start, input.startUtc)
        assertEquals(end, input.endUtc)
        assertEquals(id(11), input.performers.single().id)
        assertEquals(end, input.performers.single().endUtc)
        assertEquals("Sibling notes", snapshot.flight.services[1].notes)
        assertEquals("", snapshot.drafts.services.single().serviceId)
        assertEquals("Local notes", snapshot.drafts.services.single().remarks)
        assertEquals("", source.startUtc)
        val restored = Json.decodeFromString<FlightWorkUpdateSnapshot>(Json.encodeToString(snapshot.copy(drafts = staged)))
        assertEquals(input, restored.drafts.services.single().serviceInput(zone))
    }

    @Test fun openingTaskReviewKeepsAbsentTaskTypeAndRecordedResourceFieldsAlongsideLocalNotes() {
        val recorded = FlightTaskRecord(id(2), 1, start, end, customerReferenceNumber = "Online ref", notes = "Recorded notes",
            createdByUserId = id(90), createdAtUtc = start, tools = listOf(FlightTaskResourceRecord(id(51), id(61), "Recorded tool", 1, startUtc = start, endUtc = end)))
        val flight = FlightDetail(id(1), "SV100", "customer", customerName = "Customer", stationId = "station",
            stationIata = "AAA", stationName = "Station", operationTypeId = "operation", operationTypeName = "Transit",
            scheduledArrivalUtc = start, scheduledDepartureUtc = end, isPerLanding = false,
            createdAtUtc = start, rowVersion = "AAAAAAAAAAE=", stationTimeZoneId = zone.id, tasks = listOf(recorded))
        val source = SaveFlightTaskRequest(-1, "", notes = "Local notes",
            tools = listOf(SaveFlightTaskResourceRequest(id(99), id(61))))
        val snapshot = FlightWorkUpdateSnapshot(flight, FlightWorkDrafts(tasks = listOf(source.toTaskEditorDraft(recorded.id,
            recorded, FlightOperationsCatalogs(), zone))), isService = false, reviewMode = true)

        val input = flightWorkReviewDrafts(snapshot, FlightOperationsCatalogs(), zone, taskSource = source)
            .tasks.single().taskInput(zone)

        assertEquals(1, input.taskType)
        assertEquals("Local notes", input.notes)
        assertEquals("ONLINE REF", input.customerReferenceNumber)
        assertEquals(start, input.startUtc)
        assertEquals(end, input.endUtc)
        assertEquals(id(51), input.tools.single().id)
        assertEquals(end, input.tools.single().endUtc)
        assertEquals(-1, source.taskType)
        assertNull(source.tools.single().endUtc)
    }


    @Test fun correctionOverlapUsesSiblingPendingDeletionsAndAdditionsWithoutRestoringOrDroppingTheEditedRecord() {
        val recorded = FlightServiceRecord(id(2), id(3), "Service", start, end, createdByUserId = id(90), createdAtUtc = start)
        val overlapping = recorded.copy(id = id(4), startUtc = "2026-11-10T12:05:00Z", endUtc = "2026-11-10T12:15:00Z")
        val flight = FlightDetail(id(1), "SV100", "customer", customerName = "Customer", stationId = "station",
            stationIata = "AAA", stationName = "Station", operationTypeId = "operation", operationTypeName = "Transit",
            scheduledArrivalUtc = start, scheduledDepartureUtc = end, isPerLanding = false,
            createdAtUtc = start, rowVersion = "AAAAAAAAAAE=", stationTimeZoneId = zone.id, services = listOf(recorded, overlapping))
        val catalogs = FlightOperationsCatalogs()
        val edited = recorded.input().toEditorDraft(recorded.id, recorded.serviceName, recorded, catalogs, zone)
        val deletion = FlightServicePendingEntity("owner", flight.id, overlapping.id, flight.flightNumber,
            "delete-change", "delete", null, flight.rowVersion, Json.encodeToString(overlapping), start)
        val addition = deletion.copy(serviceRecordId = id(5), operation = "save", inputJson = Json.encodeToString(overlapping.input()), reviewedServiceJson = null)
        val staleCurrent = deletion.copy(serviceRecordId = recorded.id, operation = "save", inputJson = Json.encodeToString(overlapping.input()))

        assertTrue(flightServiceCorrectionOverlapErrors(flight, emptyList(), catalogs, zone, edited).containsKey("startUtc"))
        assertTrue(flightServiceCorrectionOverlapErrors(flight, listOf(deletion, staleCurrent), catalogs, zone, edited).isEmpty())
        assertTrue(flightServiceCorrectionOverlapErrors(flight.copy(services = listOf(recorded)), listOf(addition), catalogs, zone, edited).containsKey("startUtc"))
        assertEquals(listOf(recorded.id, overlapping.id), flight.services.map { it.id })
        assertEquals("delete", deletion.operation)
    }

}
