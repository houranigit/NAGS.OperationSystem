package com.nags.operations.data

import com.nags.operations.data.db.entities.WorkOrderOutboxEntity
import com.nags.operations.data.outbox.selectEligiblePending
import com.nags.operations.data.realtime.MobileSyncChangeDto
import com.nags.operations.data.realtime.MobileSyncOps
import com.nags.operations.data.realtime.MobileSyncTables
import com.nags.operations.data.realtime.coalesceCatalogRefreshes
import com.nags.operations.data.realtime.oldestCompleteCursor
import com.nags.operations.data.sync.MobileSyncVersionTracker
import java.time.Instant
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class SyncReliabilityTest {
    @Test
    fun deferredOldestRowDoesNotStarveLaterEligibleRow() {
        val first = queued("first", createdAt = 1)
        val second = queued("second", createdAt = 2)

        val selected = selectEligiblePending(
            pending = listOf(first, second),
            retryAfterEpochMs = mapOf(first.clientMutationId to 2_000L),
            nowEpochMs = 1_000L,
        )

        assertEquals(second.clientMutationId, selected?.clientMutationId)
        assertNull(
            selectEligiblePending(
                pending = listOf(first),
                retryAfterEpochMs = mapOf(first.clientMutationId to 2_000L),
                nowEpochMs = 1_000L,
            ),
        )
    }

    @Test
    fun versionTrackerOrdersPerEntityWithoutDroppingEqualTimestampSiblings() {
        val tracker = MobileSyncVersionTracker()
        val version = Instant.parse("2026-07-12T01:00:00Z")
        tracker.recordApplied("flights", "flight-a", version)

        assertTrue(tracker.isStaleOrDuplicate("flights", "flight-a", version))
        assertTrue(
            tracker.isStaleOrDuplicate(
                "flights",
                "flight-a",
                Instant.parse("2026-07-12T00:59:59Z"),
            ),
        )
        assertFalse(tracker.isStaleOrDuplicate("flights", "flight-b", version))
    }

    @Test
    fun tableRefreshSupersedesOlderEntityEnvelope() {
        val tracker = MobileSyncVersionTracker()
        tracker.recordApplied("flights", null, Instant.parse("2026-07-12T01:00:00Z"))

        assertTrue(
            tracker.isStaleOrDuplicate(
                "flights",
                "flight-a",
                Instant.parse("2026-07-12T00:59:59Z"),
            ),
        )
        assertFalse(
            tracker.isStaleOrDuplicate(
                "flights",
                "flight-a",
                Instant.parse("2026-07-12T01:00:01Z"),
            ),
        )
    }

    @Test
    fun catchupUsesOldestCursorOnlyWhenEveryTableHasOne() {
        val old = "2026-07-12T00:59:59Z"
        val recent = "2026-07-12T01:00:00+00:00"

        assertEquals(old, oldestCompleteCursor(mapOf("flights" to recent, "employees" to old)))
        assertNull(oldestCompleteCursor(mapOf("flights" to recent, "employees" to null)))
        assertNull(oldestCompleteCursor(mapOf("flights" to "not-a-date")))
    }

    @Test
    fun catchupCoalescesAggregateCatalogRefreshes() {
        val envelopes = listOf(
            refresh(MobileSyncTables.Flights),
            refresh(MobileSyncTables.Services),
            refresh(MobileSyncTables.Tools),
            refresh(MobileSyncTables.Materials),
            refresh(MobileSyncTables.GeneralSupports),
            refresh(MobileSyncTables.Customers),
            refresh(MobileSyncTables.AircraftTypes),
            refresh(MobileSyncTables.Employees),
        )

        val coalesced = coalesceCatalogRefreshes(envelopes)

        assertEquals(3, coalesced.size)
        assertEquals(1, coalesced.count { it.table in MobileSyncTables.CatalogTables })
        assertTrue(coalesced.any { it.table == MobileSyncTables.Flights })
        assertTrue(coalesced.any { it.table == MobileSyncTables.Employees })
    }

    private fun refresh(table: String) = MobileSyncChangeDto(
        table = table,
        op = MobileSyncOps.Refresh,
        version = "2026-07-12T01:00:00Z",
    )

    private fun queued(id: String, createdAt: Long) = WorkOrderOutboxEntity(
        clientMutationId = id,
        flightId = "flight-$id",
        flightKind = WorkOrderOutboxEntity.FLIGHT_KIND_MY,
        clientFlightId = null,
        payloadJson = "{}",
        attachmentsDir = null,
        status = WorkOrderOutboxEntity.STATUS_PENDING,
        attempts = 0,
        lastError = null,
        createdAtEpochMs = createdAt,
        updatedAtEpochMs = createdAt,
        serverWorkOrderId = null,
    )
}
