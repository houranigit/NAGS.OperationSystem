package com.nags.operations.ui.sync

import com.nags.operations.data.CatalogSyncStatus
import com.nags.operations.data.FlightSyncStatus
import com.nags.operations.data.sync.SyncTarget
import com.nags.operations.data.SharedCatalog
import com.nags.operations.data.realtime.RealtimeChannelState
import java.time.Instant
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.launch
import org.junit.After
import org.junit.Assert.*
import org.junit.Test

class SyncCenterViewModelTest {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Unconfined)
    private val now = Instant.parse("2026-11-03T20:00:00Z").toEpochMilli()

    @After
    fun tearDown() = scope.cancel()

    @Test
    fun cardsKeepThePreviousCatalogOrderAndShowEmptyCatalogsBeforeFirstSync() {
        val rows = catalogSyncRows(emptyList(), now)
        assertEquals(listOf(SharedCatalog.AtaChapters, SharedCatalog.Services, SharedCatalog.Tools,
            SharedCatalog.Materials, SharedCatalog.GeneralSupports, SharedCatalog.Customers,
            SharedCatalog.AircraftTypes, SharedCatalog.StationStaff).map { SyncTarget.Catalog(it) }, rows.map { it.target })
        assertTrue(rows.all { it.rowCount == 0 && it.lastSyncedAt == null && it.lastDurationMs == null })
    }

    @Test
    fun refreshFailureKeepsTheActualSuccessTimestampAndDurationVisible() {
        val success = "2026-11-03T19:58:00Z"
        val status = CatalogSyncStatus(SharedCatalog.AircraftTypes, itemCount = 12,
            lastAttemptUtc = "2026-11-03T19:59:00Z", lastSuccessUtc = success,
            error = "refresh failed", lastDurationMs = 74L)
        val row = catalogSyncRows(listOf(status), now).first { it.target == SyncTarget.Catalog(SharedCatalog.AircraftTypes) }
        assertEquals(12, row.rowCount)
        assertEquals(Instant.parse(success).toEpochMilli(), row.lastSyncedAt)
        assertEquals(120_000L, row.ageMs)
        assertEquals(74L, row.lastDurationMs)
        assertNotNull(row.lastError)
        assertFalse(row.permissionDenied)
    }

    @Test
    fun invalidOrFutureMetadataDoesNotCrashOrShowNegativeAges() {
        val rows = catalogSyncRows(listOf(
            CatalogSyncStatus(SharedCatalog.Customers, lastSuccessUtc = "invalid"),
            CatalogSyncStatus(SharedCatalog.Services, lastSuccessUtc = "2026-11-03T20:01:00Z"),
            CatalogSyncStatus(SharedCatalog.Tools, permissionDenied = true),
        ), now)
        assertNull(rows.first { it.target == SyncTarget.Catalog(SharedCatalog.Customers) }.lastSyncedAt)
        assertEquals(0L, rows.first { it.target == SyncTarget.Catalog(SharedCatalog.Services) }.ageMs)
        assertTrue(rows.first { it.target == SyncTarget.Catalog(SharedCatalog.Tools) }.permissionDenied)
    }

    @Test
    fun flightCardUsesItsOwnCountFreshnessAndAccessState() {
        val synced = "2026-11-03T19:59:00Z"
        val rows = mobileSyncRows(listOf(CatalogSyncStatus(SharedCatalog.Tools, itemCount = 9)),
            FlightSyncStatus(itemCount = 31, lastSuccessUtc = synced, lastDurationMs = 84L), now)
        val flight = rows.first()
        assertEquals(SyncTarget.Flights, flight.target)
        assertEquals(31, flight.rowCount)
        assertEquals(60_000L, flight.ageMs)
        assertEquals(84L, flight.lastDurationMs)
        assertEquals(9, rows.first { it.target == SyncTarget.Catalog(SharedCatalog.Tools) }.rowCount)
        val denied = mobileSyncRows(emptyList(), FlightSyncStatus(permissionDenied = true, error = "Denied"), now).first()
        assertTrue(denied.permissionDenied)
        assertNull(denied.lastSyncedAt)
        assertEquals(0, denied.rowCount)
    }

    @Test
    fun oldCompletedCacheCannotClaimSearchPreparedBeforeItsFirstIndexSnapshot() {
        val success = "2026-11-03T19:59:00Z"
        val rows = mobileSyncRows(SharedCatalog.entries.map { CatalogSyncStatus(it, lastSuccessUtc = success) },
            FlightSyncStatus(lastSuccessUtc = success), now)
        assertFalse(offlineDataPrepared(rows, FlightSyncStatus()))
        assertTrue(offlineDataPrepared(rows, FlightSyncStatus(lastSuccessUtc = success)))
    }

    @Test
    fun completedSearchMembershipDoesNotHideAFailedFullFlightMediaRefresh() {
        val success = "2026-11-03T19:59:00Z"
        val rows = mobileSyncRows(SharedCatalog.entries.map { CatalogSyncStatus(it, lastSuccessUtc = success) },
            FlightSyncStatus(lastSuccessUtc = success, error = "media download failed"), now)
        assertFalse(offlineDataPrepared(rows, FlightSyncStatus(lastSuccessUtc = success)))
    }

    @Test
    fun failedOrDeniedSearchRefreshCannotReuseAnOldSnapshotToClaimPrepared() {
        val success = "2026-11-03T19:59:00Z"
        val rows = mobileSyncRows(SharedCatalog.entries.map { CatalogSyncStatus(it, lastSuccessUtc = success) },
            FlightSyncStatus(lastSuccessUtc = success), now)
        assertFalse(offlineDataPrepared(rows, FlightSyncStatus(lastSuccessUtc = success, error = "refresh failed")))
        assertFalse(offlineDataPrepared(rows, FlightSyncStatus(lastSuccessUtc = success, permissionDenied = true)))
    }

    @Test
    fun restoredRefreshControlsRespectOfflineAndBusyStatesWhileMetadataRemainsVisible() {
        val statuses = MutableStateFlow(listOf(CatalogSyncStatus(SharedCatalog.Customers, itemCount = 3)))
        val online = MutableStateFlow(false)
        val syncing = MutableStateFlow(false)
        var refreshes = 0
        val model = SyncCenterViewModel(statuses, syncing, online,
            MutableStateFlow(RealtimeChannelState.Disconnected), MutableStateFlow(null),
            { refreshes++ }, scope, { now })
        val observer = scope.launch { model.rows.collect { } }
        assertEquals(3, model.rows.value.first { it.target == SyncTarget.Catalog(SharedCatalog.Customers) }.rowCount)
        model.refreshNow()
        assertEquals(0, refreshes)
        online.value = true
        syncing.value = true
        model.refreshNow()
        assertEquals(0, refreshes)
        syncing.value = false
        model.refreshNow()
        assertEquals(1, refreshes)
        statuses.value = listOf(CatalogSyncStatus(SharedCatalog.Customers, itemCount = 4,
            lastSuccessUtc = "2026-11-03T20:00:00Z", lastDurationMs = 28L))
        val updated = model.rows.value.first { it.target == SyncTarget.Catalog(SharedCatalog.Customers) }
        assertEquals(4, updated.rowCount)
        assertEquals(28L, updated.lastDurationMs)
        observer.cancel()
    }
}
