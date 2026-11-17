package com.nags.operations.ui.flights

import com.nags.operations.data.*
import java.time.Instant
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.runBlocking
import org.junit.After
import org.junit.Assert.*
import org.junit.Test

class SharedFlightsViewModelTest {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Unconfined)
    private val at = Instant.parse("2026-11-16T12:00:00Z")
    @After fun close() = scope.cancel()

    @Test fun repeatedSharesOfTheSameFlightKeepSeparatePostIdentitiesAcrossPages() {
        val requests = mutableListOf<Int>()
        val vm = model(fetch = { _, _, number, _ ->
            requests += number
            page("post-$number").copy(page = number, totalCount = 2, hasNextPage = number == 1)
        })
        vm.refresh(); vm.loadMore(); vm.loadMore()
        assertEquals(listOf(1, 2), requests)
        assertEquals(listOf("post-1", "post-2"), vm.state.value.items.map { it.id })
        assertEquals(1, vm.state.value.items.map { it.flightId }.distinct().size)
    }

    @Test fun refreshingCancelsAnOlderCombinedFeedBeforeItCanReplaceNewPosts() {
        val earlier = CompletableDeferred<FlightSharePage>()
        var reads = 0
        val vm = model(fetch = { direction, query, _, _ ->
            assertEquals(FlightShareDirection.All, direction)
            assertEquals("", query)
            if (++reads == 1) earlier.await() else page("newer")
        })
        vm.refresh(); vm.refresh()
        earlier.complete(page("older"))
        assertEquals(listOf("newer"), vm.state.value.items.map { it.id })
    }

    @Test fun incompleteOfflineManifestDoesNotEraseLivePartialFeedOnDetailChanges() = runBlocking {
        val changes = MutableSharedFlow<Unit>(extraBufferCapacity = 1)
        val vm = model(fetch = { _, _, _, _ -> page("live").copy(totalCount = 40, hasNextPage = true) },
            changes = changes, scopeCurrent = { _, _ -> true })
        vm.refresh()
        changes.emit(Unit)
        assertEquals(listOf("live"), vm.state.value.items.map { it.id })
        assertEquals(40, vm.state.value.totalCount.toInt())
        assertTrue(vm.state.value.hasMore)
    }

    @Test fun confirmedScopeInvalidationRemovesLiveRowsEvenWithoutAnOfflineManifest() = runBlocking {
        val changes = MutableSharedFlow<Unit>(extraBufferCapacity = 1)
        val vm = model(changes = changes, scopeCurrent = { _, _ -> false })
        vm.refresh(); changes.emit(Unit)
        assertTrue(vm.state.value.items.isEmpty())
        assertFalse(vm.state.value.hasMore)
        assertEquals(0L, vm.state.value.totalCount)
    }

    @Test fun livePartialFeedReconcilesExpiredActualEvidenceWithoutNetworkFetch() = runBlocking {
        val changes = MutableSharedFlow<Unit>(extraBufferCapacity = 1)
        var reads = 0
        val vm = model(fetch = { _, _, _, _ -> reads++; page("live") }, changes = changes,
            scopeCurrent = { _, _ -> true }, reconcile = { _, _ -> emptyList() })
        vm.refresh(); changes.emit(Unit)
        assertEquals(1, reads)
        assertTrue(vm.state.value.items.isEmpty())
    }

    @Test fun combinedFeedRequestsAllPostsWithoutAQueryAcrossPages() {
        val requests = mutableListOf<Pair<FlightShareDirection, String>>()
        val vm = model(fetch = { direction, query, number, _ ->
            requests += direction to query
            page("post-$number").copy(page = number, totalCount = 2, hasNextPage = number == 1)
        })
        vm.refresh(); vm.loadMore()
        assertEquals(listOf(FlightShareDirection.All to "", FlightShareDirection.All to ""), requests)
    }

    @Test fun accessFailureClearsPostsWhileOrdinaryFailureRetainsTheLastPage() {
        var error: Exception? = null
        val vm = model(fetch = { _, _, _, _ -> error?.let { throw it }; page("one") })
        vm.refresh(); error = java.io.IOException("Offline"); vm.refresh()
        assertEquals(1, vm.state.value.items.size)
        error = ApiException(403, "Not allowed"); vm.refresh()
        assertTrue(vm.state.value.items.isEmpty())
    }

    @Test fun publishingAttemptNormalizesOnlyOnceAndDoesNotReuseIdentityForAnIntentionalSecondPost() {
        val one = FlightShareAttempt.create("first", "  Help with departure  ", listOf("B", "A", "B"))
        assertEquals("Help with departure", one.message)
        assertEquals(listOf("A", "B"), one.staffMemberIds)
        val retry = kotlinx.serialization.json.Json.decodeFromString<FlightShareAttempt>(
            kotlinx.serialization.json.Json.encodeToString(FlightShareAttempt.serializer(), one))
        assertEquals(one, retry)
        assertNotEquals(one.id, FlightShareAttempt.create("next", "Help with departure", listOf("A", "B")).id)
        assertNull(FlightShareAttempt.create("public", "  ", emptyList()).message)
    }

    @Test fun shareActionRespectsServerCapabilityAndRecordedOrPendingCancellation() {
        val flight = FlightDetail("flight", "SV100", "customer", customerName = "Customer", stationId = "station",
            stationIata = "RUH", stationName = "Riyadh", operationTypeId = "operation", operationTypeName = "Transit",
            scheduledArrivalUtc = at.toString(), scheduledDepartureUtc = at.plusSeconds(3600).toString(),
            isPerLanding = false, createdAtUtc = at.toString(), rowVersion = "version", stationTimeZoneId = "Asia/Riyadh", canShare = true)
        assertTrue(flight.canShowShareAction())
        assertFalse(flight.copy(canShare = false).canShowShareAction())
        assertFalse(flight.copy(hasCancellationSubmission = true).canShowShareAction())
        assertFalse(flight.canShowShareAction(hasPendingCancellation = true))
    }

    private fun model(fetch: suspend (FlightShareDirection, String, Int, Int) -> FlightSharePage = { _, _, _, _ -> page("one") },
        changes: kotlinx.coroutines.flow.Flow<Unit> = kotlinx.coroutines.flow.emptyFlow(),
        scopeCurrent: suspend (String, String?) -> Boolean = { _, _ -> true },
        reconcile: suspend (List<FlightSharePost>, String?) -> List<FlightSharePost> = { rows, _ -> rows },
    ) = SharedFlightsViewModel(fetch, { _, _, _, _ -> null }, changes = changes, scope = scope, now = { at },
        scopeCurrent = scopeCurrent, reconcile = reconcile)

    private fun page(id: String) = FlightSharePage(listOf(post(id)), totalCount = 1, stationId = "station",
        asOfUtc = at.toString(), windowStartUtc = at.minusSeconds(43200).toString(), windowEndUtc = at.plusSeconds(43200).toString(),
        ownerSessionKey = "session")
    private fun post(id: String) = FlightSharePost(id, "flight", "staff", "Employee", "E1", createdAtUtc = at.toString(),
        isStationWide = true, flight = FlightListItem("flight", "SV100", customerName = "Customer", stationIataCode = "RUH",
            operationTypeName = "Transit", scheduledArrivalUtc = at.toString(), scheduledDepartureUtc = at.plusSeconds(3600).toString(),
            isPerLanding = false, stationId = "station", stationTimeZoneId = "Asia/Riyadh", hasArrivalSubmission = false,
            hasDepartureSubmission = false, hasCancellationSubmission = false, hasOpenReturnToRamp = false, visibilityPolicyVersion = 1))
}
