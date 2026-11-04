package com.nags.operations.data.sync

import com.nags.operations.data.SharedCatalog
import java.io.IOException
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withTimeout
import kotlinx.coroutines.yield
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class CatalogSyncSchedulerTest {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Unconfined)

    @After
    fun tearDown() = scope.cancel()

    @Test
    fun reconnectAndRefreshedCredentialsReloadCatalogsWithoutOfflineRequests() = runBlocking {
        val tokens = MutableStateFlow<String?>("first-token")
        val online = MutableStateFlow(false)
        val batches = mutableListOf<Set<SharedCatalog>>()
        val scheduler = CatalogSyncScheduler(tokens, online, { batches += it }, {}, scope)
        scheduler.start()
        settle()
        assertEquals(0, batches.size)
        online.value = true
        settle()
        assertEquals(1, batches.size)
        online.value = false
        settle()
        scheduler.refreshNow()
        settle()
        assertEquals(1, batches.size)
        online.value = true
        settle()
        assertEquals(2, batches.size)
        tokens.value = "refreshed-token"
        settle()
        assertEquals(3, batches.size)
        assertTrue(batches.all { it == allCatalogs })
        scheduler.stop()
    }

    @Test
    fun manualHintBurstsKeepOnlyOnePendingFullRefresh() = runBlocking {
        val pending = CompletableDeferred<Unit>()
        var refreshes = 0
        val scheduler = CatalogSyncScheduler(
            MutableStateFlow("token"), MutableStateFlow(true),
            { if (++refreshes == 1) pending.await() }, {}, scope,
        )
        scheduler.start()
        settle()
        repeat(20) { scheduler.refreshNow() }
        settle()
        assertEquals(1, refreshes)
        pending.complete(Unit)
        settle()
        assertEquals(2, refreshes)
        scheduler.stop()
        scheduler.refreshNow()
        settle()
        assertEquals(2, refreshes)
    }

    @Test
    fun aLiveHintRefreshesOnlyTheAffectedCatalog() = runBlocking {
        val batches = mutableListOf<Set<SharedCatalog>>()
        val scheduler = CatalogSyncScheduler(
            MutableStateFlow("token"), MutableStateFlow(true), { batches += it }, {}, scope,
        )
        scheduler.start()
        settle()
        scheduler.refreshCatalog(SharedCatalog.Tools)
        settle()
        assertEquals(listOf(allCatalogs, setOf(SharedCatalog.Tools)), batches)
        scheduler.stop()
    }

    @Test
    fun liveHintBurstsRetainEveryDistinctPendingCatalog() = runBlocking {
        val finishInitialRefresh = CompletableDeferred<Unit>()
        val batches = mutableListOf<Set<SharedCatalog>>()
        val scheduler = CatalogSyncScheduler(
            MutableStateFlow("token"), MutableStateFlow(true), {
                batches += it
                if (batches.size == 1) finishInitialRefresh.await()
            }, {}, scope,
        )
        scheduler.start()
        settle()
        repeat(20) {
            scheduler.refreshCatalog(SharedCatalog.Tools)
            scheduler.refreshCatalog(SharedCatalog.Customers)
            scheduler.refreshCatalog(SharedCatalog.AircraftTypes)
        }
        settle()
        assertEquals(listOf(allCatalogs), batches)
        finishInitialRefresh.complete(Unit)
        settle()
        assertEquals(listOf(allCatalogs, setOf(SharedCatalog.Tools, SharedCatalog.Customers, SharedCatalog.AircraftTypes)), batches)
        scheduler.stop()
    }

    @Test
    fun pendingFullRefreshCannotBeDowngradedByLaterLiveHints() = runBlocking {
        val finishInitialRefresh = CompletableDeferred<Unit>()
        val batches = mutableListOf<Set<SharedCatalog>>()
        val scheduler = CatalogSyncScheduler(
            MutableStateFlow("token"), MutableStateFlow(true), {
                batches += it
                if (batches.size == 1) finishInitialRefresh.await()
            }, {}, scope,
        )
        scheduler.start()
        settle()
        scheduler.refreshCatalog(SharedCatalog.Tools)
        scheduler.refreshNow()
        scheduler.refreshCatalog(SharedCatalog.Customers)
        settle()
        finishInitialRefresh.complete(Unit)
        settle()
        assertEquals(listOf(allCatalogs, allCatalogs), batches)
        scheduler.stop()
    }

    @Test
    fun hintsReceivedDuringTargetedFetchRunAsTheNextBatch() = runBlocking {
        val finishToolsRefresh = CompletableDeferred<Unit>()
        val batches = mutableListOf<Set<SharedCatalog>>()
        val scheduler = CatalogSyncScheduler(
            MutableStateFlow("token"), MutableStateFlow(true), {
                batches += it
                if (it == setOf(SharedCatalog.Tools)) finishToolsRefresh.await()
            }, {}, scope,
        )
        scheduler.start()
        settle()
        scheduler.refreshCatalog(SharedCatalog.Tools)
        settle()
        scheduler.refreshCatalog(SharedCatalog.Services)
        scheduler.refreshCatalog(SharedCatalog.Materials)
        settle()
        assertEquals(listOf(allCatalogs, setOf(SharedCatalog.Tools)), batches)
        finishToolsRefresh.complete(Unit)
        settle()
        assertEquals(listOf(allCatalogs, setOf(SharedCatalog.Tools), setOf(SharedCatalog.Services, SharedCatalog.Materials)), batches)
        scheduler.stop()
    }

    @Test
    fun signingInAndPeriodicChecksRefreshEveryCatalog() = runBlocking {
        val tokens = MutableStateFlow<String?>(null)
        val periodicRefresh = CompletableDeferred<Set<SharedCatalog>>()
        val batches = mutableListOf<Set<SharedCatalog>>()
        val scheduler = CatalogSyncScheduler(tokens, MutableStateFlow(true), {
            batches += it
            if (batches.size == 2) periodicRefresh.complete(it)
        }, {}, scope, pollIntervalMs = 20L)
        scheduler.start()
        settle()
        assertTrue(batches.isEmpty())
        tokens.value = "token"
        settle()
        assertEquals(allCatalogs, batches.first())
        assertEquals(allCatalogs, withTimeout(2_000L) { periodicRefresh.await() })
        assertTrue(batches.all { it == allCatalogs })
        scheduler.stop()
    }

    @Test
    fun flightHintsDoNotRefreshUnrelatedCatalogsAndCatalogHintsDoNotRefreshFlights() = runBlocking {
        val batches = mutableListOf<Set<SharedCatalog>>()
        var flightRefreshes = 0
        val scheduler = CatalogSyncScheduler(MutableStateFlow("token"), MutableStateFlow(true),
            { batches += it }, {}, scope, refreshFlightCache = { flightRefreshes++ })
        scheduler.start()
        settle()
        assertEquals(listOf(allCatalogs), batches)
        assertEquals(1, flightRefreshes)
        scheduler.refreshFlights()
        settle()
        assertEquals(listOf(allCatalogs), batches)
        assertEquals(2, flightRefreshes)
        scheduler.refreshCatalog(SharedCatalog.Tools)
        settle()
        assertEquals(listOf(allCatalogs, setOf(SharedCatalog.Tools)), batches)
        assertEquals(2, flightRefreshes)
        scheduler.stop()
        scheduler.refreshFlights()
        settle()
        assertEquals(2, flightRefreshes)
    }

    @Test
    fun pendingFullRefreshRetainsBothFlightsAndCatalogsDuringFlightWork() = runBlocking {
        val finishInitialFlightRefresh = CompletableDeferred<Unit>()
        val batches = mutableListOf<Set<SharedCatalog>>()
        var flightRefreshes = 0
        val scheduler = CatalogSyncScheduler(MutableStateFlow("token"), MutableStateFlow(true),
            { batches += it }, {}, scope, refreshFlightCache = {
                flightRefreshes++
                if (flightRefreshes == 1) finishInitialFlightRefresh.await()
            })
        scheduler.start()
        settle()
        scheduler.refreshFlights()
        scheduler.refreshCatalog(SharedCatalog.Tools)
        scheduler.refreshNow()
        scheduler.refreshCatalog(SharedCatalog.Customers)
        settle()
        assertEquals(1, flightRefreshes)
        finishInitialFlightRefresh.complete(Unit)
        settle()
        assertEquals(listOf(allCatalogs, allCatalogs), batches)
        assertEquals(2, flightRefreshes)
        scheduler.stop()
    }

    @Test
    fun catalogFailureDoesNotSkipThePendingFlightRefresh() = runBlocking {
        var flightRefreshes = 0
        val scheduler = CatalogSyncScheduler(MutableStateFlow("token"), MutableStateFlow(true),
            { throw IOException("Catalog unavailable") }, {}, scope,
            refreshFlightCache = { flightRefreshes++ })
        scheduler.start()
        settle()
        assertEquals(1, flightRefreshes)
        scheduler.refreshFlights()
        settle()
        assertEquals(2, flightRefreshes)
        scheduler.stop()
    }

    @Test
    fun losingCredentialsCancelsTheOldRequestBeforeClearingTheCache() = runBlocking {
        val tokens = MutableStateFlow<String?>("token")
        val started = CompletableDeferred<Unit>()
        val ordering = mutableListOf<String>()
        val scheduler = CatalogSyncScheduler(tokens, MutableStateFlow(true), {
            started.complete(Unit)
            try {
                CompletableDeferred<Unit>().await()
            } finally {
                ordering += "request-stopped"
            }
        }, { ordering += "cache-cleared" }, scope)
        scheduler.start()
        started.await()
        tokens.value = null
        settle()
        assertEquals(listOf("request-stopped", "cache-cleared"), ordering)
        scheduler.stop()
    }

    @Test
    fun anHttpRequestCanPublishItsRefreshedTokenAndFinishBeforeTheQueuedRecheck() = runBlocking {
        val tokens = MutableStateFlow<String?>("first-token")
        val finishHttpRequest = CompletableDeferred<Unit>()
        val ordering = mutableListOf<String>()
        var refreshes = 0
        val scheduler = CatalogSyncScheduler(tokens, MutableStateFlow(true), {
            refreshes++
            if (refreshes == 1) {
                tokens.value = "refreshed-token"
                ordering += "token-published"
                finishHttpRequest.await()
                ordering += "http-request-finished"
            } else {
                ordering += "catalogs-rechecked"
            }
        }, {}, scope)
        scheduler.start()
        settle()
        assertEquals(listOf("token-published"), ordering)
        finishHttpRequest.complete(Unit)
        settle()
        assertEquals(listOf("token-published", "http-request-finished", "catalogs-rechecked"), ordering)
        assertEquals(2, refreshes)
        scheduler.stop()
    }

    @Test
    fun rapidReloginWaitsForTheUnauthenticatedCachePurgeToFinish() = runBlocking {
        val tokens = MutableStateFlow<String?>("first-token")
        val purgeStarted = CompletableDeferred<Unit>()
        val finishPurge = CompletableDeferred<Unit>()
        val ordering = mutableListOf<String>()
        val scheduler = CatalogSyncScheduler(tokens, MutableStateFlow(true), {
            ordering += "refresh"
        }, {
            purgeStarted.complete(Unit)
            finishPurge.await()
            ordering += "purged"
        }, scope)
        scheduler.start()
        settle()
        assertEquals(listOf("refresh"), ordering)
        tokens.value = null
        purgeStarted.await()
        tokens.value = "new-token"
        settle()
        assertEquals(listOf("refresh"), ordering)
        finishPurge.complete(Unit)
        settle()
        assertEquals(listOf("refresh", "purged", "refresh"), ordering)
        scheduler.stop()
    }

    @Test
    fun stopAwaitsInflightCancellationAndFailedRefreshCanBeRetried() = runBlocking {
        var refreshes = 0
        var stopped = false
        val scheduler = CatalogSyncScheduler(
            MutableStateFlow("token"), MutableStateFlow(true), {
                refreshes++
                if (refreshes == 1) throw IOException("offline")
                try {
                    CompletableDeferred<Unit>().await()
                } finally {
                    stopped = true
                }
            }, {}, scope,
        )
        scheduler.start()
        settle()
        scheduler.refreshNow()
        settle()
        assertEquals(2, refreshes)
        scheduler.stop()
        assertTrue(stopped)
    }

    private suspend fun settle() {
        repeat(10) { yield() }
    }

    private val allCatalogs = SharedCatalog.entries.toSet()
}
