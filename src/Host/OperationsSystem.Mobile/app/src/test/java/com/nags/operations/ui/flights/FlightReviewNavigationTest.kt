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
        assertEquals("online field three", roundTrip.customerReferenceNumber)
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
        assertEquals("online field three", roundTrip.customerReferenceNumber)
        assertEquals(0, roundTrip.taskType)
        assertEquals(end, roundTrip.tools.single().endUtc)
        assertEquals(id(51), roundTrip.tools.single().id)
        assertEquals("2.5", roundTrip.materials.single().quantity)
        assertEquals("content://selected/task", roundTrip.attachments?.single { it.id == selectedFile.id }?.contentUri)
    }
}
