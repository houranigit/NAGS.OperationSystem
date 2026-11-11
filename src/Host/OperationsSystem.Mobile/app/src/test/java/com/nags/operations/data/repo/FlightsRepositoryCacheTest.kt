package com.nags.operations.data.repo

import com.nags.operations.data.CustomerLogoDownload

import com.nags.operations.data.ApiException
import com.nags.operations.data.FlightCategory
import com.nags.operations.data.FlightDetail
import com.nags.operations.data.FlightListItem
import com.nags.operations.data.FlightPage
import com.nags.operations.data.db.dao.FlightCacheDao
import com.nags.operations.data.db.entities.FlightDetailCacheEntity
import com.nags.operations.data.db.entities.FlightSnapshotEntity
import java.io.IOException
import java.time.Instant
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import java.nio.file.Files
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CoroutineStart
import kotlinx.coroutines.async
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.collect
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
    private var serverStation = "station"
    private var includeScope = true
    private val categoryReads = mutableListOf<FlightCategory?>()
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
        fetchFlights = { page, size, search, category ->
            categoryReads += category
            pageLoader(page, size, search).let { result ->
                val filtered = result.copy(items = result.items.filter { category == null || when (category) {
                    FlightCategory.MyFlights -> it.isMyFlight
                    FlightCategory.PerLanding -> it.isPerLanding && !it.isMyAdHocFlight
                    FlightCategory.AdHoc -> it.isMyAdHocFlight
                } })
                if (includeScope) scoped(filtered) else filtered
            }
        },
        fetchFlight = { detailLoader(it) },
        fetchInvitationOptions = { invitationReads++; emptyList() },
        sendInvitation = { _, _, _ -> invitationWrites++ },
        logoStore = logoStore,
        dao = dao, ownerFlow = owner, currentOwner = { owner.value }, isOnline = { online }, now = { clock },
    )

    @Test
    fun registerWithoutRequiredStationTimeMetadataUsesTheExistingRefreshPolicy() = runBlocking {
        val repository = repository()
        repository.refreshCache()
        repository.flight("one")
        val snapshot = dao.snapshot(owner.value!!)!!
        val payload = Json.parseToJsonElement(snapshot.payloadJson).jsonObject
        val oldRows = JsonArray(payload.getValue("items").jsonArray.map { row ->
            JsonObject(row.jsonObject - "stationTimeZoneId")
        })
        dao.putSnapshot(snapshot.copy(payloadJson = JsonObject(payload + ("items" to oldRows)).toString()))

        online = false
        assertTrue(runCatching { repository.flights() }.isFailure)
        assertTrue(runCatching { repository.flight("one") }.isFailure)
        online = true
        repository.refreshCache()
        assertEquals("Asia/Riyadh", repository.flights().items.first().stationTimeZoneId)
    }

    @Test
    fun detailWithoutRequiredStationTimeMetadataRequiresTheExistingOnlineRead() = runBlocking {
        val repository = repository()
        repository.refreshCache()
        repository.flight("one")
        val cached = dao.detail(owner.value!!, "one")!!
        val oldDetail = JsonObject(Json.parseToJsonElement(cached.payloadJson).jsonObject - "stationTimeZoneId")
        dao.putDetails(listOf(cached.copy(payloadJson = oldDetail.toString())))

        online = false
        assertEquals(2, repository.flights().items.size)
        assertTrue(runCatching { repository.flight("one") }.isFailure)
        online = true
        assertEquals("Asia/Riyadh", repository.flight("one").stationTimeZoneId)
        online = false
        assertEquals("Asia/Riyadh", repository.flight("one").stationTimeZoneId)
    }

    @Test
    fun acceptedArrivalDoesNotDependOnDecodingAnOlderDetailCache() = runBlocking {
        val repository = repository()
        repository.refreshCache()
        repository.flight("one")
        val cached = dao.detail(owner.value!!, "one")!!
        val oldDetail = JsonObject(Json.parseToJsonElement(cached.payloadJson).jsonObject - "stationTimeZoneId")
        dao.putDetails(listOf(cached.copy(payloadJson = oldDetail.toString())))
        val employee = com.nags.operations.data.AssignedEmployee("staff", "Employee", "E1")
        val arrival = com.nags.operations.data.FlightArrivalSubmission("arrival", employee,
            "2026-11-04T10:00:00Z", "aircraft", "A320", "HZ-TEST", receivedByEmployee = employee,
            isAccepted = true, createdAtUtc = "2026-11-04T10:00:00Z")

        repository.recordArrival("one", com.nags.operations.data.FlightArrivalSaved(arrival, "new-version"))

        online = false
        assertTrue(runCatching { repository.flight("one") }.isFailure)
    }

    @Test
    fun notificationLinksReadLocalMyFlightsWithoutNetworkOrDetailRequests() = runBlocking {
        val id = "legacy-flight-id"
        serverItems = listOf(item(id))
        val repository = repository()
        repository.refreshCache()
        val readsBefore = pageReads.toList()
        pageLoader = { _, _, _ -> throw AssertionError("A notification lookup must not fetch flights") }
        detailLoader = { throw AssertionError("A notification lookup must not fetch details") }

        assertEquals(setOf(id), repository.syncedMyFlightIds.first { it.isNotEmpty() })
        online = false
        assertEquals(setOf(id), repository.syncedMyFlightIds.first { it.isNotEmpty() })
        assertEquals(readsBefore, pageReads)
        assertTrue(detailReads.isEmpty())
        assertTrue(dao.details(owner.value!!).isEmpty())
    }

    @Test
    fun notificationMembershipUsesTheSameMyFlightsPredicateAsTheTab() = runBlocking {
        serverItems = listOf(
            item("assigned"),
            item("landing").copy(isMyFlight = false, isPerLanding = true),
            item("ad-hoc").copy(isMyFlight = false, isMyAdHocFlight = true),
            item("mixed").copy(isMyFlight = false, isPerLanding = true, isMyAdHocFlight = true),
            // The server tab flag remains authoritative even for legacy mixed flags.
            item("my-flight-flag").copy(isPerLanding = true, isMyAdHocFlight = true),
        )
        val repository = repository()
        repository.refreshCache()
        val expected = repository.flights(FlightCategory.MyFlights).items.map { it.id }.toSet()
        assertEquals(setOf("assigned", "my-flight-flag"), expected)
        val readsBefore = pageReads.toList()
        pageLoader = { _, _, _ -> throw AssertionError("Notification membership must not request another page") }
        detailLoader = { throw AssertionError("Notification membership must not fetch details") }

        assertEquals(expected, repository.syncedMyFlightIds.first { it.isNotEmpty() })
        online = false
        assertEquals(expected, repository.syncedMyFlightIds.first { it.isNotEmpty() })
        assertEquals(readsBefore, pageReads)
        assertTrue(detailReads.isEmpty())
    }

    @Test
    fun notificationMembershipChangesWithTheAccountWithoutFetchingFlights() = runBlocking {
        val firstId = java.util.UUID(0, 1).toString()
        val secondId = java.util.UUID(0, 2).toString()
        val firstOwner = owner.value!!
        val repository = repository()
        serverItems = listOf(item(firstId))
        repository.refreshCache()
        owner.value = "http://other-api/|another-account"
        serverItems = listOf(item(secondId))
        repository.refreshCache()
        val readsBefore = pageReads.toList()
        val observed = MutableStateFlow(emptySet<String>())
        val collector = launch(start = CoroutineStart.UNDISPATCHED) {
            repository.syncedMyFlightIds.collect { observed.value = it }
        }
        try {
            observed.first { it == setOf(secondId) }
            owner.value = firstOwner
            assertEquals(setOf(firstId), observed.first { it == setOf(firstId) })
            owner.value = null
            assertTrue(observed.first { it.isEmpty() }.isEmpty())
            assertEquals(readsBefore, pageReads)
            assertTrue(detailReads.isEmpty())
        } finally { collector.cancel() }
    }

    @Test
    fun clearingLocalFlightsRemovesNotificationMembershipWithoutFetchingAgain() = runBlocking {
        serverItems = listOf(item("my-flight"))
        val repository = repository()
        repository.refreshCache()
        val readsBefore = pageReads.toList()
        val observed = MutableStateFlow(emptySet<String>())
        val collector = launch(start = CoroutineStart.UNDISPATCHED) {
            repository.syncedMyFlightIds.collect { observed.value = it }
        }
        try {
            observed.first { it == setOf("my-flight") }

            repository.clearLocal()

            assertTrue(observed.first { it.isEmpty() }.isEmpty())
            assertEquals(readsBefore, pageReads)
            assertTrue(detailReads.isEmpty())
        } finally { collector.cancel() }
    }

    @Test
    fun confirmedStationChangesRemoveNotificationTargetsUntilTheNewManifestIsSynced() = runBlocking {
        val oldId = java.util.UUID(0, 1).toString()
        val newId = java.util.UUID(0, 2).toString()
        serverItems = listOf(item(oldId))
        val repository = repository()
        repository.refreshCache()
        val observed = MutableStateFlow(emptySet<String>())
        val collector = launch(start = CoroutineStart.UNDISPATCHED) {
            repository.syncedMyFlightIds.collect { observed.value = it }
        }
        try {
            observed.first { it == setOf(oldId) }
            serverStation = "new-station"
            serverItems = listOf(item(newId).copy(stationId = serverStation))
            repository.flights() // Existing flight reads confirm new station metadata; lookup itself does not fetch.
            assertTrue(observed.first { it.isEmpty() }.isEmpty())
            assertNull(dao.snapshot(owner.value!!)?.lastSuccessUtc)
            repository.refreshCache()
            assertEquals(setOf(newId), observed.first { it == setOf(newId) })
            assertTrue(detailReads.isEmpty())
        } finally { collector.cancel() }
    }

    @Test
    fun expiredOrRevokedSyncedFlightsAreRemovedFromLocalNotificationMembership() = runBlocking {
        val id = java.util.UUID(0, 1).toString()
        serverItems = listOf(item(id))
        val repository = repository()
        repository.refreshCache()
        val observed = MutableStateFlow(emptySet<String>())
        val collector = launch(start = CoroutineStart.UNDISPATCHED) {
            repository.syncedMyFlightIds.collect { observed.value = it }
        }
        try {
            observed.first { it == setOf(id) }
            val readsBeforePrune = pageReads.toList()
            clock = "2026-11-04T23:00:00.001Z"
            repository.pruneExpired()
            assertTrue(observed.first { it.isEmpty() }.isEmpty())
            assertEquals(readsBeforePrune, pageReads)

            clock = "2026-11-04T10:00:00Z"
            repository.refreshCache()
            observed.first { it == setOf(id) }
            listFailure = ApiException(403, "Flight access revoked")
            assertTrue(runCatching { repository.flights() }.isFailure)
            assertTrue(observed.first { it.isEmpty() }.isEmpty())
            assertTrue(dao.snapshot(owner.value!!)!!.permissionDenied)
        } finally { collector.cancel() }
    }

    @Test
    fun apiOffsetTimestampsSupportLiveReadsAndOfflineFlightSnapshots() = runBlocking {
        val arrival = "2026-11-04T13:00:00+03:00"
        val departure = "2026-11-04T11:00:00+00:00"
        val response = FlightPage(items = listOf(item("one").copy(
            scheduledArrivalUtc = arrival, scheduledDepartureUtc = departure)), totalCount = 1,
            totalPages = 1, stationId = serverStation, asOfUtc = "2026-11-04T10:00:00.1234567+00:00",
            windowStartUtc = "2026-11-03T22:00:00.1234567+00:00",
            windowEndUtc = "2026-11-04T22:00:00.1234567+00:00")
        pageLoader = { page, size, _ -> response.copy(page = page, pageSize = size) }
        detailLoader = { id -> detail(id).copy(scheduledArrivalUtc = arrival, scheduledDepartureUtc = departure) }
        val repository = repository()

        assertEquals(listOf("one"), repository.flights(FlightCategory.MyFlights).items.map { it.id })
        repository.refreshCache()
        assertNull(repository.syncStatus.first().error)
        assertEquals(1, repository.syncStatus.first().itemCount)
        assertEquals(departure, repository.flight("one").scheduledDepartureUtc)
        online = false
        assertEquals(listOf("one"), repository.flights().items.map { it.id })
        assertTrue(repository.flights().fromCache)
        assertTrue(repository.flight("one").fromCache)
    }

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
        assertNull(dao.snapshot(owner.value!!)?.lastSuccessUtc)
        assertTrue(dao.details(owner.value!!).isEmpty())
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

    @Test
    fun offlineTabsUseServerMembershipAndKeepAdHocPrecedenceForMixedFlights() = runBlocking {
        serverItems = listOf(
            item("mine"),
            item("per-landing").copy(isMyFlight = false, isPerLanding = true),
            item("ad-hoc").copy(isMyFlight = false, isMyAdHocFlight = true),
            item("mixed").copy(isMyFlight = false, isPerLanding = true, isMyAdHocFlight = true),
        )
        val repository = repository()
        repository.refreshCache()
        assertEquals(listOf<FlightCategory?>(null), categoryReads)
        online = false

        assertEquals(listOf("mine"), repository.flights(FlightCategory.MyFlights).items.map { it.id })
        assertEquals(listOf("per-landing"), repository.flights(FlightCategory.PerLanding).items.map { it.id })
        assertEquals(listOf("ad-hoc", "mixed"), repository.flights(FlightCategory.AdHoc).items.map { it.id })
        assertEquals(4, repository.flights().items.size)
        online = true
        repository.flights(FlightCategory.AdHoc)
        assertEquals(FlightCategory.AdHoc, categoryReads.last())
    }

    @Test
    fun legacyBroadSnapshotAndOrphanDetailsAreNeverUsedOffline() = runBlocking {
        val subject = owner.value!!
        dao.putSnapshot(FlightSnapshotEntity(subject, Json.encodeToString(serverItems), serverItems.size,
            lastSuccessUtc = clock))
        dao.putDetails(listOf(FlightDetailCacheEntity(subject, "one", Json.encodeToString(detail("one")))))
        online = false
        val repository = repository()

        assertNull(repository.syncStatus.first().lastSuccessUtc)
        assertTrue(runCatching { repository.flights() }.isFailure)
        assertTrue(runCatching { repository.flight("one") }.isFailure)
        assertTrue(dao.details(subject).isEmpty())
        assertNull(dao.snapshot(subject)!!.lastSuccessUtc)
        assertFalse(dao.snapshot(subject)!!.payloadJson.startsWith("["))
    }

    @Test
    fun rollingWindowPrunesSavedRowsAndTheirDetailsAtTheInclusiveBoundary() = runBlocking {
        serverItems = listOf(
            item("long").copy(scheduledArrivalUtc = "2026-11-02T00:00:00Z"),
            item("boundary").copy(scheduledArrivalUtc = "2026-11-03T21:00:00Z",
                scheduledDepartureUtc = "2026-11-03T22:00:00Z"),
        )
        val repository = repository()
        repository.refreshCache()
        repository.flight("boundary")
        online = false
        assertEquals(listOf("long", "boundary"), repository.flights().items.map { it.id })
        clock = "2026-11-04T10:00:00.001Z"

        repository.pruneExpired()

        assertEquals(listOf("long"), repository.flights().items.map { it.id })
        assertNull(dao.detail(owner.value!!, "boundary"))
        assertTrue(runCatching { repository.flight("boundary") }.isFailure)
        assertEquals(1, repository.syncStatus.first().itemCount)
        clock = "2026-11-05T00:00:00Z"
        assertTrue(repository.flights().items.isEmpty())
        assertEquals(0, repository.syncStatus.first().itemCount)
    }

    @Test
    fun liveDetailsRequireCompleteScopedMembershipBeforeTheyCanBeSavedOffline() = runBlocking {
        val repository = repository()
        assertFalse(repository.flight("one").fromCache)
        assertTrue(dao.details(owner.value!!).isEmpty())
        online = false
        assertTrue(runCatching { repository.flight("one") }.isFailure)
        online = true
        repository.refreshCache()
        repository.flight("one")
        online = false
        assertTrue(repository.flight("one").fromCache)
    }

    @Test
    fun missingScopeAndWrongStationResponsesCannotBecomeAnOfflineSnapshot() = runBlocking {
        includeScope = false
        val repository = repository()
        assertTrue(runCatching { repository.flights() }.exceptionOrNull() is IOException)
        repository.refreshCache()
        assertNull(repository.syncStatus.first().lastSuccessUtc)
        includeScope = true
        serverItems = listOf(item("foreign").copy(stationId = "other-station"))
        repository.refreshCache()
        assertNull(repository.syncStatus.first().lastSuccessUtc)
        online = false
        assertTrue(runCatching { repository.flights() }.isFailure)
    }

    @Test
    fun newlyConfirmedStationCannotFallBackToThePreviousStationSnapshot() = runBlocking {
        val repository = repository()
        repository.refreshCache()
        repository.flight("one")
        serverStation = "new-station"
        serverItems = listOf(item("new").copy(stationId = serverStation))

        assertEquals(listOf("new"), repository.flights().items.map { it.id })

        assertTrue(dao.details(owner.value!!).isEmpty())
        online = false
        assertTrue(runCatching { repository.flights() }.isFailure)
        assertTrue(runCatching { repository.flight("one") }.isFailure)
    }

    @Test
    fun pendingFullSnapshotCannotRestoreAPreviouslyConfirmedStation() = runBlocking {
        val repository = repository()
        repository.refreshCache()
        repository.flight("one")
        val oldResponse = scoped(FlightPage(serverItems, 1, 100, 2, 1))
        val delayed = CompletableDeferred<FlightPage>()
        pageLoader = { page, size, _ ->
            if (size == 100) delayed.await()
            else FlightPage(serverItems, page, size, serverItems.size.toLong(), 1)
        }
        val pending = launch(start = CoroutineStart.UNDISPATCHED) { repository.refreshCache() }
        serverStation = "new-station"
        serverItems = listOf(item("new").copy(stationId = serverStation))
        assertEquals(listOf("new"), repository.flights().items.map { it.id })

        delayed.complete(oldResponse)
        pending.join()

        assertNull(repository.syncStatus.first().lastSuccessUtc)
        assertTrue(dao.details(owner.value!!).isEmpty())
        online = false
        assertTrue(runCatching { repository.flights() }.isFailure)
        assertTrue(runCatching { repository.flight("one") }.isFailure)
    }

    @Test
    fun newStationMetadataRevokesOldCacheEvenWhenBackgroundPaginationFails() = runBlocking {
        val repository = repository()
        repository.refreshCache()
        repository.flight("one")
        var changes = 0
        val observer = launch(start = CoroutineStart.UNDISPATCHED) { repository.cacheChanges.collect { changes++ } }
        serverStation = "new-station"
        pageLoader = { page, size, _ ->
            if (page == 2) throw IOException("Disconnected")
            FlightPage(listOf(item("new").copy(stationId = serverStation)), page, size, 2, 2, false, true)
        }

        repository.refreshCache()
        kotlinx.coroutines.yield()

        assertEquals(1, changes)
        assertNull(repository.syncStatus.first().lastSuccessUtc)
        assertTrue(dao.details(owner.value!!).isEmpty())
        online = false
        assertTrue(runCatching { repository.flights() }.isFailure)
        assertTrue(runCatching { repository.flight("one") }.isFailure)
        observer.cancel()
    }

    @Test fun acknowledgedDepartureIsAvailableOfflineWithoutReplacingPlanningDetails() = runBlocking {
        val repository = repository()
        repository.refreshCache()
        val original = repository.flight("one")
        val employee = com.nags.operations.data.AssignedEmployee("employee", "Employee", "E1")
        val submission = com.nags.operations.data.FlightDepartureSubmission("departure", employee,
            "2026-11-04T11:15:00Z", employee, remarks = "Departed", isAccepted = true, createdAtUtc = clock)
        repository.recordDeparture("one", com.nags.operations.data.FlightDepartureSaved(submission, "saved-version"))
        online = false
        val cached = repository.flight("one")
        assertEquals(listOf(submission), cached.departureSubmissions)
        assertEquals("saved-version", cached.rowVersion)
        assertEquals(original.scheduledArrivalUtc, cached.scheduledArrivalUtc)
        assertEquals(original.scheduledDepartureUtc, cached.scheduledDepartureUtc)
        assertEquals(original.arrivalSubmissions, cached.arrivalSubmissions)
    }

    @Test fun acknowledgedReportsRemainOnOfflineCardsAndRevertsPreserveOtherCancellationReports() = runBlocking {
        val repository = repository()
        repository.refreshCache()
        val planned = repository.flight("one")
        val employee = com.nags.operations.data.AssignedEmployee("staff", "Employee", "E1")
        val arrival = com.nags.operations.data.FlightArrivalSubmission("arrival", employee, clock, "actual-aircraft", "A320", "TAIL",
            receivedByEmployee = employee, isAccepted = false, createdAtUtc = clock)
        val departure = com.nags.operations.data.FlightDepartureSubmission("departure", employee, clock, employee,
            isAccepted = false, createdAtUtc = clock)
        val other = employee.copy(staffMemberId = "other")
        val cancellation = com.nags.operations.data.FlightCancellationSubmission("cancel", other, clock,
            isAccepted = false, createdAtUtc = clock)
        repository.recordArrival("one", com.nags.operations.data.FlightArrivalSaved(arrival, "arrival-version"))
        repository.recordDeparture("one", com.nags.operations.data.FlightDepartureSaved(departure, "departure-version"))
        repository.recordCancellation("one", com.nags.operations.data.FlightCancellationSaved(cancellation, "cancel-version"))
        online = false
        val reads = detailReads.toList()
        val reopened = repository().flights().items.first { it.id == "one" }
        assertEquals(true, reopened.hasArrivalSubmission)
        assertEquals(true, reopened.hasDepartureSubmission)
        assertEquals(true, reopened.hasCancellationSubmission)
        assertEquals(planned.scheduledArrivalUtc, reopened.scheduledArrivalUtc)
        assertEquals(planned.scheduledDepartureUtc, reopened.scheduledDepartureUtc)
        assertEquals(listOf("one", "two"), repository.flights().items.map { it.id })
        assertNull(reopened.aircraftTypeModel) // Actual aircraft does not replace planning.

        repository.recordCancellationReverted("one", com.nags.operations.data.FlightCancellationReverted(listOf(cancellation), "retained-version"))
        assertEquals(true, repository.flights().items.first().hasCancellationSubmission)
        repository.recordCancellationReverted("one", com.nags.operations.data.FlightCancellationReverted(emptyList(), "reverted-version"))
        val reverted = repository().flights().items.first()
        assertEquals(false, reverted.hasCancellationSubmission)
        assertEquals(true, reverted.hasArrivalSubmission)
        assertEquals(true, reverted.hasDepartureSubmission)
        assertEquals(reads, detailReads)
        assertEquals(2, repository.syncStatus.first().itemCount)
    }

    @Test fun legacyCardFlagsUseExistingScopedDetailWithoutOverridingANewerExplicitServerFalse() = runBlocking {
        val employee = com.nags.operations.data.AssignedEmployee("staff", "Employee", "E1")
        val arrival = com.nags.operations.data.FlightArrivalSubmission("arrival", employee, clock, "aircraft", "A320", "TAIL",
            receivedByEmployee = employee, isAccepted = true, createdAtUtc = clock)
        detailLoader = { id -> detail(id).copy(arrivalSubmissions = listOf(arrival)) }
        val repository = repository()
        repository.refreshCache()
        repository.flight("one")
        val snapshot = dao.snapshot(owner.value!!)!!
        val payload = Json.parseToJsonElement(snapshot.payloadJson).jsonObject
        val oldRows = JsonArray(payload.getValue("items").jsonArray.map { row ->
            JsonObject(row.jsonObject - setOf("hasArrivalSubmission", "hasDepartureSubmission", "hasCancellationSubmission"))
        })
        dao.putSnapshot(snapshot.copy(payloadJson = JsonObject(payload + ("items" to oldRows)).toString()))
        online = false
        val reads = detailReads.toList()
        val legacy = repository.flights().items
        assertEquals(true, legacy.first { it.id == "one" }.hasArrivalSubmission)
        assertNull(legacy.first { it.id == "two" }.hasArrivalSubmission)
        val knownRows = JsonArray(oldRows.map { row ->
            JsonObject(row.jsonObject + mapOf("hasArrivalSubmission" to kotlinx.serialization.json.JsonPrimitive(false),
                "hasDepartureSubmission" to kotlinx.serialization.json.JsonPrimitive(false),
                "hasCancellationSubmission" to kotlinx.serialization.json.JsonPrimitive(false)))
        })
        dao.putSnapshot(snapshot.copy(payloadJson = JsonObject(payload + ("items" to knownRows)).toString()))
        assertEquals(false, repository.flights().items.first().hasArrivalSubmission)
        assertEquals(reads, detailReads)
    }

    @Test fun resolvingEmptyLegacyDetailMetadataDoesNotDiscardNewerPlanningListValues() = runBlocking {
        val repository = repository()
        repository.refreshCache()
        val requested = CompletableDeferred<Unit>()
        val response = CompletableDeferred<FlightPage>()
        pageLoader = { _, _, _ -> requested.complete(Unit); response.await() }
        val pending = async(start = CoroutineStart.UNDISPATCHED) { repository.flights(FlightCategory.MyFlights) }
        requested.await()
        repository.flight("one") // No reports: unknown legacy metadata becomes explicit false.
        response.complete(FlightPage(listOf(item("one", "456")), 1, 20, 1, 1))
        assertEquals("456", pending.await().items.single().flightNumber)
    }

    @Test fun aListReadStartedBeforeAnAcknowledgedReportCannotRestoreItsOldBadgeFlags() = runBlocking {
        val repository = repository()
        repository.refreshCache()
        repository.flight("one")
        val requested = CompletableDeferred<Unit>()
        val response = CompletableDeferred<FlightPage>()
        pageLoader = { _, _, _ -> requested.complete(Unit); response.await() }
        val pending = async(start = CoroutineStart.UNDISPATCHED) { repository.flights(FlightCategory.MyFlights) }
        requested.await()
        val employee = com.nags.operations.data.AssignedEmployee("staff", "Employee", "E1")
        val arrival = com.nags.operations.data.FlightArrivalSubmission("arrival", employee, clock, "aircraft", "A320", "TAIL",
            receivedByEmployee = employee, isAccepted = false, createdAtUtc = clock)
        repository.recordArrival("one", com.nags.operations.data.FlightArrivalSaved(arrival, "saved-version"))
        response.complete(FlightPage(listOf(item("one").copy(hasArrivalSubmission = false)), 1, 20, 1, 1))
        assertEquals(true, pending.await().items.first().hasArrivalSubmission)
        online = false
        assertEquals(true, repository().flights().items.first().hasArrivalSubmission)
    }

    private fun scoped(page: FlightPage): FlightPage {
        if (page.stationId != null) return page
        val at = Instant.parse(clock)
        return page.copy(stationId = serverStation, asOfUtc = clock,
            windowStartUtc = at.minusSeconds(12 * 60 * 60).toString(),
            windowEndUtc = at.plusSeconds(12 * 60 * 60).toString())
    }

    private fun item(id: String, number: String = "123") = FlightListItem(
        id, number, "XY", "Airline", "AAA", "Scheduled", "2026-11-04T10:00:00Z", "2026-11-04T11:00:00Z", false,
        stationId = "station", isMyFlight = true, stationTimeZoneId = "Asia/Riyadh",
    )
    private fun detail(id: String, number: String = "123") = FlightDetail(
        id, number, "customer", "XY", "Airline", "station", "AAA", "Station", "operation", "Scheduled",
        scheduledArrivalUtc = "2026-11-04T10:00:00Z", scheduledDepartureUtc = "2026-11-04T11:00:00Z",
        isPerLanding = false, createdAtUtc = "2026-11-03T10:00:00Z", rowVersion = "version", stationTimeZoneId = "Asia/Riyadh",
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
