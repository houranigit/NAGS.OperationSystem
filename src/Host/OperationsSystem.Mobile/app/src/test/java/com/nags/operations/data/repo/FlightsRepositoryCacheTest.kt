package com.nags.operations.data.repo

import com.nags.operations.data.CustomerLogoDownload

import com.nags.operations.data.ApiException
import com.nags.operations.data.FlightDetail
import com.nags.operations.data.FlightListItem
import com.nags.operations.data.FlightPage
import com.nags.operations.data.db.dao.FlightCacheDao
import com.nags.operations.data.db.entities.FlightDetailCacheEntity
import com.nags.operations.data.db.entities.FlightSnapshotEntity
import java.io.IOException
import java.nio.file.Files
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CoroutineStart
import kotlinx.coroutines.async
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.launch
import kotlinx.coroutines.runBlocking
import org.junit.Assert.*
import org.junit.Test

class FlightsRepositoryCacheTest {
    private val dao = MemoryFlightCacheDao()
    private val owner = MutableStateFlow<String?>("http://branch/|account")
    private var online = true
    private var clock = "2026-11-04T10:00:00Z"
    private var serverItems = listOf(item("one"), item("two"))
    private var listFailure: Exception? = null
    private var detailFailure: Exception? = null
    private val detailReads = mutableListOf<String>()
    private val pageReads = mutableListOf<Pair<Int, Int>>()
    private var detailNumber = "123"
    private var invitationReads = 0
    private var invitationWrites = 0
    private var logoStore: CustomerLogoStore? = null
    private var pageLoader: suspend (Int, Int, String?) -> FlightPage = { page, size, _ ->
        listFailure?.let { throw it }
        pageReads += page to size
        val totalPages = if (serverItems.isEmpty()) 0 else (serverItems.size - 1) / size + 1
        FlightPage(serverItems.drop((page - 1) * size).take(size), page, size,
            serverItems.size.toLong(), totalPages, page > 1, page < totalPages)
    }
    private var detailLoader: suspend (String) -> FlightDetail = { id ->
        detailFailure?.let { throw it }
        detailReads += id
        detail(id, detailNumber)
    }

    private fun repository() = FlightsRepository(
        fetchFlights = { page, size, search -> pageLoader(page, size, search) },
        fetchFlight = { detailLoader(it) },
        fetchInvitationOptions = { invitationReads++; emptyList() },
        sendInvitation = { _, _, _ -> invitationWrites++ },
        logoStore = logoStore,
        dao = dao, ownerFlow = owner, currentOwner = { owner.value }, isOnline = { online }, now = { clock },
    )

    @Test
    fun completePagedListsAreCachedWithServerOrderAndOfflineSearchPaging() = runBlocking {
        serverItems = (1..101).map { item(it.toString(), if (it == 101) "999" else "123") }
        val repository = repository()
        repository.refreshCache()
        assertEquals(listOf(1 to 100, 2 to 100), pageReads)
        assertTrue(detailReads.isEmpty())
        assertEquals(101, repository.syncStatus.first().itemCount)
        assertEquals(clock, repository.syncStatus.first().lastSuccessUtc)
        online = false
        val second = repository.flights(page = 2, pageSize = 100)
        assertTrue(second.fromCache)
        assertEquals(listOf("101"), second.items.map { it.id })
        assertFalse(second.hasNextPage)
        val searched = repository.flights(search = "XY-999")
        assertEquals(listOf("101"), searched.items.map { it.id })
        assertEquals(1L, searched.totalCount)
        assertEquals(listOf("101"), repository.flights(search = "XY 999").items.map { it.id })
    }

    @Test
    fun onlyOpenedDetailsAreRevalidatedAndLostVisibilityPrunesThem() = runBlocking {
        val repository = repository()
        repository.refreshCache()
        repository.flight("one")
        detailNumber = "456"
        repository.refreshCache()
        assertEquals(listOf("one", "one"), detailReads)
        online = false
        val cached = repository.flight("one")
        assertEquals("456", cached.flightNumber)
        assertTrue(cached.fromCache)
        assertTrue(runCatching { repository.flight("two") }.isFailure)
        online = true
        serverItems = listOf(item("two"))
        repository.refreshCache()
        assertEquals(listOf("one", "one"), detailReads)
        online = false
        assertTrue(runCatching { repository.flight("one") }.isFailure)
        assertEquals(listOf("two"), repository.flights().items.map { it.id })
    }

    @Test
    fun incompletePaginationAndTransientReadsRetainTheLastSuccessfulSnapshot() = runBlocking {
        val repository = repository()
        repository.refreshCache()
        repository.flight("one")
        val before = dao.snapshot(owner.value!!)!!
        clock = "2026-11-04T10:01:00Z"
        pageLoader = { page, size, _ ->
            if (page == 2) throw IOException("Disconnected")
            FlightPage(listOf(item("new")), page, size, 2, 2, false, true)
        }
        repository.refreshCache()
        val after = dao.snapshot(owner.value!!)!!
        assertEquals(before.payloadJson, after.payloadJson)
        assertEquals(before.lastSuccessUtc, after.lastSuccessUtc)
        assertEquals(before.lastDurationMs, after.lastDurationMs)
        assertEquals(clock, after.lastAttemptUtc)
        assertNotNull(after.error)
        assertNotNull(dao.detail(owner.value!!, "one"))
        listFailure = IOException("Disconnected")
        pageLoader = { _, _, _ -> throw listFailure!! }
        assertTrue(repository.flights().fromCache)
        detailFailure = ApiException(503, "Unavailable")
        assertTrue(repository.flight("one").fromCache)
    }

    @Test
    fun listPermissionDenialClearsFlightsAndDetailsWithoutOfflineFallback() = runBlocking {
        val repository = repository()
        repository.refreshCache()
        repository.flight("one")
        listFailure = ApiException(403, "Denied")
        val failure = runCatching { repository.flights() }.exceptionOrNull()
        assertTrue(failure is ApiException && failure.statusCode == 403)
        val status = repository.syncStatus.first()
        assertTrue(status.permissionDenied)
        assertEquals(0, status.itemCount)
        assertNull(status.lastSuccessUtc)
        assertTrue(dao.details(owner.value!!).isEmpty())
        online = false
        assertTrue(runCatching { repository.flights() }.isFailure)
    }

    @Test
    fun missingOrDeniedDetailsAreEvictedInsteadOfReturnedFromCache() = runBlocking {
        val repository = repository()
        repository.refreshCache()
        repository.flight("one")
        repository.flight("two")
        detailFailure = ApiException(404, "Missing")
        assertTrue(runCatching { repository.flight("one") }.exceptionOrNull() is ApiException)
        online = false
        assertEquals(listOf("two"), repository.flights().items.map { it.id })
        assertTrue(runCatching { repository.flight("one") }.isFailure)
        online = true
        detailFailure = ApiException(403, "Denied")
        assertTrue(runCatching { repository.flight("two") }.exceptionOrNull() is ApiException)
        assertTrue(dao.details(owner.value!!).isEmpty())
        online = false
        assertTrue(repository.flights().items.isEmpty())
    }

    @Test
    fun unauthorizedResponseClearsFlightsEvenAfterTokenStoreRemovedTheOwner() = runBlocking {
        val repository = repository()
        repository.refreshCache()
        repository.flight("one")
        pageLoader = { _, _, _ -> owner.value = null; throw ApiException(401, "Expired") }
        assertTrue(runCatching { repository.flights() }.exceptionOrNull() is ApiException)
        assertFalse(dao.hasAnyCache())
        owner.value = "http://branch/|account"
        online = false
        assertTrue(runCatching { repository.flights() }.isFailure)
    }

    @Test
    fun accountAndApiEnvironmentChangesNeverExposeAnotherFlightSnapshot() = runBlocking {
        val repository = repository()
        repository.refreshCache()
        repository.flight("one")
        online = false
        owner.value = "http://other-api/|account"
        assertTrue(runCatching { repository.flights() }.isFailure)
        assertTrue(runCatching { repository.flight("one") }.isFailure)
        assertEquals(0, repository.syncStatus.first().itemCount)
        owner.value = "http://branch/|different-account"
        assertTrue(runCatching { repository.flights() }.isFailure)
        owner.value = null
        assertTrue(runCatching { repository.flights() }.isFailure)
    }

    @Test
    fun lateFullResponsesCannotRestoreDataAfterSameAccountCleanup() = runBlocking {
        val response = CompletableDeferred<FlightPage>()
        pageLoader = { _, _, _ -> response.await() }
        val repository = repository()
        val pending = launch(start = CoroutineStart.UNDISPATCHED) { repository.refreshCache() }
        assertTrue(repository.isSyncing.value)
        repository.clearLocal()
        response.complete(FlightPage(listOf(item("old")), 1, 100, 1, 1))
        pending.join()
        assertFalse(dao.hasAnyCache())
        assertFalse(repository.isSyncing.value)
    }

    @Test
    fun aLateDetailCannotUndoAFullVisibilityRecheck() = runBlocking {
        val repository = repository()
        repository.refreshCache()
        val response = CompletableDeferred<FlightDetail>()
        detailLoader = { response.await() }
        val pending = launch(start = CoroutineStart.UNDISPATCHED) { runCatching { repository.flight("one") } }
        serverItems = emptyList()
        val fullRefresh = launch(start = CoroutineStart.UNDISPATCHED) { repository.refreshCache() }
        response.complete(detail("one"))
        pending.join()
        fullRefresh.join()
        assertTrue(dao.details(owner.value!!).isEmpty())
        assertEquals(0, repository.syncStatus.first().itemCount)
    }

    @Test
    fun aLateOnlineListReturnsTheLatestScopedSnapshotAfterFullSync() = runBlocking {
        val repository = repository()
        repository.refreshCache()
        val stalePage = CompletableDeferred<FlightPage>()
        pageLoader = { page, size, _ ->
            if (size == 20) stalePage.await()
            else FlightPage(emptyList(), page, size, 0, 0)
        }
        val pending = async(start = CoroutineStart.UNDISPATCHED) { repository.flights() }
        repository.refreshCache()
        stalePage.complete(FlightPage(serverItems, 1, 20, 2, 1))

        val current = pending.await()

        assertTrue(current.fromCache)
        assertTrue(current.items.isEmpty())
        assertEquals(0L, current.totalCount)
    }

    @Test
    fun openingAnotherDetailDuringFullSyncWaitsAndPreservesTheNewDetail() = runBlocking {
        val repository = repository()
        repository.refreshCache()
        repository.flight("one")
        val firstDetail = CompletableDeferred<FlightDetail>()
        detailLoader = { id -> if (id == "one") firstDetail.await() else detail(id, "newer") }
        val fullRefresh = launch(start = CoroutineStart.UNDISPATCHED) { repository.refreshCache() }
        val interactive = launch(start = CoroutineStart.UNDISPATCHED) { repository.flight("two") }
        assertNull(dao.detail(owner.value!!, "two"))
        firstDetail.complete(detail("one"))
        fullRefresh.join()
        interactive.join()
        online = false
        assertEquals("newer", repository.flight("two").flightNumber)
        assertEquals(2, dao.details(owner.value!!).size)
    }

    @Test
    fun knownDeniedDetailsStayEvictedWhenALaterDetailRefreshFails() = runBlocking {
        val repository = repository()
        repository.refreshCache()
        repository.flight("one")
        repository.flight("two")
        detailLoader = { id ->
            if (id == "one") throw ApiException(403, "Denied")
            throw IOException("Disconnected")
        }

        repository.refreshCache()

        assertNotNull(repository.syncStatus.first().error)
        online = false
        assertEquals(listOf("two"), repository.flights().items.map { it.id })
        assertTrue(runCatching { repository.flight("one") }.isFailure)
        assertTrue(repository.flight("two").fromCache)
    }

    @Test
    fun failedReadsForAlreadyEmptyCachesDoNotEmitRepeatedReloadEvents() = runBlocking {
        val repository = repository()
        repository.refreshCache()
        var changes = 0
        val observer = launch(start = CoroutineStart.UNDISPATCHED) { repository.cacheChanges.collect { changes++ } }
        listFailure = ApiException(403, "Denied")
        runCatching { repository.flights() }
        kotlinx.coroutines.yield()
        assertEquals(1, changes)
        repeat(3) { runCatching { repository.flights() } }
        detailFailure = ApiException(404, "Missing")
        repeat(3) { runCatching { repository.flight("absent") } }
        kotlinx.coroutines.yield()
        assertEquals(1, changes)
        observer.cancel()
    }

    @Test
    fun listSearchAndUnchangedSyncReuseLogosWhileLostFlightAccessPurgesThem() = runBlocking {
        val folder = Files.createTempDirectory("flight-logos").toFile()
        var reads = 0
        logoStore = CustomerLogoStore(folder, { _, version -> reads++; CustomerLogoDownload(version!!, byteArrayOf(1)) },
            owner, { owner.value }, MutableStateFlow(true), this, Dispatchers.Unconfined)
        try {
            serverItems = listOf(item("one").copy(customerId = "customer", customerLogoVersion = "v1"))
            val repository = repository()
            repository.refreshCache()
            assertArrayEquals(byteArrayOf(1), logoStore!!.load("customer"))
            repository.flights(search = "123")
            repository.refreshCache()
            assertArrayEquals(byteArrayOf(1), logoStore!!.load("customer"))
            assertEquals(1, reads)
            listFailure = ApiException(403, "Denied")
            assertTrue(runCatching { repository.flights() }.isFailure)
            assertFalse(folder.walkTopDown().any { it.isFile })
        } finally { folder.deleteRecursively() }
    }

    @Test
    fun cachedPagesCannotKeepCustomerFilesAfterAFreshCompleteVisibilityRevocation() = runBlocking {
        val folder = Files.createTempDirectory("revoked-flight-logos").toFile()
        logoStore = CustomerLogoStore(folder, { _, version -> CustomerLogoDownload(version!!, byteArrayOf(1)) },
            owner, { owner.value }, MutableStateFlow(true), this, Dispatchers.Unconfined)
        try {
            serverItems = listOf(item("one").copy(customerId = "customer", customerLogoVersion = "v1"))
            val repository = repository()
            repository.refreshCache()
            logoStore!!.load("customer")
            val response = CompletableDeferred<FlightPage>()
            pageLoader = { _, _, _ -> response.await() }
            val refresh = launch(start = CoroutineStart.UNDISPATCHED) { repository.refreshCache() }
            online = false
            assertEquals(1, repository.flights().items.size)
            online = true
            response.complete(FlightPage(emptyList(), page = 1, pageSize = 100))
            refresh.join()
            assertNull(logoStore!!.load("customer"))
            assertFalse(folder.walkTopDown().any { it.extension == "img" })
        } finally { folder.deleteRecursively() }
    }

    @Test
    fun invitationsRemainOnlineOnlyAndNeverQueueLocalWrites() = runBlocking {
        val repository = repository()
        repository.refreshCache()
        online = false
        assertTrue(runCatching { repository.invitationOptions("one") }.isFailure)
        assertTrue(runCatching { repository.invite("one", listOf("employee"), "version") }.isFailure)
        assertEquals(0, invitationReads)
        assertEquals(0, invitationWrites)
        online = true
        repository.invitationOptions("one")
        repository.invite("one", listOf("employee"), "version")
        assertEquals(1, invitationReads)
        assertEquals(1, invitationWrites)
    }

    private fun item(id: String, number: String = "123") = FlightListItem(
        id, number, "XY", "Airline", "AAA", "Scheduled", "2026-11-04T10:00:00Z", "2026-11-04T11:00:00Z", false,
    )
    private fun detail(id: String, number: String = "123") = FlightDetail(
        id, number, "customer", "XY", "Airline", "station", "AAA", "Station", "operation", "Scheduled",
        scheduledArrivalUtc = "2026-11-04T10:00:00Z", scheduledDepartureUtc = "2026-11-04T11:00:00Z",
        isPerLanding = false, createdAtUtc = "2026-11-03T10:00:00Z", rowVersion = "version",
    )
}

private class MemoryFlightCacheDao : FlightCacheDao {
    private val snapshots = MutableStateFlow<Map<String, FlightSnapshotEntity>>(emptyMap())
    private val details = mutableMapOf<Pair<String, String>, FlightDetailCacheEntity>()
    override fun observe(ownerSubject: String): Flow<FlightSnapshotEntity?> = snapshots.map { it[ownerSubject] }
    override suspend fun snapshot(ownerSubject: String) = snapshots.value[ownerSubject]
    override suspend fun details(ownerSubject: String) = details.values.filter { it.ownerSubject == ownerSubject }
    override suspend fun detail(ownerSubject: String, flightId: String) = details[ownerSubject to flightId]
    override suspend fun putSnapshot(snapshot: FlightSnapshotEntity) {
        snapshots.value = snapshots.value + (snapshot.ownerSubject to snapshot)
    }
    override suspend fun putDetails(details: List<FlightDetailCacheEntity>) {
        details.forEach { this.details[it.ownerSubject to it.flightId] = it }
    }
    override suspend fun deleteDetails(ownerSubject: String) {
        details.keys.removeAll { it.first == ownerSubject }
    }
    override suspend fun hasAnyCache() = snapshots.value.isNotEmpty() || details.isNotEmpty()
    override suspend fun deleteSnapshots() { snapshots.value = emptyMap() }
    override suspend fun deleteAllDetails() { details.clear() }
}
