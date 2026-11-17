package com.nags.operations.data.repo

import com.nags.operations.data.*
import java.io.IOException
import java.time.Instant
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.async
import kotlinx.coroutines.runBlocking
import org.junit.Assert.*
import org.junit.Test

class SharedFlightsRepositoryTest {
    private val initial = "2026-11-16T12:00:00Z"
    private var clock = initial
    private var owner: String? = "https://api.example/|account-a"
    private var station: String? = "station-a"
    private var online = true
    private var stationReader: suspend () -> String? = { station }
    private val store = MemorySharesStore()
    private var responses: suspend (FlightShareDirection, String, Int, Int) -> FlightSharePage = { _, _, number, size -> page(listOf(post("one")), number, size) }
    private var publish: suspend (String, String, PublishFlightShareRequest) -> FlightSharePost = { _, id, input ->
        post(id).copy(message = input.message, isStationWide = input.staffMemberIds.isEmpty(),
            recipients = input.staffMemberIds.map { AssignedEmployee(it, "Recipient", "E2") })
    }
    private var cached: FlightDetail? = null
    private fun repository() = SharedFlightsRepository(store, { owner }, { stationReader() }, { online },
        { direction, search, number, size -> responses(direction, search, number, size) },
        { flight, id, input -> publish(flight, id, input) }, cachedFlight = { cached }, now = { clock })

    @Test fun combinedFeedFiltersOfflineWithoutChangingFlightMembership() = runBlocking {
        responses = { direction, _, number, size -> page(listOf(post(direction.key)), number, size) }
        val repo = repository(); repo.refreshAll(); online = false
        assertEquals("all", repo.page(FlightShareDirection.All).items.single().id)
        assertTrue(repo.page(FlightShareDirection.All).fromCache)
        assertEquals(1, repo.page(FlightShareDirection.All, search = "sv-100").totalCount)
        assertEquals(0, repo.page(FlightShareDirection.All, search = "unmatched").totalCount)
        assertFalse(repo.page(FlightShareDirection.All).items.single().flight.isMyFlight)
    }

    @Test fun incompleteAndInterruptedPagesNeverReplaceLastCompleteOfflineFeed() = runBlocking {
        val repo = repository(); repo.page(FlightShareDirection.All)
        responses = { _, _, number, size ->
            if (number == 2) throw IOException("Disconnected")
            page((1..20).map { post("new-$it") }, number, size, total = 21)
        }
        repo.page(FlightShareDirection.All)
        assertEquals("one", repo.cachedPage(FlightShareDirection.All)!!.items.single().id)
        val fallback = repo.page(FlightShareDirection.All, page = 2)
        assertTrue(fallback.fromCache); assertTrue(fallback.items.isEmpty())
        assertTrue(runCatching { repo.refreshAll() }.isFailure)
        online = false
        assertEquals("one", repo.page(FlightShareDirection.All).items.single().id)
    }

    @Test fun successfulPagingPublishesEveryPostEvenWhenSeveralReferToTheSameFlight() = runBlocking {
        responses = { _, _, number, size ->
            val all = (1..21).map { post("post-$it") }
            page(all.drop((number - 1) * size).take(size), number, size, total = all.size.toLong())
        }
        val repo = repository(); repo.page(FlightShareDirection.All); repo.page(FlightShareDirection.All, page = 2)
        online = false
        assertEquals(21, repo.page(FlightShareDirection.All).totalCount)
        assertEquals("post-21", repo.page(FlightShareDirection.All, page = 2).items.single().id)
    }

    @Test fun accountAndApiKeysCannotReadAnotherOwnersFeed() = runBlocking {
        val repo = repository(); repo.refreshAll(); online = false
        owner = "https://api.example/|account-b"
        assertNull(repo.cachedPage(FlightShareDirection.All))
        owner = "https://other-api.example/|account-a"
        assertNull(repo.cachedPage(FlightShareDirection.All))
    }

    @Test fun newlyConfirmedStationRemovesOldFeedEvenIfTheNewDownloadIsIncomplete() = runBlocking {
        val repo = repository(); repo.refreshAll()
        station = "station-b"; repo.confirmStation("station-b")
        assertNull(repo.cachedPage(FlightShareDirection.All))
        station = "station-a"
        assertNull(repo.cachedPage(FlightShareDirection.All))
    }

    @Test fun authorizationLossClearsCombinedFeedAndInvalidatesVisibleSession() = runBlocking {
        val repo = repository(); repo.refreshAll()
        val prior = repo.cachedPage(FlightShareDirection.All)!!
        responses = { _, _, _, _ -> throw ApiException(403, "Forbidden") }
        assertTrue(runCatching { repo.page(FlightShareDirection.All) }.isFailure)
        assertNull(repo.cachedPage(FlightShareDirection.All))
        assertFalse(repo.isCurrentScope(prior.stationId, prior.ownerSessionKey))
        assertTrue(repo.reconcileVisible(prior.items, prior.ownerSessionKey).isEmpty())
    }

    @Test fun aLateResponseCannotRestoreDataAfterLogoutAndLoginToTheSameAccount() = runBlocking {
        val started = CompletableDeferred<Unit>(); val result = CompletableDeferred<FlightSharePage>()
        responses = { _, _, _, _ -> started.complete(Unit); result.await() }
        val repo = repository()
        val read = async { runCatching { repo.page(FlightShareDirection.All) } }
        started.await(); repo.clear(); result.complete(page(listOf(post("late"))))
        assertTrue(read.await().isFailure)
        assertNull(repo.cachedPage(FlightShareDirection.All))
    }

    @Test fun freshServerTerminalEvidenceWinsOverLocalGroundEvidenceRecordedBeforeItsRead() = runBlocking {
        val repo = repository(); repo.recordAcknowledged(detail())
        val departed = post("one").flight.withVisibilityReports(detail().copy(departureSubmissions = listOf(departure(initial))))
        responses = { _, _, number, size -> page(listOf(post("one").copy(flight = departed)), number, size) }
        repo.page(FlightShareDirection.All); online = false
        clock = Instant.parse(initial).plusSeconds(43201).toString()
        assertTrue(repo.page(FlightShareDirection.All).items.isEmpty())
    }

    @Test fun anInFlightOldPageCannotUndoANewerAcknowledgedDeparture() = runBlocking {
        val started = CompletableDeferred<Unit>(); val result = CompletableDeferred<FlightSharePage>()
        responses = { _, _, _, _ -> started.complete(Unit); result.await() }
        val repo = repository()
        val read = async { repo.page(FlightShareDirection.All) }
        started.await()
        repo.recordAcknowledged(detail().copy(departureSubmissions = listOf(departure("2026-11-15T22:00:00Z"))))
        result.complete(page(listOf(post("stale"))))
        assertTrue(read.await().items.isEmpty())
        online = false
        assertTrue(repo.page(FlightShareDirection.All).items.isEmpty())
    }

    @Test fun aFreshConfirmedReadCanRestoreAFormerlyDeniedFlightWithoutBeingMaskedByAnOldTombstone() = runBlocking {
        val repo = repository(); repo.revokeForOwner(owner!!, "flight")
        assertEquals("one", repo.page(FlightShareDirection.All).items.single().id)
    }

    @Test fun postsFollowLifecycleExpiryAndCategoryChangesUsingCurrentDetailEvidence() = runBlocking {
        val repo = repository(); repo.refreshAll(); online = false
        clock = "2026-12-16T12:00:00Z"
        assertEquals(1, repo.page(FlightShareDirection.All).items.size)
        repo.recordAcknowledged(detail().copy(isPerLanding = true))
        assertTrue(repo.page(FlightShareDirection.All).items.isEmpty())
        repo.recordAcknowledged(detail().copy(departureSubmissions = listOf(departure("2026-12-15T23:00:00Z"))))
        assertTrue(repo.page(FlightShareDirection.All).items.isEmpty())
    }

    @Test fun partialAcknowledgmentWithoutCompleteDetailCannotLeaveAnOldActivePostVisible() = runBlocking {
        val repo = repository(); repo.refreshAll(); cached = null
        repo.recordPartialAcknowledged("flight"); online = false
        assertTrue(repo.page(FlightShareDirection.All).items.isEmpty())
    }

    @Test fun publishingRequiresConnectionAndKeepsStableIdentityAndNormalizedBodyForRetry() = runBlocking {
        val repo = repository(); var calls = 0
        publish = { flightId, id, body ->
            assertEquals("flight", flightId); assertEquals("stable", id)
            assertEquals("Message", body.message); assertEquals(listOf("a", "b"), body.staffMemberIds)
            calls++; post(id)
        }
        repo.publish(detail(), "stable", "  Message  ", listOf("b", "a", "a"))
        repo.publish(detail(), "stable", "Message", listOf("a", "b"))
        assertEquals(2, calls)
        online = false
        assertTrue(runCatching { repo.publish(detail(), "stable", null, emptyList()) }.isFailure)
        assertEquals(2, calls)
    }

    @Test fun latePublishCannotWriteOrReturnIntoAClearedSession() = runBlocking {
        val started = CompletableDeferred<Unit>(); val result = CompletableDeferred<FlightSharePost>()
        val repo = repository(); repo.refreshAll()
        publish = { _, _, _ -> started.complete(Unit); result.await() }
        val pending = async { runCatching { repo.publish(detail(), "post", null, emptyList()) } }
        started.await(); repo.clear(); result.complete(post("post"))
        assertTrue(pending.await().isFailure)
        assertNull(repo.cachedPage(FlightShareDirection.All))
    }

    @Test fun publishingRechecksSessionInsideCommitAfterAnAwaitingStationCheck() = runBlocking {
        val waiting = CompletableDeferred<Unit>(); val release = CompletableDeferred<Unit>()
        var block = false
        stationReader = {
            if (block) { block = false; waiting.complete(Unit); release.await() }
            station
        }
        publish = { _, id, _ -> block = true; post(id) }
        val repo = repository()
        val attempt = async { runCatching { repo.publish(detail(), "post", null, emptyList()) } }
        waiting.await(); repo.clear(); release.complete(Unit)
        assertTrue(attempt.await().isFailure)
        assertNull(repo.cachedPage(FlightShareDirection.All))
    }

    @Test fun openingCombinedFeedDuringFullSyncDoesNotCancelItAndNewerCompleteSnapshotWins() = runBlocking {
        val started = CompletableDeferred<Unit>(); val oldResponse = CompletableDeferred<FlightSharePage>()
        responses = { direction, _, number, size ->
            if (direction == FlightShareDirection.All && size == 100) {
                started.complete(Unit); oldResponse.await()
            } else page(listOf(post("newer")), number, size)
        }
        val repo = repository()
        val background = async { runCatching { repo.refreshAll() } }
        started.await()
        assertEquals("newer", repo.page(FlightShareDirection.All).items.single().id)
        oldResponse.complete(page(listOf(post("older")), size = 100))
        assertTrue(background.await().isSuccess)
        online = false
        assertEquals("newer", repo.page(FlightShareDirection.All).items.single().id)
    }

    @Test fun fullSyncRequestsOnlyTheCombinedFeedAndPaginatesItGlobally() = runBlocking {
        val requests = mutableListOf<FlightShareDirection>()
        responses = { direction, _, number, size ->
            requests += direction
            val rows = (1..101).map { post("post-$it") }
            page(rows.drop((number - 1) * size).take(size), number, size, total = rows.size.toLong())
        }
        val repo = repository(); repo.refreshAll(); online = false
        assertEquals(listOf(FlightShareDirection.All, FlightShareDirection.All), requests)
        assertEquals(101, repo.page(FlightShareDirection.All).totalCount)
        assertEquals("post-101", repo.page(FlightShareDirection.All, page = 6).items.single().id)
    }

    @Test fun offlineUpgradeMergesOnlyBothCompleteLegacyFeedsInPublicationOrder() = runBlocking {
        responses = { direction, _, number, size ->
            val items = if (direction == FlightShareDirection.Sent)
                listOf(post("sent-later"), post("overlap").copy(createdAtUtc = "2026-11-16T11:59:00Z"))
            else listOf(post("received").copy(createdAtUtc = "2026-11-16T11:58:00Z"), post("overlap").copy(createdAtUtc = "2026-11-16T11:59:00Z"))
            page(items, number, size)
        }
        val repo = repository()
        repo.page(FlightShareDirection.Received); repo.page(FlightShareDirection.Sent)
        online = false
        val combined = repo.page(FlightShareDirection.All)
        assertEquals(listOf("sent-later", "overlap", "received"), combined.items.map { it.id })
        assertEquals(3, combined.totalCount)
        assertTrue(combined.fromCache)
        owner = "https://api.example/|different-user"
        assertNull(repo.cachedPage(FlightShareDirection.All))
    }

    @Test fun offlineUpgradeNeverTreatsASingleLegacyFeedOrPartialPagesAsACompleteCombinedFeed() = runBlocking {
        val repo = repository(); repo.page(FlightShareDirection.Received)
        assertNull(repo.cachedPage(FlightShareDirection.All))
        responses = { _, _, number, size -> page((1..20).map { post("sent-$it") }, number, size, total = 21) }
        repo.page(FlightShareDirection.Sent)
        online = false
        assertNull(repo.cachedPage(FlightShareDirection.All))
    }

    @Test fun publishingAddsThePostToAnExistingCombinedOfflineSnapshot() = runBlocking {
        val repo = repository(); repo.refreshAll()
        repo.publish(detail(), "new-post", "New note", emptyList())
        online = false
        assertEquals(setOf("one", "new-post"), repo.page(FlightShareDirection.All).items.map { it.id }.toSet())
    }

    @Test fun malformedOrCrossStationPagesCannotBecomeOfflineSnapshots() = runBlocking {
        val repo = repository()
        responses = { _, _, number, size -> page(listOf(post("one").copy(flight = post("one").flight.copy(stationId = "other"))), number, size) }
        assertTrue(runCatching { repo.page(FlightShareDirection.All) }.isFailure)
        assertNull(repo.cachedPage(FlightShareDirection.All))
    }

    private fun page(items: List<FlightSharePost>, number: Int = 1, size: Int = 20, total: Long = items.size.toLong()): FlightSharePage {
        val at = Instant.parse(clock); val count = if (total == 0L) 0 else ((total - 1) / size + 1).toInt()
        return FlightSharePage(items, number, size, total, count, number > 1, number < count,
            "station-a", clock, at.minusSeconds(43200).toString(), at.plusSeconds(43200).toString())
    }
    private fun post(id: String) = FlightSharePost(id, "flight", "sender", "Sender", "E1", createdAtUtc = initial,
        isStationWide = true, flight = detail().let { value -> FlightListItem(value.id, value.flightNumber,
            customerName = value.customerName, stationIataCode = value.stationIata, operationTypeName = value.operationTypeName,
            scheduledArrivalUtc = value.scheduledArrivalUtc, scheduledDepartureUtc = value.scheduledDepartureUtc,
            isPerLanding = false, stationId = value.stationId, stationTimeZoneId = value.stationTimeZoneId).withVisibilityReports(value) })
    private fun detail() = FlightDetail("flight", "SV100", "customer", customerName = "Airline", stationId = "station-a",
        stationIata = "JED", stationName = "Jeddah", operationTypeId = "scheduled", operationTypeName = "Transit",
        scheduledArrivalUtc = "2026-11-01T10:00:00Z", scheduledDepartureUtc = "2026-11-01T11:00:00Z", isPerLanding = false,
        createdAtUtc = initial, rowVersion = "version", stationTimeZoneId = "Asia/Riyadh", canShare = true)
    private fun departure(at: String) = FlightDepartureSubmission("departure", AssignedEmployee("staff", "Employee", "E1"), at,
        AssignedEmployee("staff", "Employee", "E1"), isAccepted = true, createdAtUtc = initial)
}

private class MemorySharesStore : SharedFlightsStore {
    private val rows = mutableMapOf<Pair<String, FlightShareDirection>, String>()
    override fun read(owner: String, direction: FlightShareDirection) = rows[owner to direction]
    override fun write(owner: String, direction: FlightShareDirection, value: String) { rows[owner to direction] = value }
    override fun remove(owner: String, direction: FlightShareDirection) { rows.remove(owner to direction) }
    override fun clearOwner(owner: String) { rows.keys.filter { it.first == owner }.forEach(rows::remove) }
    override fun clearAll() { rows.clear() }
}
