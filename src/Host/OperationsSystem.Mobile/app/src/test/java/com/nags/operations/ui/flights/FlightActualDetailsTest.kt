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

    @Test fun explicitTargetCorrectionsPreserveOriginalReceiverAndHandlerAcrossCacheRefresh() {
        val targetedArrival = pendingArrival.copy(inputJson = Json.encodeToString(localArrival.copy(targetSubmissionId = arrival.id)),
            reviewedArrivalJson = Json.encodeToString(arrival))
        val targetedDeparture = pendingDeparture.copy(inputJson = Json.encodeToString(localDeparture.copy(targetSubmissionId = departure.id)),
            reviewedDepartureJson = Json.encodeToString(listOf(departure)))
        for (detail in listOf(flight, flight.copy(arrivalSubmissions = emptyList(), departureSubmissions = emptyList()))) {
            val actuals = detail.actualDetails(targetedArrival, targetedDeparture, aircraft, staff, youLabel = "You")!!
            assertEquals(localArrival.ata, actuals.ata)
            assertEquals(localDeparture.atd, actuals.atd)
            assertEquals(listOf(other.fullName), actuals.employeeNames)
            assertEquals("NEW", actuals.tailNumber)
            assertTrue(actuals.savedLocally)
        }
        assertEquals(other, arrival.submittedByEmployee)
        assertEquals(other, departure.submittedByEmployee)
    }

    @Test fun submittedCardsOverlayDesiredValuesAndOptionalClearsWithoutChangingServerMetadata() {
        val originalArrival = arrival.copy(customerWorkReferenceNumber = "OLD REF", remarks = "Old arrival note",
            updatedAtUtc = "2026-11-10T10:05:00Z")
        val originalDeparture = departure.copy(remarks = "Old departure note", signatureVersion = "server-signature-v1",
            updatedAtUtc = "2026-11-10T11:05:00Z")
        val queuedArrival = pendingArrival.copy(inputJson = Json.encodeToString(localArrival.copy(
            customerWorkReferenceNumber = null, remarks = null, targetSubmissionId = arrival.id)))
        val queuedDeparture = pendingDeparture.copy(inputJson = Json.encodeToString(localDeparture.copy(
            remarks = null, targetSubmissionId = departure.id)), signatureFile = "replacement.png")
        assertEquals(originalArrival.copy(ata = localArrival.ata, aircraftTypeId = localArrival.aircraftTypeId,
            aircraftTypeModel = "B737", aircraftTailNumber = "NEW", customerWorkReferenceNumber = null, remarks = null),
            originalArrival.withPendingCorrection(queuedArrival, aircraft))
        assertEquals(originalDeparture.copy(atd = localDeparture.atd, remarks = null),
            originalDeparture.withPendingCorrection(queuedDeparture))
        assertEquals("server-signature-v1", originalDeparture.withPendingCorrection(queuedDeparture).signatureVersion)
    }

    @Test fun submittedCardOverlayIgnoresUntargetedDifferentAndMalformedLocalInput() {
        assertEquals(arrival, arrival.withPendingCorrection(pendingArrival, aircraft))
        assertEquals(departure, departure.withPendingCorrection(pendingDeparture))
        assertEquals(arrival, arrival.withPendingCorrection(pendingArrival.copy(inputJson = Json.encodeToString(
            localArrival.copy(targetSubmissionId = "another-arrival"))), aircraft))
        assertEquals(departure, departure.withPendingCorrection(pendingDeparture.copy(inputJson = "invalid JSON")))
        assertEquals("", arrival.withPendingCorrection(pendingArrival.copy(inputJson = Json.encodeToString(
            localArrival.copy(targetSubmissionId = arrival.id)))).aircraftTypeModel)
    }

    @Test fun formCardsShowOwnUntargetedCorrectionsAndPreserveOriginalAttribution() {
        val ownArrival = arrival.copy(id = "own-arrival", submittedByEmployee = actor, receivedByEmployee = actor,
            createdByDisplayName = "Creator account", updatedByDisplayName = "Latest updater", updatedAtUtc = "2026-11-10T10:10:00Z")
        val ownDeparture = departure.copy(id = "own-departure", submittedByEmployee = actor, handledByEmployee = actor,
            createdByDisplayName = "Creator account", updatedByDisplayName = "Latest updater", updatedAtUtc = "2026-11-10T11:10:00Z",
            signatureVersion = "saved-signature")
        val detail = flight.copy(arrivalSubmissions = listOf(arrival, ownArrival), departureSubmissions = listOf(departure, ownDeparture))
        assertEquals(ownArrival.copy(ata = localArrival.ata, aircraftTypeId = localArrival.aircraftTypeId,
            aircraftTypeModel = "B737", aircraftTailNumber = localArrival.aircraftTailNumber,
            customerWorkReferenceNumber = localArrival.customerWorkReferenceNumber, remarks = localArrival.remarks),
            detail.formArrivalSummary(pendingArrival, aircraft))
        assertEquals(ownDeparture.copy(atd = localDeparture.atd, remarks = localDeparture.remarks),
            detail.formDepartureSummary(pendingDeparture))
        assertEquals(ownArrival, detail.formArrivalSummary(null))
        assertEquals(ownDeparture, detail.formDepartureSummary(null))
        assertEquals("reviewed-version", detail.rowVersion)
        assertEquals(listOf(arrival, ownArrival), detail.arrivalSubmissions)
    }

    @Test fun formCardsSynthesizeQueuedAdditionsWithoutBorrowingForeignServerIdentity() {
        val detail = flight.copy(assignedEmployees = listOf(actor))
        for (status in listOf("pending", "failed", "conflict")) {
            val arrived = detail.formArrivalSummary(pendingArrival.copy(status = status), aircraft)!!
            assertEquals("", arrived.id)
            assertEquals(actor, arrived.submittedByEmployee)
            assertEquals(actor, arrived.receivedByEmployee)
            assertEquals(localArrival.ata, arrived.ata)
            assertEquals("B737", arrived.aircraftTypeModel)
            assertEquals(pendingArrival.savedAtUtc, arrived.createdAtUtc)
            assertFalse(arrived.isAccepted)
            assertNull(arrived.createdByDisplayName)
            assertNull(arrived.updatedAtUtc)
            val departed = detail.formDepartureSummary(pendingDeparture.copy(status = status, signatureFile = "local.png"))!!
            assertEquals("", departed.id)
            assertEquals(actor, departed.submittedByEmployee)
            assertEquals(actor, departed.handledByEmployee)
            assertEquals(localDeparture.atd, departed.atd)
            assertEquals(pendingDeparture.savedAtUtc, departed.createdAtUtc)
            assertFalse(departed.isAccepted)
            assertNull(departed.createdByDisplayName)
            assertNull(departed.signatureVersion)
            assertTrue(departed.hasSignature)
        }
        val empty = detail.copy(arrivalSubmissions = emptyList(), departureSubmissions = emptyList())
        assertNull(empty.formArrivalSummary(null))
        assertNull(empty.formDepartureSummary(null))
        assertEquals("", empty.formArrivalSummary(pendingArrival)!!.id)
        assertFalse(empty.formDepartureSummary(pendingDeparture)!!.hasSignature)
    }

    @Test fun formCardsKeepExplicitTargetsAcrossCacheRefreshAndNeverUseTheVisibleOwnerInstead() {
        val targetedArrival = pendingArrival.copy(inputJson = Json.encodeToString(localArrival.copy(targetSubmissionId = arrival.id)),
            reviewedArrivalJson = Json.encodeToString(arrival))
        val targetedDeparture = pendingDeparture.copy(inputJson = Json.encodeToString(localDeparture.copy(targetSubmissionId = departure.id)),
            reviewedDepartureJson = Json.encodeToString(listOf(departure)))
        val ownArrival = arrival.copy(id = "own-arrival", submittedByEmployee = actor)
        val ownDeparture = departure.copy(id = "own-departure", submittedByEmployee = actor)
        for (detail in listOf(flight.copy(arrivalSubmissions = listOf(ownArrival, arrival), departureSubmissions = listOf(ownDeparture, departure)),
            flight.copy(arrivalSubmissions = listOf(ownArrival), departureSubmissions = listOf(ownDeparture)))) {
            assertEquals(arrival.copy(ata = localArrival.ata, aircraftTypeId = localArrival.aircraftTypeId,
                aircraftTypeModel = "B737", aircraftTailNumber = localArrival.aircraftTailNumber,
                customerWorkReferenceNumber = localArrival.customerWorkReferenceNumber, remarks = localArrival.remarks),
                detail.formArrivalSummary(targetedArrival, aircraft))
            assertEquals(departure.copy(atd = localDeparture.atd, remarks = localDeparture.remarks),
                detail.formDepartureSummary(targetedDeparture))
        }
    }

    @Test fun formCardsRecoverOnlyOwnUntargetedReviewedReports() {
        val ownArrival = arrival.copy(id = "own-arrival", submittedByEmployee = actor)
        val ownDeparture = departure.copy(id = "own-departure", submittedByEmployee = actor)
        val localArrivalRow = pendingArrival.copy(reviewedArrivalJson = Json.encodeToString(ownArrival))
        val localDepartureRow = pendingDeparture.copy(reviewedDepartureJson = Json.encodeToString(listOf(departure, ownDeparture)))
        assertEquals(ownArrival.id, flight.formArrivalSummary(localArrivalRow, aircraft)!!.id)
        assertEquals(ownDeparture.id, flight.formDepartureSummary(localDepartureRow)!!.id)
        assertEquals("", flight.formArrivalSummary(pendingArrival.copy(reviewedArrivalJson = Json.encodeToString(arrival)), aircraft)!!.id)
        assertEquals("", flight.formDepartureSummary(pendingDeparture.copy(reviewedDepartureJson = Json.encodeToString(listOf(departure))))!!.id)
    }

    @Test fun formCardsIgnoreMissingTargetsMalformedPayloadsAndUnrelatedQueues() {
        for (row in listOf(pendingArrival.copy(flightId = "another-flight"), pendingArrival.copy(inputJson = "invalid JSON"),
            pendingArrival.copy(inputJson = Json.encodeToString(localArrival.copy(ata = "not-a-time"))),
            pendingArrival.copy(inputJson = Json.encodeToString(localArrival.copy(targetSubmissionId = "missing")),
                reviewedArrivalJson = Json.encodeToString(arrival)))) {
            assertEquals(arrival, flight.formArrivalSummary(row, aircraft))
        }
        for (row in listOf(pendingDeparture.copy(flightId = "another-flight"), pendingDeparture.copy(staffMemberId = other.staffMemberId),
            pendingDeparture.copy(inputJson = "invalid JSON"), pendingDeparture.copy(inputJson = Json.encodeToString(localDeparture.copy(atd = "not-a-time"))),
            pendingDeparture.copy(inputJson = Json.encodeToString(localDeparture.copy(targetSubmissionId = "missing")),
                reviewedDepartureJson = Json.encodeToString(listOf(departure))))) {
            assertEquals(departure, flight.formDepartureSummary(row))
        }
        val noActor = flight.copy(currentStaffMemberId = null)
        assertEquals(arrival, noActor.formArrivalSummary(pendingArrival, aircraft))
        assertEquals(departure, noActor.formDepartureSummary(pendingDeparture))
    }

    @Test fun formDepartureCardShowsNewLocalSignatureWithoutInventingAServerVersion() {
        val unsigned = departure.copy(submittedByEmployee = actor, hasSignature = false, signatureVersion = "server-version")
        val detail = flight.copy(departureSubmissions = listOf(unsigned))
        val withSignature = detail.formDepartureSummary(pendingDeparture.copy(signatureFile = "queued.png"))!!
        assertTrue(withSignature.hasSignature)
        assertEquals("server-version", withSignature.signatureVersion)
        assertFalse(detail.formDepartureSummary(pendingDeparture)!!.hasSignature)
        val signed = detail.copy(departureSubmissions = listOf(unsigned.copy(hasSignature = true)))
        assertTrue(signed.formDepartureSummary(pendingDeparture)!!.hasSignature)
        assertFalse(unsigned.hasSignature)
    }
}
