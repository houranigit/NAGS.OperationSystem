package com.nags.operations.data.repo

import com.nags.operations.data.*
import com.nags.operations.data.db.dao.FlightDiscoveryDao
import com.nags.operations.data.db.entities.*
import java.io.IOException
import java.time.Instant
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CoroutineStart
import kotlinx.coroutines.async
import kotlinx.coroutines.launch
import kotlinx.coroutines.flow.*
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.yield
import org.junit.Assert.*
import org.junit.Test

class DiscoveryFlightsRepositoryTest {
    @Test fun exactQueriesNormalizeAndKeepSourceMembershipSeparateWithoutCreatingScheduledAssignments() = runBlocking {
        val f = Fixture()
        val repo = f.repository()
        f.fetch = { source, term, number, size ->
            if (source == FlightDiscoverySource.Search) {
                assertEquals("SV123", term)
                f.page(listOf(f.row("searched")), number, size)
            } else {
                assertEquals("airline", term)
                f.page(listOf(f.row("history")), number, size)
            }
        }
        assertFalse(repo.page(FlightDiscoverySource.Search, " sv123 ").items.single().isMyFlight)
        repo.recordFetched(f.detail("searched"), repo.detailRevision("searched"))
        repo.page(FlightDiscoverySource.History, " AIRLINE ")
        repo.recordFetched(f.detail("history"), repo.detailRevision("history"))
        assertTrue(f.dao.interactions(f.firstOwner).isEmpty())
        f.online = false
        assertEquals(listOf("searched"), repo.page(FlightDiscoverySource.Search, "SV123").items.map { it.id })
        assertEquals(listOf("history"), repo.page(FlightDiscoverySource.History, "airline").items.map { it.id })
        assertTrue(runCatching { repo.page(FlightDiscoverySource.Search, "history") }.isFailure)
        assertTrue(runCatching { repo.page(FlightDiscoverySource.History) }.isFailure)
        repo.recordFetched(f.detail("scheduled-only"), 0)
        assertNull(repo.cachedFlight("scheduled-only"))
    }

    @Test fun partialLivePagesCannotAuthorizeOfflineQueriesOrDetailsUntilEveryPageIsValidated() = runBlocking {
        val f = Fixture()
        val repo = f.repository()
        f.fetch = { _, _, number, size ->
            f.page(listOf(f.row(if (number == 1) "one" else "two")), number, size, total = 2, hasNext = number == 1)
        }
        assertEquals("one", repo.page(FlightDiscoverySource.Search, "SV123", pageSize = 1).items.single().id)
        repo.recordFetched(f.detail("one"), 0)
        assertNull(repo.cachedFlight("one"))
        f.online = false
        assertTrue(runCatching { repo.page(FlightDiscoverySource.Search, "SV123", pageSize = 1) }.isFailure)
        f.online = true
        repo.page(FlightDiscoverySource.Search, "SV123", page = 2, pageSize = 1)
        repo.recordFetched(f.detail("one"), repo.detailRevision("one"))
        assertNotNull(repo.cachedFlight("one"))
        f.online = false
        val cached = repo.page(FlightDiscoverySource.Search, "sv123", page = 2, pageSize = 1)
        assertTrue(cached.fromCache)
        assertEquals(2L, cached.totalCount)
        assertEquals(listOf("two"), cached.items.map { it.id })
    }

    @Test fun overlappingOrOutOfSourcePagesNeverReplaceAValidatedSnapshot() = runBlocking {
        val f = Fixture()
        val repo = f.repository()
        f.fetch = { _, _, number, size -> f.page(listOf(f.row("good")), number, size) }
        repo.page(FlightDiscoverySource.Search, "SV123", pageSize = 1)
        f.fetch = { _, _, number, size -> f.page(listOf(f.row("duplicate")), number, size, total = 2, hasNext = number == 1) }
        repo.page(FlightDiscoverySource.Search, "SV123", pageSize = 1)
        val fallback = repo.page(FlightDiscoverySource.Search, "SV123", page = 2, pageSize = 1)
        assertTrue(fallback.fromCache)
        f.online = false
        assertEquals(listOf("good"), repo.page(FlightDiscoverySource.Search, "SV123").items.map { it.id })
        f.online = true
        f.fetch = { _, _, number, size -> f.page(listOf(f.row("adhoc").copy(isAdHocFlight = true)), number, size) }
        assertTrue(repo.page(FlightDiscoverySource.Search, "SV123").fromCache)
        f.online = false
        assertEquals(listOf("good"), repo.page(FlightDiscoverySource.Search, "SV123").items.map { it.id })
    }

    @Test fun denialRevokesOnlyItsSourceWhileConfirmedFlightRevocationRemovesEveryReadMembership() = runBlocking {
        val f = Fixture()
        val repo = f.repository()
        f.fetch = { source, _, number, size -> f.page(listOf(f.row(if (source == FlightDiscoverySource.Search) "searched" else "history")), number, size) }
        repo.page(FlightDiscoverySource.Search, "SV123")
        repo.page(FlightDiscoverySource.History)
        repo.recordFetched(f.detail("searched"), 0)
        repo.recordFetched(f.detail("history"), 0)
        f.fetch = { _, _, _, _ -> throw ApiException(403, "Source denied") }
        assertTrue(runCatching { repo.page(FlightDiscoverySource.Search, "SV123") }.exceptionOrNull() is ApiException)
        f.online = false
        assertNull(repo.cachedFlight("searched"))
        assertNotNull(repo.cachedFlight("history"))
        assertEquals(listOf("history"), repo.page(FlightDiscoverySource.History).items.map { it.id })
        assertTrue(runCatching { repo.page(FlightDiscoverySource.Search, "SV123") }.exceptionOrNull() is ApiException)
        repo.revoke("history")
        assertNull(repo.cachedFlight("history"))
        assertTrue(repo.page(FlightDiscoverySource.History).items.isEmpty())
    }

    @Test fun accountSwitchRejectsAnOldResponseAndCannotReadThePreviousAccountsSnapshots() = runBlocking {
        val f = Fixture()
        val repo = f.repository()
        val started = CompletableDeferred<Unit>()
        val response = CompletableDeferred<FlightPage>()
        f.fetch = { _, _, _, _ -> started.complete(Unit); response.await() }
        val request = async { runCatching { repo.page(FlightDiscoverySource.Search, "SV123") } }
        started.await()
        f.owner.value = "other-api|other-account"
        response.complete(f.page(listOf(f.row("old-account"))))
        assertTrue(request.await().isFailure)
        assertTrue(f.dao.queries(f.firstOwner).all { it.payloadJson == null })
        f.online = false
        assertTrue(runCatching { repo.page(FlightDiscoverySource.Search, "SV123") }.isFailure)
        assertNull(repo.cachedFlight("old-account"))
    }

    @Test fun confirmedStationTransferDefeatsAnOlderFlightQueryAndRetainsNoOldStationReadRights() = runBlocking {
        val f = Fixture()
        val repo = f.repository()
        repo.page(FlightDiscoverySource.Search, "SV123")
        repo.recordFetched(f.detail("one"), 0)
        val started = CompletableDeferred<Unit>()
        val response = CompletableDeferred<FlightPage>()
        f.fetch = { _, _, _, _ -> started.complete(Unit); response.await() }
        val request = async { runCatching { repo.page(FlightDiscoverySource.Search, "SV456") } }
        started.await()
        repo.confirmStation("other-station")
        response.complete(f.page(listOf(f.row("old-station").copy(flightNumber = "SV456"))))
        assertTrue(request.await().isFailure)
        assertNull(repo.cachedFlight("one"))
        assertEquals("other-station", f.dao.station(f.firstOwner)!!.stationId)
        assertTrue(f.dao.queries(f.firstOwner).isEmpty())
    }

    @Test fun onlySavedWorkCreatesHistoryAndRecentlyViewedRequiresSavedSearchWork() = runBlocking {
        val f = Fixture()
        val repo = f.repository()
        repo.page(FlightDiscoverySource.Search, "SV123")
        repo.recordFetched(f.detail("one"), 0)
        assertTrue(f.dao.interactions(f.firstOwner).isEmpty())
        repo.recordLocalSave(f.detail("scheduled").copy(assignedEmployees = listOf(AssignedEmployee("staff", "Employee", "E1"))))
        f.time = Instant.parse(f.time).plusSeconds(60).toString()
        repo.recordLocalSave(f.detail("searched").copy(isUnassignedSearchFlight = true))
        f.time = Instant.parse(f.time).plusSeconds(60).toString()
        repo.recordLocalSave(f.detail("adhoc").copy(isAdHocFlight = true))
        f.online = false
        val history = repo.page(FlightDiscoverySource.History)
        assertEquals(listOf("adhoc", "searched", "scheduled"), history.items.map { it.id })
        assertEquals(listOf("searched"), repo.page(FlightDiscoverySource.Search).items.map { it.id })
        f.time = Instant.parse(f.time).plusSeconds(60).toString()
        repo.recordLocalSave(f.detail("searched").copy(isUnassignedSearchFlight = false))
        assertEquals(listOf("searched", "adhoc", "scheduled"), repo.page(FlightDiscoverySource.History).items.map { it.id })
        assertEquals(listOf("searched"), repo.page(FlightDiscoverySource.Search).items.map { it.id })
    }

    @Test fun firstOfflineSaveAfterUpgradeUsesOnlyAnExplicitTrustedStationAuthority() = runBlocking {
        val f = Fixture()
        val repo = f.repository()
        f.online = false
        f.trustedStation = null
        assertTrue(runCatching { repo.recordLocalSave(f.detail("one")) }.isFailure)
        assertNull(f.dao.station(f.firstOwner))
        assertTrue(f.dao.interactions(f.firstOwner).isEmpty())
        f.trustedStation = "other-station"
        assertTrue(runCatching { repo.recordLocalSave(f.detail("one")) }.isFailure)
        assertNull(f.dao.station(f.firstOwner))
        // The production callback may accept a current-owner validated normal-flight snapshot,
        // even though an upgraded v13 database has no Discovery or AdHoc rows yet.
        f.trustedStation = f.station
        repo.recordLocalSave(f.detail("one"))
        assertEquals(f.station, f.dao.station(f.firstOwner)!!.stationId)
        assertEquals(listOf("one"), repo.page(FlightDiscoverySource.History).items.map { it.id })
        assertNotNull(repo.cachedFlight("one"))
        assertTrue(f.dao.queries(f.firstOwner).isEmpty())
    }

    @Test fun acknowledgmentDefeatsAnOlderHistoryResponseAndLocalRecencyWaitsForFreshServerHistory() = runBlocking {
        val f = Fixture()
        val repo = f.repository()
        repo.confirmStation(f.station)
        val local = f.detail("one").copy(isUnassignedSearchFlight = true)
        repo.recordLocalSave(local)
        val started = CompletableDeferred<Unit>()
        val response = CompletableDeferred<FlightPage>()
        f.fetch = { _, _, _, _ -> started.complete(Unit); response.await() }
        val request = async { repo.page(FlightDiscoverySource.History) }
        started.await()
        val acknowledged = local.copy(rowVersion = "new-version", customerName = "Current customer")
        repo.recordAcknowledged(acknowledged)
        response.complete(f.page(listOf(f.row("one"))))
        val returned = request.await()
        assertEquals("Current customer", returned.items.single().customerName)
        assertEquals("new-version", repo.cachedFlight("one")!!.rowVersion)
        assertEquals(1, f.dao.interactions(f.firstOwner).size)
        f.fetch = { _, _, number, size -> f.page(listOf(f.row("one").copy(customerName = "Current customer", lastInteractionAtUtc = f.time)), number, size) }
        repo.page(FlightDiscoverySource.History)
        assertTrue(f.dao.interactions(f.firstOwner).isEmpty())
        f.online = false
        assertEquals("Current customer", repo.page(FlightDiscoverySource.History).items.single().customerName)
    }

    @Test fun completeSyncPreparesNeverOpenedFlightsAndKeepsLastReadyDetailsAfterAMediaFailure() = runBlocking {
        val f = Fixture()
        val repo = f.repository()
        f.fetch = { source, _, number, size -> f.page(if (source == FlightDiscoverySource.History)
            listOf(f.row("one"), f.row("two")) else emptyList(), number, size) }
        repo.refreshKnownAndPrepare()
        assertEquals(setOf("one", "two"), f.prepared.toSet())
        assertEquals("old-version", repo.cachedFlight("one")!!.rowVersion)
        f.readDetail = { id -> f.detail(id).copy(rowVersion = "new-version") }
        f.prepare = { detail -> if (detail.id == "one") throw IOException("Media unavailable") }
        assertTrue(runCatching { repo.refreshKnownAndPrepare() }.isFailure)
        assertEquals("old-version", repo.cachedFlight("one")!!.rowVersion)
        f.prepare = {}
        repo.refreshKnownAndPrepare()
        assertEquals("new-version", repo.cachedFlight("one")!!.rowVersion)
    }

    @Test fun sourceHintsIgnoreFreshnessOnlyRefreshesButStillReportChangedCards() = runBlocking {
        val f = Fixture()
        val repo = f.repository()
        var hints = 0
        val collector = launch(start = CoroutineStart.UNDISPATCHED) { repo.changes.collect { hints++ } }
        try {
            repeat(20) { yield() }
            assertEquals(0, hints)
            repo.page(FlightDiscoverySource.Search, "SV123")
            repeat(20) { yield() }
            assertEquals(1, hints)
            f.time = Instant.parse(f.time).plusSeconds(60).toString()
            repo.page(FlightDiscoverySource.Search, "SV123")
            repeat(20) { yield() }
            assertEquals(1, hints)
            f.fetch = { _, _, number, size -> f.page(listOf(f.row("one").copy(customerName = "Changed")), number, size) }
            repo.page(FlightDiscoverySource.Search, "SV123")
            repeat(20) { yield() }
            assertEquals(2, hints)
        } finally { collector.cancel() }
    }

    @Test fun partialWriteAcknowledgmentsRestrictOnlyUnassignedSearchCreationCapabilities() = runBlocking {
        for (kind in listOf("arrival", "service", "task", "cancellation")) {
            val f = Fixture()
            val repo = f.repository()
            repo.confirmStation(f.station)
            val employee = AssignedEmployee("staff", "Employee", "E1")
            for (category in listOf("search", "assigned", "perLanding", "adHoc")) {
                val id = "$kind-$category"
                repo.recordLocalSave(f.detail(id).copy(isUnassignedSearchFlight = category == "search",
                    isPerLanding = category == "perLanding", isAdHocFlight = category == "adHoc",
                    canSaveServices = true, canSaveTasks = true, canAddServices = true, canAddTasks = true))
                when (kind) {
                    "arrival" -> repo.recordArrival(id, FlightArrivalSaved(FlightArrivalSubmission("arrival", employee,
                        f.time, "aircraft", "Aircraft", "TAIL", receivedByEmployee = employee, isAccepted = true, createdAtUtc = f.time), "ack"))
                    "service" -> repo.recordService(id, FlightServiceSaved(FlightServiceRecord("service", "service-type", "Service",
                        f.time, createdByUserId = "user", createdAtUtc = f.time), "ack"))
                    "task" -> repo.recordTask(id, FlightTaskSaved(FlightTaskRecord("task", 0, f.time,
                        createdByUserId = "user", createdAtUtc = f.time), "ack"))
                    else -> repo.recordCancellation(id, FlightCancellationSaved(FlightCancellationSubmission("cancel", employee,
                        f.time, isAccepted = true, createdAtUtc = f.time), "ack"))
                }
                val restarted = f.repository().cachedFlight(id)!!
                assertEquals("$kind/$category", category != "search", restarted.canAddServices)
                assertEquals("$kind/$category", category != "search", restarted.canAddTasks)
                assertTrue(restarted.canSaveServices && restarted.canSaveTasks)
                assertEquals("ack", restarted.rowVersion)
            }
        }
    }

    private class Fixture {
        val firstOwner = "api|account"
        val owner = MutableStateFlow<String?>(firstOwner)
        val station = "station"
        val dao = MemoryDiscoveryDao()
        var online = true
        var time = "2026-11-13T10:00:00Z"
        var pending = false
        var trustedStation: String? = station
        val prepared = mutableListOf<String>()
        var fetch: suspend (FlightDiscoverySource, String, Int, Int) -> FlightPage = { _, _, number, size -> page(listOf(row("one")), number, size) }
        var readDetail: suspend (String) -> FlightDetail = { detail(it) }
        var prepare: suspend (FlightDetail) -> Unit = {}
        fun row(id: String) = FlightListItem(id, "SV123", customerName = "Airline", stationIataCode = "AAA",
            operationTypeName = "Transit", scheduledArrivalUtc = "2026-11-13T09:00:00Z", scheduledDepartureUtc = "2026-11-13T11:00:00Z",
            isPerLanding = false, stationId = station, stationTimeZoneId = "Etc/UTC")
        fun detail(id: String) = FlightDetail(id, "SV123", "customer", customerName = "Airline", stationId = station,
            stationIata = "AAA", stationName = "Station", operationTypeId = "scheduled", operationTypeName = "Transit",
            scheduledArrivalUtc = "2026-11-13T09:00:00Z", scheduledDepartureUtc = "2026-11-13T11:00:00Z", isPerLanding = false,
            createdAtUtc = "2026-11-13T08:00:00Z", rowVersion = "old-version", stationTimeZoneId = "Etc/UTC")
        fun page(items: List<FlightListItem>, number: Int = 1, size: Int = 20, total: Long = items.size.toLong(), hasNext: Boolean = false) =
            FlightPage(items, number, size, total, if (hasNext) number + 1 else number,
                number > 1, hasNext, stationId = station, asOfUtc = time,
                windowStartUtc = Instant.parse(time).minusSeconds(43200).toString(), windowEndUtc = Instant.parse(time).plusSeconds(43200).toString())
        private suspend fun transaction(block: suspend () -> Unit) {
            val queries = dao.queryRows.value; val details = dao.detailRows.toMap()
            val stations = dao.stationRows.toMap(); val interactions = dao.interactionRows.value
            try { block() } catch (failure: Throwable) {
                dao.queryRows.value = queries; dao.detailRows.clear(); dao.detailRows.putAll(details)
                dao.stationRows.clear(); dao.stationRows.putAll(stations); dao.interactionRows.value = interactions
                throw failure
            }
        }
        fun repository() = DiscoveryFlightsRepository(dao, owner, { owner.value }, { online },
            { source, query, number, size -> fetch(source, query, number, size) }, { readDetail(it) },
            { prepared += it.id; prepare(it) }, { transaction(it) }, localStationAuthority = { it == trustedStation },
            hasPendingWork = { pending }, now = { time })
    }
}

private class MemoryDiscoveryDao : FlightDiscoveryDao {
    val queryRows = MutableStateFlow<List<FlightDiscoveryQueryEntity>>(emptyList())
    val detailRows = linkedMapOf<Pair<String, String>, FlightDiscoveryDetailEntity>()
    val stationRows = linkedMapOf<String, FlightDiscoveryStationEntity>()
    val interactionRows = MutableStateFlow<List<FlightLocalInteractionEntity>>(emptyList())
    override suspend fun queries(owner: String) = queryRows.value.filter { it.ownerSubject == owner }
    override fun observeQueries(owner: String) = queryRows.map { rows -> rows.filter { it.ownerSubject == owner } }
    override suspend fun putQuery(row: FlightDiscoveryQueryEntity) { queryRows.value = queryRows.value.filterNot { it.ownerSubject == row.ownerSubject && it.source == row.source && it.query == row.query } + row }
    override suspend fun deleteQueries(owner: String) { queryRows.value = queryRows.value.filterNot { it.ownerSubject == owner } }
    override suspend fun detail(owner: String, id: String) = detailRows[owner to id]
    override suspend fun details(owner: String) = detailRows.values.filter { it.ownerSubject == owner }
    override suspend fun putDetail(row: FlightDiscoveryDetailEntity) { detailRows[row.ownerSubject to row.flightId] = row }
    override suspend fun deleteDetail(owner: String, id: String) { detailRows.remove(owner to id); Unit }
    override suspend fun deleteDetails(owner: String) { detailRows.entries.removeAll { it.key.first == owner } }
    override suspend fun station(owner: String) = stationRows[owner]
    override suspend fun putStation(row: FlightDiscoveryStationEntity) { stationRows[row.ownerSubject] = row }
    override suspend fun deleteStation(owner: String) { stationRows.remove(owner); Unit }
    override suspend fun interactions(owner: String) = interactionRows.value.filter { it.ownerSubject == owner }
    override fun observeInteractions(owner: String) = interactionRows.map { rows -> rows.filter { it.ownerSubject == owner } }
    override suspend fun putInteraction(row: FlightLocalInteractionEntity) { interactionRows.value = interactionRows.value.filterNot { it.ownerSubject == row.ownerSubject && it.flightId == row.flightId } + row }
    override suspend fun deleteInteraction(owner: String, id: String) { interactionRows.value = interactionRows.value.filterNot { it.ownerSubject == owner && it.flightId == id } }
    override suspend fun deleteInteractions(owner: String) { interactionRows.value = interactionRows.value.filterNot { it.ownerSubject == owner } }
    override suspend fun clearQueries() { queryRows.value = emptyList() }
    override suspend fun clearDetails() { detailRows.clear() }
    override suspend fun clearStations() { stationRows.clear() }
    override suspend fun clearInteractions() { interactionRows.value = emptyList() }
}
