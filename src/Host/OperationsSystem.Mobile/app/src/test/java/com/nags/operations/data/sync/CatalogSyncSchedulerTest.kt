package com.nags.operations.data.sync

import java.io.IOException
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.runBlocking
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
        var refreshes = 0
        val scheduler = CatalogSyncScheduler(tokens, online, { refreshes++ }, {}, scope)
        scheduler.start()
        settle()
        assertEquals(0, refreshes)
        online.value = true
        settle()
        assertEquals(1, refreshes)
        online.value = false
        settle()
        scheduler.refreshNow()
        settle()
        assertEquals(1, refreshes)
        online.value = true
        settle()
        assertEquals(2, refreshes)
        tokens.value = "refreshed-token"
        settle()
        assertEquals(3, refreshes)
        scheduler.stop()
    }

    @Test
    fun liveAndManualHintBurstsKeepOnlyOnePendingRefresh() = runBlocking {
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
}
