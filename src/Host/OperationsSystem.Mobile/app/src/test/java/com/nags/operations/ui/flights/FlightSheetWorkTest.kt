package com.nags.operations.ui.flights

import com.nags.operations.data.AtaChapterCatalogItem
import com.nags.operations.data.FlightDetail
import com.nags.operations.data.FlightServiceRecord
import com.nags.operations.data.PlannedService
import com.nags.operations.data.SaveFlightServiceRequest
import com.nags.operations.data.SaveFlightTaskRequest
import com.nags.operations.data.ServiceCatalogItem
import com.nags.operations.data.db.entities.FlightServicePendingEntity
import com.nags.operations.data.db.entities.FlightTaskPendingEntity
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class FlightSheetWorkTest {
    private val flight = FlightDetail("flight", "TEST", "customer", customerName = "Airline", stationId = "station",
        stationIata = "JED", stationName = "Jeddah", operationTypeId = "operation", operationTypeName = "Transit",
        scheduledArrivalUtc = "2026-11-12T09:00:00Z", scheduledDepartureUtc = "2026-11-12T12:00:00Z",
        isPerLanding = false, createdAtUtc = "2026-11-12T09:00:00Z", rowVersion = "version", stationTimeZoneId = "Asia/Riyadh")
    private val catalogs = FlightOperationsCatalogs(services = listOf(ServiceCatalogItem("catalog-service", "Transit check", false)),
        ataChapters = listOf(AtaChapterCatalogItem("chapter", "category", "Airframe", "32", "Landing gear")))

    @Test fun plannedWorkBeforeArrivalIsEmptyButRecordedWorkDoesNotRequireArrival() {
        val planned = flight.copy(plannedServices = listOf(PlannedService("catalog-service", "Transit check", false)),
            allowedServiceIds = listOf("catalog-service"))
        assertTrue(flightSheetWorkItems(planned, emptyList(), emptyList(), catalogs).isEmpty())
        val recorded = planned.copy(services = listOf(service("record", "2026-11-12T09:20:00Z")))
        val items = flightSheetWorkItems(recorded, emptyList(), emptyList(), catalogs)
        assertEquals(1, items.size)
        assertEquals("service:record", items.single().key)
        assertEquals(null, items.single().endUtc)
        assertEquals("2026-11-12T09:20:00Z", items.single().startUtc)
        assertTrue(recorded.arrivalSubmissions.isEmpty())
    }

    @Test fun pendingDesiredStateReplacesSavedRecordAndDeletionRemovesIt() {
        val saved = flight.copy(services = listOf(service("edit", "2026-11-12T09:20:00Z"), service("delete", "2026-11-12T09:30:00Z")))
        val corrected = SaveFlightServiceRequest("catalog-service", "2026-11-12T09:21:00Z", "2026-11-12T09:50:00Z")
        val changes = listOf(pendingService("edit", "save", corrected).copy(status = "conflict"),
            pendingService("delete", "delete", null), pendingService("unrelated", "save", corrected).copy(flightId = "another"))
        val item = flightSheetWorkItems(saved, changes, emptyList(), catalogs).single()
        assertEquals("service:edit", item.key)
        assertEquals("2026-11-12T09:21:00Z", item.startUtc)
        assertEquals("2026-11-12T09:50:00Z", item.endUtc)
        assertEquals(FlightSheetWorkSync.NeedsReview, item.sync)
        assertEquals("Transit check", item.name)
    }

    @Test fun pendingTasksResolveChapterAndRemainDistinctFromServiceWithSameRecordId() {
        val request = SaveFlightTaskRequest(1, "2026-11-12T09:10:00Z", ataChapterId = "chapter")
        val task = FlightTaskPendingEntity("owner", flight.id, "shared", flight.flightNumber, "change", "save",
            Json.encodeToString(request), "version", null, "2026-11-12T09:11:00Z")
        val items = flightSheetWorkItems(flight.copy(services = listOf(service("shared", "2026-11-12T09:20:00Z"))),
            emptyList(), listOf(task), catalogs)
        assertEquals(listOf("task:shared", "service:shared"), items.map { it.key })
        assertEquals(FlightSheetWorkKind.MinorTask, items.first().kind)
        assertEquals("32", items.first().ataCode)
        assertEquals("Landing gear", items.first().ataTitle)
        assertEquals(FlightSheetWorkSync.Pending, items.first().sync)
    }

    private fun service(id: String, start: String) = FlightServiceRecord(id, "catalog-service", "Transit check", start,
        createdByUserId = "user", createdAtUtc = start)

    private fun pendingService(id: String, operation: String, request: SaveFlightServiceRequest?) = FlightServicePendingEntity(
        "owner", flight.id, id, flight.flightNumber, "change-$id", operation, request?.let { Json.encodeToString(it) },
        "version", null, "2026-11-12T10:00:00Z")
}
