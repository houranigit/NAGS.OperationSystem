package com.nags.operations.data

import com.nags.operations.data.db.entities.FlightCancellationPendingEntity
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class FlightModelsContractTest {
    private val json = Json { ignoreUnknownKeys = true }

    @Test
    fun pagedRegisterReadsServerNavigationAndLongTotalCount() {
        val page = json.decodeFromString<FlightPage>(
            """
            {
              "items": [{
                "id": "flight-1", "flightNumber": "104", "customerIataCode": null,
                "customerName": "Customer", "stationIataCode": "RUH", "stationTimeZoneId": "Asia/Riyadh", "operationTypeName": "Transit",
                "scheduledArrivalUtc": "2026-11-03T08:00:00+00:00",
                "scheduledDepartureUtc": "2026-11-03T10:00:00+00:00", "isPerLanding": false
              }],
              "page": 2, "pageSize": 20, "totalCount": 2147483648,
              "totalPages": 107374183, "hasPreviousPage": true, "hasNextPage": true
            }
            """.trimIndent(),
        )

        assertEquals(2_147_483_648L, page.totalCount)
        assertEquals(2, page.page)
        assertTrue(page.hasPreviousPage)
        assertTrue(page.hasNextPage)
        assertNull(page.items.single().customerIataCode)
        assertNull(page.items.single().aircraftTypeModel) // Previously saved registers omit this field.
        assertNull(page.items.single().customerId)
        assertNull(page.items.single().customerLogoVersion)
        assertEquals("2026-11-03T08:00:00+00:00", page.items.single().scheduledArrivalUtc)
    }

    @Test
    fun aircraftModelSurvivesRegisterCacheSerialization() {
        val page = json.decodeFromString<FlightPage>(
            """
            {"items":[{
              "id":"flight-1","flightNumber":"TEST","customerName":"Customer",
              "stationIataCode":"RUH","stationTimeZoneId":"Asia/Riyadh","operationTypeName":"HAJJ","aircraftTypeModel":"A310","customerId":"customer-1","customerLogoVersion":"none",
              "scheduledArrivalUtc":"2026-11-04T10:00:00Z",
              "scheduledDepartureUtc":"2026-11-04T12:00:00Z","isPerLanding":false
            }]}
            """.trimIndent(),
        )

        val cached = json.decodeFromString<List<FlightListItem>>(json.encodeToString(page.items))

        assertEquals("A310", page.items.single().aircraftTypeModel)
        assertEquals("A310", cached.single().aircraftTypeModel)
        assertEquals("customer-1", cached.single().customerId)
        assertEquals("none", cached.single().customerLogoVersion)
    }

    @Test
    fun olderRegistersKeepMissingCycleSummariesUnknown() {
        val flight = json.decodeFromString<FlightListItem>(
            """{"id":"flight-1","flightNumber":"104","customerName":"Customer",
                "stationIataCode":"RUH","stationTimeZoneId":"Asia/Riyadh","operationTypeName":"Transit",
                "scheduledArrivalUtc":"2026-11-03T08:00:00Z","scheduledDepartureUtc":"2026-11-03T10:00:00Z",
                "isPerLanding":false}""",
        )

        assertNull(flight.hasOpenReturnToRamp)
        assertNull(flight.latestReturnToRampSequence)
        assertNull(flight.activeWorkCount)
        assertNull(flight.completedWorkCount)
        assertNull(flight.latestCancellationAtUtc)
    }

    @Test
    fun currentCycleSummariesAndKnownZeroWorkSurviveRegisterCaching() {
        val flight = json.decodeFromString<FlightListItem>(
            """{"id":"flight-1","flightNumber":"104","customerName":"Customer",
                "stationIataCode":"RUH","stationTimeZoneId":"Asia/Riyadh","operationTypeName":"Transit",
                "scheduledArrivalUtc":"2026-11-03T08:00:00Z","scheduledDepartureUtc":"2026-11-03T10:00:00Z",
                "isPerLanding":false,"hasOpenReturnToRamp":true,"latestReturnToRampSequence":2,
                "activeWorkCount":0,"completedWorkCount":3,"latestCancellationAtUtc":"2026-11-03T07:30:00Z"}""",
        )
        val cached = json.decodeFromString<FlightListItem>(json.encodeToString(flight))

        assertEquals(true, cached.hasOpenReturnToRamp)
        assertEquals(2, cached.latestReturnToRampSequence)
        assertEquals(0, cached.activeWorkCount)
        assertEquals(3, cached.completedWorkCount)
        assertEquals("2026-11-03T07:30:00Z", cached.latestCancellationAtUtc)
    }

    @Test
    fun cancellationCardTimeUsesDesiredSaveOrTheFrozenSaveBeingReverted() {
        val attempted = "2026-11-03T07:00:00Z"
        val desired = "2026-11-03T07:30:00Z"
        val row = FlightCancellationPendingEntity("owner", "flight", "104", "staff", "change", "save",
            json.encodeToString(SaveFlightCancellationRequest(desired)), null, "version", desired,
            attemptOperation = "save", attemptInputJson = json.encodeToString(SaveFlightCancellationRequest(attempted)))

        assertEquals(desired, row.cancellationAtUtcForCard())
        assertEquals(desired, row.copy(status = "failed").cancellationAtUtcForCard())
        assertEquals(attempted, row.copy(operation = "revert", inputJson = null).cancellationAtUtcForCard())
        assertNull(row.copy(operation = "revert", inputJson = null, attemptOperation = "revert",
            submissionId = "confirmed").cancellationAtUtcForCard())
    }

    @Test
    fun cancellationCardTimeDoesNotInventValuesForMalformedLocalInput() {
        val row = FlightCancellationPendingEntity("owner", "flight", "104", "staff", "change", "save",
            "broken", null, "version", "2026-11-03T07:30:00Z")

        assertNull(row.cancellationAtUtcForCard())
        assertNull(row.copy(inputJson = json.encodeToString(SaveFlightCancellationRequest("invalid-time"))).cancellationAtUtcForCard())
        assertNull(row.copy(operation = "revert", inputJson = null).cancellationAtUtcForCard())
    }

    @Test
    fun basicDetailsReadPlanningAndAssignedStaffWithoutOperationalWorkFields() {
        val detail = json.decodeFromString<FlightDetail>(
            """
            {
              "id": "flight-1", "flightNumber": "104", "customerId": "customer-1",
              "customerIataCode": "SV", "customerName": "Customer", "stationId": "station-1",
              "stationIata": "RUH", "stationTimeZoneId": "Asia/Riyadh", "stationName": "Riyadh", "operationTypeId": "operation-1",
              "operationTypeName": "Transit", "aircraftTypeId": null, "aircraftTypeModel": null,
              "scheduledArrivalUtc": "2026-11-03T08:00:00Z",
              "scheduledDepartureUtc": "2026-11-03T10:00:00Z", "isPerLanding": false,
              "plannedServices": [{"serviceId": "service-1", "name": "Planned service", "isAircraftPerLanding": false}],
              "assignedEmployees": [{"staffMemberId": "staff-1", "fullName": "Assigned staff", "employeeId": "E001"}],
              "createdAtUtc": "2026-11-02T12:00:00Z", "updatedAtUtc": null, "rowVersion": "AAAAAAAAAAE="
            }
            """.trimIndent(),
        )

        assertEquals("RUH", detail.stationIata)
        assertNull(detail.aircraftTypeId)
        assertNull(detail.updatedAtUtc)
        assertEquals("service-1", detail.plannedServices.single().serviceId)
        assertEquals("E001", detail.assignedEmployees.single().employeeId)
        assertFalse(detail.isPerLanding)
    }

    @Test
    fun emptyRegisterRetainsServerPagingMetadata() {
        val page = json.decodeFromString<FlightPage>(
            """{"items":[],"page":1,"pageSize":20,"totalCount":0,"totalPages":0,"hasPreviousPage":false,"hasNextPage":false}""",
        )

        assertTrue(page.items.isEmpty())
        assertEquals(20, page.pageSize)
        assertFalse(page.hasNextPage)
        assertFalse(page.hasPreviousPage)
    }

    @Test
    fun invitationOptionsAndSelectionUseTheSharedStaffContract() {
        val options = json.decodeFromString<List<AssignedEmployee>>(
            """[{"staffMemberId":"staff-2","fullName":"Station colleague","employeeId":"E002"}]""",
        )
        val request = InviteEmployeesRequest(options.map { it.staffMemberId })

        assertEquals("E002", options.single().employeeId)
        assertEquals("""{"staffMemberIds":["staff-2"]}""", json.encodeToString(request))
    }
}
