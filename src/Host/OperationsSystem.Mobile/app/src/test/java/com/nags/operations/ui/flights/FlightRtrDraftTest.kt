package com.nags.operations.ui.flights

import com.nags.operations.data.FlightReturnToRampRecord
import java.time.ZoneId
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertTrue
import org.junit.Assert.assertNull
import org.junit.Test

class FlightRtrDraftTest {
    private val zone = ZoneId.of("Asia/Riyadh")

    @Test fun restoringOccurrenceKeepsItsOwnActualsAndOriginalUtcPrecision() {
        val saved = FlightReturnToRampRecord("rtr", 3, "2026-11-15T13:04:12.1234567Z", "staff", "Employee",
            arrivalRemarks = "Back at stand", departedAtUtc = "2026-11-15T14:35:22Z", departureRemarks = "Ready",
            createdByUserId = "user", createdAtUtc = "2026-11-15T13:05:00Z")
        val draft = saved.rtrDraft(FlightOperationsCatalogs(), zone)
        assertEquals("2026-11-15T16:04:12.123456700", draft.arrival.start)
        assertEquals(java.time.OffsetDateTime.parse(saved.arrivedAtUtc).toInstant(), java.time.OffsetDateTime.parse(draft.input(zone).arrival.arrivedAtUtc).toInstant())
        assertEquals(saved.departedAtUtc, draft.input(zone).departure?.departedAtUtc)
        assertEquals(3, draft.sequence)
        assertEquals("Ready", draft.input(zone).departure?.remarks)
    }

    @Test fun loadingMediaPreviewDoesNotInventAnEditButRemovingItDoes() {
        val photo = FlightAttachmentDraft("photo", "", "photo.png", "image/png", uploadedAtUtc = "2026-11-15T12:00:00Z")
        val draft = FlightRtrDraft(sequence = 1, arrival = FlightWorkPeriod(start = "2026-11-15T16:00"),
            work = FlightWorkDrafts(services = listOf(FlightServiceDraft(id = "service", attachments = listOf(photo)))))
        val hydrated = draft.copy(work = draft.work.copy(services = draft.work.services.map {
            it.copy(attachments = listOf(photo.copy(uri = "content://private/preview")))
        }))
        assertEquals(draft.fingerprint(), hydrated.fingerprint())
        assertNotEquals(draft.fingerprint(), hydrated.copy(work = FlightWorkDrafts()).fingerprint())
    }

    @Test fun reviewRestoresExplicitSignatureRemovalWithoutReintroducingPublishedInk() {
        val saved = FlightReturnToRampRecord("rtr", 1, "2026-11-15T13:00:00Z", "staff", "Employee",
            departedAtUtc = "2026-11-15T14:00:00Z", hasSignature = true, signatureSignedAtUtc = "2026-11-15T14:00:00Z",
            createdByUserId = "user", createdAtUtc = "2026-11-15T13:00:00Z")
        val input = saved.input().let { it.copy(departure = it.departure?.copy(removeSignature = true)) }
        val restored = saved.rtrDraft(FlightOperationsCatalogs(), zone, input)
        assertTrue(restored.removeSignature)
        assertTrue(restored.input(zone).departure!!.removeSignature)
        assertNull(restored.input(zone).departure!!.signatureContent)
    }
}
