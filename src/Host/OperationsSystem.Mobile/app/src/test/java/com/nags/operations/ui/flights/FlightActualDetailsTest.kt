package com.nags.operations.ui.flights

import com.nags.operations.data.*
import com.nags.operations.data.db.entities.FlightArrivalPendingEntity
import com.nags.operations.data.db.entities.FlightDeparturePendingEntity
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import org.junit.Assert.*
import org.junit.Test

class FlightActualDetailsTest {
    private val staffId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
    private val actor = AssignedEmployee(staffId, "Current operator", "E1")
    private val other = AssignedEmployee("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", "Recorded employee", "E2")
    private val arrival = FlightArrivalSubmission("arrival", other, "2026-11-10T10:00:00Z", "old-type", "A320", "OLD",
        receivedByEmployee = other, isAccepted = true, createdAtUtc = "2026-11-10T10:00:00Z")
    private val departure = FlightDepartureSubmission("departure", other, "2026-11-10T11:00:00Z", other,
        isAccepted = true, createdAtUtc = "2026-11-10T11:00:00Z")
    private val flight = FlightDetail("flight", "TEST", "customer", customerName = "Airline", stationId = "station",
        stationIata = "JED", stationName = "Jeddah", operationTypeId = "operation", operationTypeName = "Transit",
        aircraftTypeId = "planned-type", aircraftTypeModel = "Planned model",
        scheduledArrivalUtc = "2026-11-10T09:00:00Z", scheduledDepartureUtc = "2026-11-10T12:00:00Z",
        isPerLanding = false, createdAtUtc = "2026-11-10T09:00:00Z", rowVersion = "reviewed-version",
        arrivalSubmissions = listOf(arrival), departureSubmissions = listOf(departure), currentStaffMemberId = staffId,
        stationTimeZoneId = "Asia/Riyadh")
    private val localArrival = SaveFlightArrivalRequest("2026-11-10T10:30:00Z", "new-type", "NEW")
    private val localDeparture = SaveFlightDepartureRequest("2026-11-10T11:30:00Z")
    private val pendingArrival = FlightArrivalPendingEntity("owner", flight.id, flight.flightNumber, "arrival-change",
        Json.encodeToString(localArrival), flight.rowVersion, "2026-11-10T11:30:00Z",
        attemptInputJson = Json.encodeToString(localArrival.copy(aircraftTailNumber = "OLDER ATTEMPT")))
    private val pendingDeparture = FlightDeparturePendingEntity("owner", flight.id, flight.flightNumber, staffId,
        "departure-change", Json.encodeToString(localDeparture), flight.rowVersion, "[]", "2026-11-10T11:30:00Z",
        attemptInputJson = Json.encodeToString(localDeparture.copy(atd = "2026-11-10T11:15:00Z")))
    private val aircraft = listOf(AircraftTypeCatalogItem("new-type", "Boeing", "B737"))
    private val staff = listOf(StaffCatalogItem(staffId, actor.fullName, actor.employeeId, flight.stationId, "JED", "Staff"))

    @Test fun persistedDesiredValuesAppearForEveryRetainedStatusWithoutChangingServerSubmissions() {
        for (status in listOf("pending", "conflict", "failed")) {
            val actuals = flight.actualDetails(pendingArrival.copy(status = status), pendingDeparture.copy(status = status),
                aircraft, staff, youLabel = "You")!!
            assertEquals(localArrival.ata, actuals.ata)
            assertEquals(localDeparture.atd, actuals.atd)
            assertEquals("B737", actuals.aircraftModel)
            assertEquals("NEW", actuals.tailNumber)
            assertEquals(listOf(actor.fullName), actuals.employeeNames)
            assertTrue(actuals.savedLocally)
        }
        assertEquals(listOf(arrival), flight.arrivalSubmissions)
        assertEquals(listOf(departure), flight.departureSubmissions)
        assertEquals("reviewed-version", flight.rowVersion)
    }

    @Test fun eitherLocalSectionOverridesOnlyItsOwnServerValuesAndPreservesDistinctStaff() {
        val arrived = flight.actualDetails(pendingArrival = pendingArrival, aircraftTypes = aircraft, stationStaff = staff, youLabel = "You")!!
        assertEquals(localArrival.ata, arrived.ata)
        assertEquals(departure.atd, arrived.atd)
        assertEquals(listOf(actor.fullName, other.fullName), arrived.employeeNames)
        val departed = flight.actualDetails(pendingDeparture = pendingDeparture, stationStaff = staff, youLabel = "You")!!
        assertEquals(arrival.ata, departed.ata)
        assertEquals(localDeparture.atd, departed.atd)
        assertEquals(arrival.aircraftTypeModel, departed.aircraftModel)
        assertEquals(listOf(other.fullName, actor.fullName), departed.employeeNames)
    }

    @Test fun localFirstSaveAppearsWithoutAnyServerSubmission() {
        val empty = flight.copy(arrivalSubmissions = emptyList(), departureSubmissions = emptyList())
        assertNull(empty.actualDetails(youLabel = "You"))
        assertEquals(localArrival.ata, empty.actualDetails(pendingArrival = pendingArrival, youLabel = "You")!!.ata)
        val actuals = empty.actualDetails(pendingDeparture = pendingDeparture, currentUserName = "Cached name", youLabel = "You")!!
        assertEquals(localDeparture.atd, actuals.atd)
        assertEquals(listOf("Cached name"), actuals.employeeNames)
        assertNull(actuals.ata)
    }

    @Test fun discardAndServerAcknowledgmentReplaceTheOverlay() {
        assertTrue(flight.actualDetails(pendingArrival, pendingDeparture, youLabel = "You")!!.savedLocally)
        val discarded = flight.actualDetails(youLabel = "You")!!
        assertFalse(discarded.savedLocally)
        assertEquals(arrival.ata, discarded.ata)
        assertEquals(departure.atd, discarded.atd)
        val acknowledged = flight.copy(arrivalSubmissions = listOf(arrival.copy(ata = localArrival.ata, aircraftTailNumber = "NEW")),
            departureSubmissions = listOf(departure.copy(atd = localDeparture.atd)))
            .actualDetails(youLabel = "You")!!
        assertFalse(acknowledged.savedLocally)
        assertEquals(localArrival.ata, acknowledged.ata)
        assertEquals(localDeparture.atd, acknowledged.atd)
    }

    @Test fun unrelatedRowsAndInvalidPayloadsNeverReplaceTheSelectedFlight() {
        val server = flight.actualDetails(youLabel = "You")
        assertEquals(server, flight.actualDetails(pendingArrival.copy(flightId = "different"), pendingDeparture.copy(flightId = "different"), youLabel = "You"))
        assertEquals(server, flight.actualDetails(pendingDeparture = pendingDeparture.copy(staffMemberId = other.staffMemberId), youLabel = "You"))
        assertEquals(server, flight.actualDetails(pendingArrival.copy(inputJson = "invalid"),
            pendingDeparture.copy(inputJson = Json.encodeToString(localDeparture.copy(atd = "invalid"))), youLabel = "You"))
        assertEquals(server, flight.copy(currentStaffMemberId = null).actualDetails(pendingArrival, pendingDeparture, youLabel = "You"))
    }

    @Test fun missingAircraftLookupNeverBorrowsADifferentActualOrPlannedModel() {
        assertNull(flight.actualDetails(pendingArrival = pendingArrival, youLabel = "You")!!.aircraftModel)
        assertEquals("Planned model", flight.copy(aircraftTypeId = localArrival.aircraftTypeId)
            .actualDetails(pendingArrival = pendingArrival, youLabel = "You")!!.aircraftModel)
        assertEquals("A320", flight.copy(arrivalSubmissions = listOf(arrival.copy(aircraftTypeId = localArrival.aircraftTypeId)))
            .actualDetails(pendingArrival = pendingArrival, youLabel = "You")!!.aircraftModel)
    }

    @Test fun cachedStaffIsStationScopedAndRecordedEmployeeNamesStayUnchanged() {
        val wrongStation = staff.map { it.copy(stationId = "different", fullName = "Wrong station") }
        val actuals = flight.actualDetails(pendingDeparture = pendingDeparture, stationStaff = wrongStation,
            currentUserName = "Cached operator", youLabel = "You")!!
        assertEquals(listOf(other.fullName, "Cached operator"), actuals.employeeNames)
        assertEquals(listOf("You"), flight.copy(arrivalSubmissions = emptyList(), departureSubmissions = emptyList())
            .actualDetails(pendingDeparture = pendingDeparture, youLabel = "You")!!.employeeNames)
    }

    @Test fun compatibleExtraJsonFieldsStillShowTheLocallySavedInput() {
        val compatible = pendingArrival.copy(inputJson = pendingArrival.inputJson.dropLast(1) + ",\"futureField\":true}")
        assertEquals(localArrival.ata, flight.actualDetails(pendingArrival = compatible, youLabel = "You")!!.ata)
    }
}
