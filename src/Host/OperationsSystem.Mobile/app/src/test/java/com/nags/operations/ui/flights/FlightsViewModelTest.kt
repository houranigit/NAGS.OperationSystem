package com.nags.operations.ui.flights

import com.nags.operations.data.ApiException
import com.nags.operations.data.AssignedEmployee
import com.nags.operations.data.AuthenticatedUser
import com.nags.operations.data.FlightCategory
import com.nags.operations.data.FlightDetail
import com.nags.operations.data.FlightListItem
import com.nags.operations.data.FlightPage
import com.nags.operations.data.FlightSyncStatus
import java.io.IOException
import java.time.Instant
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.delay
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.emptyFlow
import kotlinx.coroutines.flow.flowOf
import org.junit.After
import org.junit.Assert.*
import org.junit.Test

class FlightsViewModelTest {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Unconfined)

    @After
    fun tearDown() = scope.cancel()

    @Test
    fun missingViewPermissionDoesNotReadFlightsOrDetails() {
        var reads = 0
        var clears = 0
        val vm = model(permissions = emptyList(), list = { _, _, _ -> reads++; page("one") },
            readDetail = { reads++; detail(it) }, clearCache = { clears++ })
        vm.refresh()
        vm.openFlight("one")
        assertEquals(0, reads)
        assertEquals(2, clears)
        assertTrue(vm.state.value.permissionDenied)
        assertNotNull(vm.state.value.detailError)
    }

    @Test
    fun searchAndLoadMoreUseTheServerPageAndKeepTheQuery() {
        val requests = mutableListOf<Pair<Int, String?>>()
        val vm = model(list = { number, _, search ->
            requests += number to search
            FlightPage(items = listOf(row("$number")), page = number, totalCount = 2,
                hasNextPage = number == 1)
        })
        vm.openSearch()
        vm.setSearch(" 123 ")
        vm.refresh()
        vm.loadMore()
        assertEquals(listOf(1 to "123", 2 to "123"), requests)
        assertEquals(listOf("1", "2"), vm.state.value.items.map { it.id })
        assertEquals(2L, vm.state.value.totalCount)
        assertFalse(vm.state.value.hasMore)
    }

    @Test
    fun repeatedLoadMoreOnlyRequestsOnceUntilThePageCompletes() {
        val nextPage = CompletableDeferred<FlightPage>()
        val requests = mutableListOf<Int>()
        val vm = model(list = { number, _, _ ->
            requests += number
            if (number == 1) page("one").copy(totalCount = 2, hasNextPage = true) else nextPage.await()
        })
        vm.refresh()
        vm.loadMore()
        vm.loadMore()
        assertEquals(listOf(1, 2), requests)
        assertTrue(vm.state.value.isLoadingMore)

        nextPage.complete(page("two").copy(page = 2, totalCount = 2))
        vm.loadMore()
        assertEquals(listOf(1, 2), requests)
        assertEquals(listOf("one", "two"), vm.state.value.items.map { it.id })
        assertFalse(vm.state.value.isLoadingMore)
        assertFalse(vm.state.value.hasMore)
    }

    @Test
    fun failedNextPageKeepsVisibleRowsAndRetriesTheSamePage() {
        var fail = true
        val requests = mutableListOf<Int>()
        val vm = model(list = { number, _, _ ->
            requests += number
            if (number == 1) page("one").copy(totalCount = 2, hasNextPage = true)
            else if (fail) throw IOException("Connection lost")
            else FlightPage(items = listOf(row("one"), row("two")), page = 2, totalCount = 2)
        })
        vm.refresh()
        vm.loadMore()
        assertEquals(listOf("one"), vm.state.value.items.map { it.id })
        assertEquals(1, vm.state.value.page)
        assertTrue(vm.state.value.hasMore)
        assertNotNull(vm.state.value.loadMoreError)
        assertNull(vm.state.value.error)
        assertFalse(vm.state.value.isLoadingMore)

        fail = false
        vm.loadMore()
        assertEquals(listOf(1, 2, 2), requests)
        assertEquals(listOf("one", "two"), vm.state.value.items.map { it.id })
        assertNull(vm.state.value.loadMoreError)
        assertFalse(vm.state.value.hasMore)
    }

    @Test
    fun aNewSearchCancelsAnInFlightNextPage() {
        val nextPage = CompletableDeferred<FlightPage>()
        val vm = model(list = { number, _, search ->
            when {
                search != null -> page("new")
                number == 1 -> page("one").copy(totalCount = 2, hasNextPage = true)
                else -> nextPage.await()
            }
        })
        vm.refresh()
        vm.loadMore()
        vm.openSearch()
        vm.setSearch("new")
        vm.refresh()
        nextPage.complete(page("old").copy(page = 2, totalCount = 2))
        assertEquals(listOf("new"), vm.state.value.items.map { it.id })
        assertFalse(vm.state.value.isLoadingMore)
        assertNull(vm.state.value.loadMoreError)
    }

    @Test
    fun aPreviousSearchResponseCannotReplaceTheNewSearch() {
        val firstResponse = CompletableDeferred<FlightPage>()
        val vm = model(list = { _, _, search ->
            if (search == null) firstResponse.await() else page("new")
        })
        vm.refresh()
        vm.openSearch()
        vm.setSearch("new")
        vm.refresh()
        firstResponse.complete(page("old"))
        assertEquals(listOf("new"), vm.state.value.items.map { it.id })
        assertFalse(vm.state.value.isRefreshing)
    }

    @Test
    fun failedRefreshKeepsTheVisibleListAndAllowsRetry() {
        var fail = false
        val vm = model(list = { _, _, _ -> if (fail) throw IOException() else page("one") })
        vm.refresh()
        fail = true
        vm.refresh()
        assertEquals(listOf("one"), vm.state.value.items.map { it.id })
        assertNotNull(vm.state.value.error)
        assertFalse(vm.state.value.isRefreshing)
        fail = false
        vm.refresh()
        assertNull(vm.state.value.error)
    }

    @Test
    fun deniedDetailRemovesTheUnavailableRowWithoutDenyingTheWholeList() {
        val vm = model(readDetail = { throw ApiException(403, "Flight unavailable") })
        vm.refresh()
        vm.openFlight("one")
        assertNull(vm.state.value.detail)
        assertEquals("Flight unavailable", vm.state.value.detailError)
        assertTrue(vm.state.value.items.isEmpty())
        assertFalse(vm.state.value.permissionDenied)
    }

    @Test
    fun inviteIsUnavailableWithoutItsSeparatePermission() {
        var optionsReads = 0
        val vm = model(options = { optionsReads++; emptyList() })
        vm.openFlight("one")
        vm.openInvitations()
        assertFalse(vm.state.value.isInvitationOpen)
        assertEquals(0, optionsReads)
    }

    @Test
    fun aStaleInvitePreservesSelectionsAndRefreshesOptionsAndVersionBeforeRetry() {
        var currentDetail = detail("one")
        val first = AssignedEmployee("a", "First employee", "A")
        val second = AssignedEmployee("b", "Second employee", "B")
        var options = listOf(first, second)
        val sends = mutableListOf<Pair<List<String>, String>>()
        val vm = model(permissions = listOf("operations.flights.view", "operations.flights.invite"),
            readDetail = { currentDetail }, options = { options }, send = { _, selected, version ->
                sends += selected to version
                if (sends.size == 1) {
                    currentDetail = currentDetail.copy(rowVersion = "v2", assignedEmployees = listOf(first))
                    options = listOf(second)
                    throw ApiException(409, "Flight changed")
                }
                currentDetail = currentDetail.copy(rowVersion = "v3", assignedEmployees = listOf(first, second))
            })
        vm.openFlight("one")
        vm.openInvitations()
        vm.selectInvitation("a", true)
        vm.selectInvitation("b", true)
        vm.inviteSelectedEmployees()

        assertTrue(vm.state.value.isInvitationOpen)
        assertTrue(vm.state.value.invitationStale)
        assertEquals(setOf("a", "b"), vm.state.value.selectedInvitationIds)
        assertEquals(setOf("b"), vm.state.value.availableInvitationIds)
        assertEquals(setOf("a", "b"), vm.state.value.invitationOptions.map { it.staffMemberId }.toSet())
        assertEquals("v2", vm.state.value.detail!!.rowVersion)
        vm.inviteSelectedEmployees()
        assertEquals(1, sends.size)

        vm.selectInvitation("a", false)
        vm.inviteSelectedEmployees()
        assertEquals(listOf("b") to "v2", sends.last())
        assertFalse(vm.state.value.isInvitationOpen)
        assertTrue(vm.state.value.invitationSent)
        assertEquals(listOf(first, second), vm.state.value.detail!!.assignedEmployees)
    }

    @Test
    fun offlineSavedReadsSkipProfileNetworkAndCannotInvite() {
        var profileReads = 0
        var optionReads = 0
        var sends = 0
        val vm = model(online = MutableStateFlow(false),
            profile = { profileReads++; throw IOException("Offline") },
            list = { _, _, _ -> page("one").copy(fromCache = true) },
            readDetail = { detail(it).copy(fromCache = true) },
            options = { optionReads++; emptyList() }, send = { _, _, _ -> sends++ })
        vm.refresh()
        vm.openFlight("one")
        vm.openInvitations()
        vm.inviteSelectedEmployees()
        assertEquals(0, profileReads)
        assertTrue(vm.state.value.isUsingCachedList)
        assertTrue(vm.state.value.detail!!.fromCache)
        assertFalse(vm.state.value.isOnline)
        assertFalse(vm.state.value.isInvitationOpen)
        assertEquals(0, optionReads)
        assertEquals(0, sends)
    }

    @Test
    fun transientProfileFailureCanShowSavedFlightsButCannotGrantInvitationAuthority() {
        val vm = model(profile = { throw IOException("Server unavailable") },
            list = { _, _, _ -> page("one").copy(fromCache = true) })
        vm.refresh()
        assertEquals(listOf("one"), vm.state.value.items.map { it.id })
        assertTrue(vm.state.value.isUsingCachedList)
        assertFalse(vm.state.value.canInviteEmployees)
        assertNull(vm.state.value.error)
    }

    @Test
    fun forbiddenProfileEvictsKnownCachedAccessBeforeAnyFlightRead() {
        var reads = 0
        var clears = 0
        val vm = model(profile = { throw ApiException(403, "Forbidden") },
            list = { _, _, _ -> reads++; page("one") }, clearCache = { clears++ })
        vm.refresh()
        assertEquals(0, reads)
        assertEquals(1, clears)
        assertTrue(vm.state.value.permissionDenied)
    }

    @Test
    fun aCompletedFlightSyncReloadsTheListAndOpenDetailWithoutAnotherSyncLoop() {
        val changes = MutableSharedFlow<Unit>(extraBufferCapacity = 1)
        val cached = MutableStateFlow(FlightSyncStatus(itemCount = 1, lastSuccessUtc = "2026-11-04T10:00:00Z"))
        val refreshedDetail = CompletableDeferred<FlightDetail>()
        var listReads = 0
        var detailReads = 0
        val vm = model(changes = changes, cacheStatus = cached,
            list = { _, _, _ -> listReads++; page("one") },
            readDetail = { if (++detailReads == 1) detail(it) else refreshedDetail.await() })
        vm.refresh()
        vm.openFlight("one")
        val visible = vm.state.value.detail

        assertTrue(changes.tryEmit(Unit))

        assertEquals(2, listReads)
        assertEquals(2, detailReads)
        assertEquals("one", vm.state.value.selectedFlightId)
        assertSame(visible, vm.state.value.detail)
        assertTrue(vm.state.value.isLoadingDetail)
        assertNull(vm.state.value.detailError)
        assertFalse(vm.state.value.showRefreshIndicator)

        refreshedDetail.complete(detail("one").copy(flightNumber = "456", rowVersion = "v2"))

        assertEquals("456", vm.state.value.detail!!.flightNumber)
        assertEquals("v2", vm.state.value.detail!!.rowVersion)
        assertFalse(vm.state.value.isLoadingDetail)
        assertEquals(listOf("one"), vm.state.value.items.map { it.id })
        assertEquals(2, listReads)
        assertEquals(2, detailReads)
    }

    @Test
    fun aFailedAutomaticDetailRefreshKeepsVisibleContentAndCanRetry() {
        for (failure in listOf(IOException("Connection lost"), ApiException(503, "Server unavailable"))) {
            val changes = MutableSharedFlow<Unit>(extraBufferCapacity = 1)
            val cached = MutableStateFlow(FlightSyncStatus(itemCount = 1, lastSuccessUtc = "2026-11-04T10:00:00Z"))
            var fail = false
            val vm = model(changes = changes, cacheStatus = cached, readDetail = {
                if (fail) throw failure
                detail(it)
            })
            vm.refresh()
            vm.openFlight("one")
            val visible = vm.state.value.detail
            fail = true

            assertTrue(changes.tryEmit(Unit))

            assertSame(visible, vm.state.value.detail)
            assertEquals("one", vm.state.value.selectedFlightId)
            assertNotNull(vm.state.value.detailError)
            assertFalse(vm.state.value.isLoadingDetail)
            assertEquals(listOf("one"), vm.state.value.items.map { it.id })

            fail = false
            assertTrue(changes.tryEmit(Unit))

            assertNotNull(vm.state.value.detail)
            assertNull(vm.state.value.detailError)
            assertFalse(vm.state.value.isLoadingDetail)
        }
    }

    @Test
    fun anAutomaticDetailRefreshClearsRetainedContentWhenAccessIsRevoked() {
        for (status in listOf(401, 403, 404)) {
            val changes = MutableSharedFlow<Unit>(extraBufferCapacity = 1)
            val cached = MutableStateFlow(FlightSyncStatus(itemCount = 2, lastSuccessUtc = "2026-11-04T10:00:00Z"))
            var denied = false
            val vm = model(changes = changes, cacheStatus = cached,
                list = { _, _, _ -> FlightPage(items = listOf(row("one"), row("two")), totalCount = 2) },
                readDetail = {
                    if (denied) throw ApiException(status, "Flight unavailable")
                    detail(it)
                })
            vm.refresh()
            vm.openFlight("one")
            assertNotNull(vm.state.value.detail)
            denied = true

            assertTrue(changes.tryEmit(Unit))

            assertNull(vm.state.value.detail)
            assertNull(vm.state.value.selectedFlightId)
            assertFalse(vm.state.value.isLoadingDetail)
            if (status == 401) {
                assertTrue(vm.state.value.items.isEmpty())
            } else {
                assertEquals(listOf("two"), vm.state.value.items.map { it.id })
                assertEquals("one", vm.state.value.lastUnavailableFlightId)
                assertEquals("Flight unavailable", vm.state.value.detailError)
                assertFalse(vm.state.value.permissionDenied)
            }
        }
    }

    @Test
    fun openingADifferentFlightClearsThePreviousDetailUntilItsInitialLoadCompletes() {
        val first = CompletableDeferred<FlightDetail>()
        val second = CompletableDeferred<FlightDetail>()
        val vm = model(readDetail = { if (it == "one") first.await() else second.await() })

        vm.openFlight("one")
        assertEquals("one", vm.state.value.selectedFlightId)
        assertNull(vm.state.value.detail)
        assertTrue(vm.state.value.isLoadingDetail)
        first.complete(detail("one"))
        assertEquals("one", vm.state.value.detail!!.id)
        assertFalse(vm.state.value.isLoadingDetail)

        vm.openFlight("two")
        assertEquals("two", vm.state.value.selectedFlightId)
        assertNull(vm.state.value.detail)
        assertTrue(vm.state.value.isLoadingDetail)
        second.complete(detail("two"))
        assertEquals("two", vm.state.value.detail!!.id)
        assertFalse(vm.state.value.isLoadingDetail)
    }

    @Test
    fun aFlightSyncRefreshesTheLoadedRangeSilentlyAndStillBlocksPagination() {
        val changes = MutableSharedFlow<Unit>(extraBufferCapacity = 1)
        val cached = MutableStateFlow(FlightSyncStatus(lastSuccessUtc = "2026-11-03T11:00:00Z"))
        val refreshedSecondPage = CompletableDeferred<FlightPage>()
        var updated = false
        val requests = mutableListOf<Int>()
        val vm = model(changes = changes, cacheStatus = cached, list = { number, _, _ ->
            requests += number
            when {
                !updated -> page("old-$number").copy(page = number, totalCount = 3, hasNextPage = true)
                number == 1 -> page("new-1").copy(totalCount = 2, hasNextPage = true)
                else -> refreshedSecondPage.await()
            }
        })
        vm.refreshSilently()
        vm.loadMore()
        updated = true
        changes.tryEmit(Unit)

        assertEquals(listOf("old-1", "old-2"), vm.state.value.items.map { it.id })
        assertEquals(2, vm.state.value.page)
        assertTrue(vm.state.value.isRefreshing)
        assertFalse(vm.state.value.showRefreshIndicator)
        assertFalse(vm.state.value.isLoading)
        vm.loadMore()
        assertEquals(listOf(1, 2, 1, 2), requests)

        refreshedSecondPage.complete(page("new-2").copy(page = 2, totalCount = 2))
        assertEquals(listOf("new-1", "new-2"), vm.state.value.items.map { it.id })
        assertEquals(2, vm.state.value.page)
        assertFalse(vm.state.value.isRefreshing)
        assertFalse(vm.state.value.showRefreshIndicator)
    }

    @Test
    fun anAutomaticUpdateKeepsAnOngoingManualIndicatorUntilItsReplacementCompletes() {
        val changes = MutableSharedFlow<Unit>(extraBufferCapacity = 1)
        val cached = MutableStateFlow(FlightSyncStatus(lastSuccessUtc = "2026-11-03T11:00:00Z"))
        val manual = CompletableDeferred<FlightPage>()
        val replacement = CompletableDeferred<FlightPage>()
        var reads = 0
        val vm = model(changes = changes, cacheStatus = cached, list = { _, _, _ ->
            when (++reads) {
                1 -> page("old")
                2 -> manual.await()
                else -> replacement.await()
            }
        })
        vm.refreshSilently()
        vm.refresh()
        assertTrue(vm.state.value.showRefreshIndicator)
        changes.tryEmit(Unit)
        assertEquals(3, reads)
        assertTrue(vm.state.value.showRefreshIndicator)
        assertEquals(listOf("old"), vm.state.value.items.map { it.id })

        manual.complete(page("obsolete"))
        assertTrue(vm.state.value.showRefreshIndicator)
        assertEquals(listOf("old"), vm.state.value.items.map { it.id })
        replacement.complete(page("updated"))
        assertEquals(listOf("updated"), vm.state.value.items.map { it.id })
        assertFalse(vm.state.value.showRefreshIndicator)
        assertFalse(vm.state.value.isRefreshing)
    }

    @Test
    fun aLoadedEmptyListCanRefreshSilentlyAndManualFailureStopsItsIndicator() {
        val silent = CompletableDeferred<FlightPage>()
        val manual = CompletableDeferred<FlightPage>()
        var reads = 0
        val vm = model(list = { _, _, _ ->
            when (++reads) {
                1 -> FlightPage()
                2 -> silent.await()
                else -> manual.await()
            }
        })
        vm.refreshSilently()
        vm.refreshSilently()
        assertTrue(vm.state.value.hasLoadedList)
        assertTrue(vm.state.value.items.isEmpty())
        assertFalse(vm.state.value.isLoading)
        assertTrue(vm.state.value.isRefreshing)
        assertFalse(vm.state.value.showRefreshIndicator)

        vm.refresh()
        assertTrue(vm.state.value.showRefreshIndicator)
        assertFalse(vm.state.value.isLoading)
        silent.complete(page("obsolete"))
        manual.completeExceptionally(IOException("Connection lost"))
        assertTrue(vm.state.value.hasLoadedList)
        assertTrue(vm.state.value.items.isEmpty())
        assertNotNull(vm.state.value.error)
        assertFalse(vm.state.value.showRefreshIndicator)
        assertFalse(vm.state.value.isRefreshing)
    }

    @Test
    fun aSilentFirstReadRetainsInitialLoadingWithoutAManualIndicator() {
        val first = CompletableDeferred<FlightPage>()
        val vm = model(list = { _, _, _ -> first.await() })
        vm.refreshSilently()
        assertTrue(vm.state.value.isLoading)
        assertFalse(vm.state.value.hasLoadedList)
        assertFalse(vm.state.value.isRefreshing)
        assertFalse(vm.state.value.showRefreshIndicator)

        first.complete(page("one"))
        assertTrue(vm.state.value.hasLoadedList)
        assertFalse(vm.state.value.isLoading)
        assertFalse(vm.state.value.showRefreshIndicator)
    }

    @Test
    fun manualIndicatorsAreNotRetainedWhenSwitchingTabs() {
        val manual = CompletableDeferred<FlightPage>()
        val returned = CompletableDeferred<FlightPage>()
        var mineReads = 0
        val vm = model(categoryList = { category, _, _, _ ->
            if (category != FlightCategory.MyFlights) tabPage(category)
            else when (++mineReads) {
                1 -> tabPage(category, "old")
                2 -> manual.await()
                else -> returned.await()
            }
        })
        vm.refreshSilently()
        vm.setCategory(FlightCategory.PerLanding)
        vm.setCategory(FlightCategory.MyFlights)
        vm.refresh()
        assertTrue(vm.state.value.showRefreshIndicator)
        assertFalse(vm.state.value.forCategory(FlightCategory.PerLanding).showRefreshIndicator)

        vm.setCategory(FlightCategory.PerLanding)
        assertFalse(vm.state.value.showRefreshIndicator)
        assertFalse(vm.state.value.forCategory(FlightCategory.MyFlights).showRefreshIndicator)
        vm.setCategory(FlightCategory.MyFlights)
        assertTrue(vm.state.value.isRefreshing)
        assertFalse(vm.state.value.showRefreshIndicator)
        assertEquals(listOf("old"), vm.state.value.items.map { it.id })
        manual.complete(tabPage(FlightCategory.MyFlights, "obsolete"))
        returned.complete(tabPage(FlightCategory.MyFlights, "updated"))
        assertEquals(3, mineReads)
        assertEquals(listOf("updated"), vm.state.value.items.map { it.id })
        assertFalse(vm.state.value.showRefreshIndicator)
    }

    @Test
    fun deniedOrClearedCacheEventsRemoveVisibleDataWithoutRefetching() {
        val changes = MutableSharedFlow<Unit>(extraBufferCapacity = 1)
        val cached = MutableStateFlow(FlightSyncStatus(itemCount = 1, lastSuccessUtc = "2026-11-04T10:00:00Z"))
        var reads = 0
        val vm = model(changes = changes, cacheStatus = cached,
            list = { _, _, _ -> reads++; page("one") })
        vm.refresh()
        cached.value = FlightSyncStatus(permissionDenied = true)
        changes.tryEmit(Unit)
        assertTrue(vm.state.value.items.isEmpty())
        assertTrue(vm.state.value.permissionDenied)
        assertEquals(1, reads)
        cached.value = FlightSyncStatus()
        changes.tryEmit(Unit)
        assertEquals(1, reads)
    }

    @Test
    fun aDeniedFirstSyncClearsALiveOnlyListWithoutAnEvictionEvent() {
        val cached = MutableStateFlow(FlightSyncStatus())
        var reads = 0
        val vm = model(cacheStatus = cached, list = { _, _, _ -> reads++; page("one") })
        vm.refresh()
        assertEquals(1, vm.state.value.items.size)

        cached.value = FlightSyncStatus(permissionDenied = true)

        assertTrue(vm.state.value.items.isEmpty())
        assertTrue(vm.state.value.permissionDenied)
        assertEquals(1, reads)
    }

    @Test
    fun goingOfflineWhileChoosingInviteesDisablesSendingUntilOptionsAreRefreshed() {
        val online = MutableStateFlow(true)
        var sends = 0
        val vm = model(permissions = listOf("operations.flights.view", "operations.flights.invite"),
            online = online, options = { listOf(AssignedEmployee("a", "Employee", "A")) },
            send = { _, _, _ -> sends++ })
        vm.openFlight("one")
        vm.openInvitations()
        vm.selectInvitation("a", true)
        online.value = false
        vm.inviteSelectedEmployees()
        assertEquals(0, sends)
        assertFalse(vm.state.value.invitationOptionsLoaded)
        online.value = true
        vm.inviteSelectedEmployees()
        assertEquals(0, sends)
    }

    @Test
    fun invitationDetailRevocationRemovesOnlyThatFlightAndClearsItsChoices() {
        for (status in listOf(403, 404)) {
            val changes = MutableSharedFlow<Unit>(extraBufferCapacity = 1)
            val cached = MutableStateFlow(FlightSyncStatus(itemCount = 2, lastSuccessUtc = "2026-11-04T10:00:00Z"))
            var denied = false
            var evictionPublished = false
            var detailReads = 0
            val vm = model(permissions = listOf("operations.flights.view", "operations.flights.invite"),
                changes = changes, cacheStatus = cached,
                list = { _, _, _ ->
                    if (denied) FlightPage(items = listOf(row("two")), totalCount = 1)
                    else FlightPage(items = listOf(row("one"), row("two")), totalCount = 2)
                },
                readDetail = {
                    detailReads++
                    if (denied && it == "one") {
                        // The repository may publish an eviction before propagating the HTTP failure.
                        if (!evictionPublished) {
                            evictionPublished = true
                            changes.tryEmit(Unit)
                        }
                        throw ApiException(status, "Flight unavailable")
                    }
                    detail(it)
                },
                options = { listOf(AssignedEmployee("a", "Employee", "A")) })
            vm.refresh()
            vm.openFlight("one")
            vm.openInvitations()
            vm.selectInvitation("a", true)
            denied = true

            vm.refreshInvitations()

            assertEquals(listOf("two"), vm.state.value.items.map { it.id })
            assertNull(vm.state.value.detail)
            assertNull(vm.state.value.selectedFlightId)
            assertEquals("Flight unavailable", vm.state.value.detailError)
            assertFalse(vm.state.value.isInvitationOpen)
            assertTrue(vm.state.value.invitationOptions.isEmpty())
            assertTrue(vm.state.value.availableInvitationIds.isEmpty())
            assertTrue(vm.state.value.selectedInvitationIds.isEmpty())
            assertFalse(vm.state.value.permissionDenied)
            assertEquals("one", vm.state.value.lastUnavailableFlightId)
            assertEquals(3, detailReads)

            assertTrue(changes.tryEmit(Unit))
            assertNull(vm.state.value.selectedFlightId)
            assertEquals(3, detailReads)
            assertEquals("one", vm.state.value.lastUnavailableFlightId)
            vm.openFlight("two")
            assertEquals("two", vm.state.value.detail!!.id)
            assertNull(vm.state.value.lastUnavailableFlightId)
        }
    }

    @Test
    fun lostInvitationPermissionKeepsAuthorizedDetailButClearsAllInviteData() {
        var denied = false
        var version = "v1"
        var sends = 0
        val vm = model(permissions = listOf("operations.flights.view", "operations.flights.invite"),
            readDetail = { detail(it).copy(rowVersion = version) },
            options = {
                if (denied) throw ApiException(403, "Invitation permission lost")
                listOf(AssignedEmployee("a", "Employee", "A"))
            }, send = { _, _, _ -> sends++ })
        vm.refresh()
        vm.openFlight("one")
        vm.openInvitations()
        vm.selectInvitation("a", true)
        denied = true
        version = "v2"

        vm.refreshInvitations()
        vm.inviteSelectedEmployees()

        assertEquals("v2", vm.state.value.detail!!.rowVersion)
        assertEquals(listOf("one"), vm.state.value.items.map { it.id })
        assertFalse(vm.state.value.permissionDenied)
        assertTrue(vm.state.value.isInvitationOpen)
        assertFalse(vm.state.value.canInviteEmployees)
        assertFalse(vm.state.value.invitationOptionsLoaded)
        assertTrue(vm.state.value.invitationOptions.isEmpty())
        assertTrue(vm.state.value.availableInvitationIds.isEmpty())
        assertTrue(vm.state.value.selectedInvitationIds.isEmpty())
        assertEquals("Invitation permission lost", vm.state.value.invitationError)
        assertEquals(0, sends)
    }

    @Test
    fun unauthorizedInvitationRefreshClearsAllVisibleFlightDataAtEitherStage() {
        for (optionsStage in listOf(false, true)) {
            var unauthorized = false
            val vm = model(permissions = listOf("operations.flights.view", "operations.flights.invite"),
                readDetail = {
                    if (unauthorized && !optionsStage) throw ApiException(401, "Session ended")
                    detail(it)
                }, options = {
                    if (unauthorized && optionsStage) throw ApiException(401, "Session ended")
                    listOf(AssignedEmployee("a", "Employee", "A"))
                })
            vm.refresh()
            vm.openFlight("one")
            vm.openInvitations()
            vm.selectInvitation("a", true)
            unauthorized = true

            vm.refreshInvitations()

            assertTrue(vm.state.value.items.isEmpty())
            assertNull(vm.state.value.detail)
            assertNull(vm.state.value.selectedFlightId)
            assertFalse(vm.state.value.isInvitationOpen)
            assertTrue(vm.state.value.invitationOptions.isEmpty())
            assertTrue(vm.state.value.selectedInvitationIds.isEmpty())
            assertFalse(vm.state.value.canInviteEmployees)
        }
    }

    @Test
    fun transientInvitationRefreshKeepsValidChoicesAndSelectionForRetry() {
        var unavailable = false
        val vm = model(permissions = listOf("operations.flights.view", "operations.flights.invite"), options = {
            if (unavailable) throw IOException("Connection lost")
            listOf(AssignedEmployee("a", "Employee", "A"))
        })
        vm.openFlight("one")
        vm.openInvitations()
        vm.selectInvitation("a", true)
        unavailable = true

        vm.refreshInvitations()

        assertNotNull(vm.state.value.detail)
        assertTrue(vm.state.value.isInvitationOpen)
        assertEquals(listOf("a"), vm.state.value.invitationOptions.map { it.staffMemberId })
        assertEquals(setOf("a"), vm.state.value.selectedInvitationIds)
        assertFalse(vm.state.value.invitationOptionsLoaded)
        assertNotNull(vm.state.value.invitationError)
    }

    @Test
    fun switchingTabsRequestsItsCategoryAndDoesNotKeepThePreviousRows() {
        val requests = mutableListOf<Pair<FlightCategory, Int>>()
        val oldPage = CompletableDeferred<FlightPage>()
        val vm = model(categoryList = { category, number, _, _ ->
            requests += category to number
            when (category) {
                FlightCategory.MyFlights -> if (number == 1) page("mine").copy(hasNextPage = true) else oldPage.await()
                FlightCategory.PerLanding -> page("landing").copy(items = listOf(row("landing").copy(isMyFlight = false, isPerLanding = true)))
                FlightCategory.AdHoc -> page("adhoc").copy(items = listOf(row("adhoc").copy(isMyFlight = false, isMyAdHocFlight = true)))
            }
        })
        vm.refresh()
        vm.loadMore()
        vm.setCategory(FlightCategory.PerLanding)
        oldPage.complete(page("late-mine").copy(page = 2))
        assertEquals(listOf("landing"), vm.state.value.items.map { it.id })
        vm.setCategory(FlightCategory.AdHoc)
        assertEquals(listOf("adhoc"), vm.state.value.items.map { it.id })
        assertEquals(FlightCategory.AdHoc, vm.state.value.category)
        assertEquals(listOf(FlightCategory.MyFlights to 1, FlightCategory.MyFlights to 2,
            FlightCategory.PerLanding to 1, FlightCategory.AdHoc to 1), requests)
    }

    @Test
    fun offlineClockExpiryRemovesCardsAndClosesDetailsAndInvitations() {
        var at = Instant.parse("2026-11-04T00:00:00Z")
        var prunes = 0
        val ticks = Channel<Unit>(Channel.UNLIMITED)
        val online = MutableStateFlow(true)
        val vm = model(permissions = listOf("operations.flights.view", "operations.flights.invite"),
            online = online, now = { at }, expiryWait = { ticks.receive() }, pruneCache = { prunes++ },
            options = { listOf(AssignedEmployee("a", "Employee", "A")) })
        vm.refresh()
        vm.openFlight("one")
        vm.openInvitations()
        vm.selectInvitation("a", true)
        online.value = false
        ticks.trySend(Unit)
        assertEquals(listOf("one"), vm.state.value.items.map { it.id }) // inclusive boundary
        assertNotNull(vm.state.value.detail)

        at = at.plusNanos(1)
        ticks.trySend(Unit)
        assertTrue(vm.state.value.items.isEmpty())
        assertNull(vm.state.value.detail)
        assertNull(vm.state.value.selectedFlightId)
        assertFalse(vm.state.value.isInvitationOpen)
        assertTrue(vm.state.value.selectedInvitationIds.isEmpty())
        assertEquals(0L, vm.state.value.totalCount)
        assertTrue(prunes >= 2)
    }

    @Test
    fun aConfirmedStationChangeClosesThePreviousStationsDetailsAndInvitations() {
        var station = "station"
        val vm = model(permissions = listOf("operations.flights.view", "operations.flights.invite"),
            list = { _, _, _ -> page("one").copy(stationId = station) },
            options = { listOf(AssignedEmployee("a", "Employee", "A")) })
        vm.refresh()
        vm.openFlight("one")
        vm.openInvitations()
        vm.selectInvitation("a", true)
        station = "another-station"
        vm.refresh()
        assertEquals(station, vm.state.value.stationId)
        assertNull(vm.state.value.detail)
        assertNull(vm.state.value.selectedFlightId)
        assertFalse(vm.state.value.isInvitationOpen)
        assertTrue(vm.state.value.selectedInvitationIds.isEmpty())
    }

    @Test
    fun aStationChangeOnNextPageRestartsPagingInsteadOfCombiningStations() {
        var station = "station"
        val requests = mutableListOf<Pair<String, Int>>()
        val vm = model(list = { number, _, _ ->
            requests += station to number
            page("$station-$number").copy(stationId = station, page = number, hasNextPage = number == 1)
        })
        vm.refresh()
        vm.openFlight("station-1")
        station = "another-station"
        vm.loadMore()
        assertEquals(listOf("another-station-1"), vm.state.value.items.map { it.id })
        assertEquals(station, vm.state.value.stationId)
        assertEquals(1, vm.state.value.page)
        assertNull(vm.state.value.detail)
        assertNull(vm.state.value.selectedFlightId)
        assertEquals(listOf("station" to 1, "another-station" to 2, "another-station" to 1), requests)
    }

    @Test
    fun anExpiredNotificationDetailIsNeverDisplayed() {
        val vm = model(now = { Instant.parse("2026-11-04T00:00:00.000000001Z") })
        vm.openFlight("one")
        assertNull(vm.state.value.detail)
        assertNull(vm.state.value.selectedFlightId)
        assertEquals("one", vm.state.value.lastUnavailableFlightId)
    }

    @Test
    fun loadedTabsRetainAllPagesAndSuccessfulEmptyResultsWithoutReadingAgain() {
        val requests = mutableListOf<Pair<FlightCategory, Int>>()
        val vm = model(categoryList = { category, number, _, _ ->
            requests += category to number
            if (category == FlightCategory.MyFlights) tabPage(category, "mine-$number").copy(
                page = number, totalCount = 3, hasNextPage = true)
            else tabPage(category).copy(items = emptyList(), totalCount = 0)
        })
        vm.refresh()
        vm.loadMore()
        vm.setCategory(FlightCategory.PerLanding)
        vm.setCategory(FlightCategory.MyFlights)
        assertEquals(listOf("mine-1", "mine-2"), vm.state.value.items.map { it.id })
        assertEquals(2, vm.state.value.page)
        assertTrue(vm.state.value.hasMore)
        assertFalse(vm.state.value.isLoading)
        assertTrue(vm.state.value.forCategory(FlightCategory.PerLanding).hasLoadedList)
        vm.setCategory(FlightCategory.PerLanding)
        assertTrue(vm.state.value.items.isEmpty())
        assertTrue(vm.state.value.hasLoadedList)
        assertFalse(vm.state.value.isLoading)
        assertEquals(listOf(FlightCategory.MyFlights to 1, FlightCategory.MyFlights to 2,
            FlightCategory.PerLanding to 1), requests)
    }

    @Test
    fun aCanceledNextPageKeepsTheLoadedTabAndRetriesThatNextPage() {
        val pending = CompletableDeferred<FlightPage>()
        var continuationReads = 0
        val requests = mutableListOf<Pair<FlightCategory, Int>>()
        val vm = model(categoryList = { category, number, _, _ ->
            requests += category to number
            if (category == FlightCategory.MyFlights && number == 2 && continuationReads++ == 0) pending.await()
            else tabPage(category, "$category-$number").copy(page = number, totalCount = 2,
                hasNextPage = number == 1)
        })
        vm.refresh()
        vm.loadMore()
        vm.setCategory(FlightCategory.PerLanding)
        vm.setCategory(FlightCategory.MyFlights)
        assertEquals(listOf("MyFlights-1"), vm.state.value.items.map { it.id })
        assertEquals(1, vm.state.value.page)
        assertFalse(vm.state.value.isLoadingMore)
        vm.loadMore()
        pending.complete(tabPage(FlightCategory.MyFlights, "late").copy(page = 2))
        assertEquals(listOf("MyFlights-1", "MyFlights-2"), vm.state.value.items.map { it.id })
        assertEquals(listOf(FlightCategory.MyFlights to 1, FlightCategory.MyFlights to 2,
            FlightCategory.PerLanding to 1, FlightCategory.MyFlights to 2), requests)
    }

    @Test
    fun aTabWhoseFirstLoadWasCanceledIsReadWhenRevisited() {
        val pending = CompletableDeferred<FlightPage>()
        var mineReads = 0
        val vm = model(categoryList = { category, _, _, _ ->
            if (category == FlightCategory.MyFlights && ++mineReads == 1) pending.await()
            else tabPage(category, "current")
        })
        vm.refresh()
        vm.setCategory(FlightCategory.PerLanding)
        vm.setCategory(FlightCategory.MyFlights)
        pending.complete(tabPage(FlightCategory.MyFlights, "late"))
        assertEquals(2, mineReads)
        assertEquals(listOf("current"), vm.state.value.items.map { it.id })
        assertTrue(vm.state.value.hasLoadedList)
    }

    @Test
    fun anInvalidatedTabKeepsItsPagesVisibleUntilTheLoadedRangeIsRevalidated() {
        val changes = MutableSharedFlow<Unit>(extraBufferCapacity = 1)
        val cached = MutableStateFlow(FlightSyncStatus(lastSuccessUtc = "2026-11-03T11:00:00Z"))
        val secondPage = CompletableDeferred<FlightPage>()
        var updated = false
        val requests = mutableListOf<Pair<FlightCategory, Int>>()
        val vm = model(changes = changes, cacheStatus = cached, categoryList = { category, number, _, _ ->
            requests += category to number
            when {
                category != FlightCategory.MyFlights -> tabPage(category)
                !updated -> tabPage(category, "old-$number").copy(page = number, totalCount = 3, hasNextPage = true)
                number == 1 -> tabPage(category, "new-1", "new-2").copy(totalCount = 3, hasNextPage = true, fromCache = true)
                else -> secondPage.await()
            }
        })
        vm.refresh()
        vm.loadMore()
        vm.setCategory(FlightCategory.PerLanding)
        updated = true
        changes.tryEmit(Unit)
        vm.setCategory(FlightCategory.MyFlights)
        assertEquals(listOf("old-1", "old-2"), vm.state.value.items.map { it.id })
        assertEquals(2, vm.state.value.page)
        assertTrue(vm.state.value.isRefreshing)
        assertFalse(vm.state.value.isLoading)
        secondPage.complete(tabPage(FlightCategory.MyFlights, "new-2", "new-3").copy(page = 2, totalCount = 3))
        assertEquals(listOf("new-1", "new-2", "new-3"), vm.state.value.items.map { it.id })
        assertEquals(2, vm.state.value.page)
        assertEquals(3L, vm.state.value.totalCount)
        assertTrue(vm.state.value.isUsingCachedList)
        assertFalse(vm.state.value.isRefreshing)
        assertEquals(listOf(FlightCategory.MyFlights to 1, FlightCategory.MyFlights to 2,
            FlightCategory.PerLanding to 1, FlightCategory.PerLanding to 1,
            FlightCategory.MyFlights to 1, FlightCategory.MyFlights to 2), requests)
    }

    @Test
    fun cancelingAnInvalidatedTabsRefreshKeepsItsRefreshRequirement() {
        val changes = MutableSharedFlow<Unit>(extraBufferCapacity = 1)
        val cached = MutableStateFlow(FlightSyncStatus(lastSuccessUtc = "2026-11-03T11:00:00Z"))
        val pending = CompletableDeferred<FlightPage>()
        var mineReads = 0
        val vm = model(changes = changes, cacheStatus = cached, categoryList = { category, _, _, _ ->
            if (category != FlightCategory.MyFlights) tabPage(category)
            else when (++mineReads) {
                1 -> tabPage(category, "old")
                2 -> pending.await()
                else -> tabPage(category, "new")
            }
        })
        vm.refresh()
        vm.setCategory(FlightCategory.PerLanding)
        changes.tryEmit(Unit)
        vm.setCategory(FlightCategory.MyFlights)
        assertEquals(listOf("old"), vm.state.value.items.map { it.id })
        vm.setCategory(FlightCategory.PerLanding)
        vm.setCategory(FlightCategory.MyFlights)
        pending.complete(tabPage(FlightCategory.MyFlights, "late"))
        assertEquals(3, mineReads)
        assertEquals(listOf("new"), vm.state.value.items.map { it.id })
    }

    @Test
    fun inactiveTabsExpireAndCanReturnAsLoadedEmptyWithoutARead() {
        var at = Instant.parse("2026-11-03T11:00:00Z")
        val ticks = Channel<Unit>(Channel.UNLIMITED)
        var reads = 0
        val vm = model(now = { at }, expiryWait = { ticks.receive() }, categoryList = { category, _, _, _ ->
            reads++
            val page = tabPage(category, "$category")
            if (category == FlightCategory.PerLanding) page.copy(items = page.items.map {
                it.copy(scheduledDepartureUtc = "2026-11-04T12:00:00Z")
            }) else page
        })
        vm.refresh()
        vm.setCategory(FlightCategory.PerLanding)
        at = Instant.parse("2026-11-04T00:00:00.000000001Z")
        ticks.trySend(Unit)
        assertTrue(vm.state.value.forCategory(FlightCategory.MyFlights).items.isEmpty())
        assertTrue(vm.state.value.items.isNotEmpty())
        vm.setCategory(FlightCategory.MyFlights)
        assertEquals(2, reads)
        assertTrue(vm.state.value.items.isEmpty())
        assertTrue(vm.state.value.hasLoadedList)
        assertFalse(vm.state.value.isLoading)
    }

    @Test
    fun aConfirmedStationChangeEvictsEveryRetainedTabAndNormalSwitchKeepsTheStation() {
        var station = "station-a"
        val vm = model(categoryList = { category, _, _, _ ->
            tabPage(category, "$station-$category").copy(stationId = station)
        })
        vm.refresh()
        vm.setCategory(FlightCategory.PerLanding)
        assertEquals("station-a", vm.state.value.stationId)
        assertEquals(listOf("station-a-MyFlights"), vm.state.value.forCategory(FlightCategory.MyFlights).items.map { it.id })
        station = "station-b"
        vm.refresh()
        assertTrue(vm.state.value.retainedTabs.isEmpty())
        assertFalse(vm.state.value.forCategory(FlightCategory.MyFlights).hasLoadedList)
        assertEquals("station-b", vm.state.value.forCategory(FlightCategory.MyFlights).stationId)
        vm.setCategory(FlightCategory.MyFlights)
        assertEquals(listOf("station-b-MyFlights"), vm.state.value.items.map { it.id })
    }

    @Test
    fun accessDenialClearsAllRetainedTabs() {
        val cached = MutableStateFlow(FlightSyncStatus())
        val vm = model(cacheStatus = cached, categoryList = { category, _, _, _ -> tabPage(category) })
        vm.refresh()
        vm.setCategory(FlightCategory.PerLanding)
        assertTrue(vm.state.value.retainedTabs.isNotEmpty())
        cached.value = FlightSyncStatus(permissionDenied = true)
        assertTrue(vm.state.value.retainedTabs.isEmpty())
        assertTrue(vm.state.value.forCategory(FlightCategory.MyFlights).items.isEmpty())
        assertFalse(vm.state.value.hasLoadedList)
    }

    @Test
    fun deniedFlightDetailsRemoveTheFlightFromAnInactiveTab() {
        var reads = 0
        val vm = model(categoryList = { category, _, _, _ ->
            reads++
            if (category == FlightCategory.MyFlights) tabPage(category, "denied", "allowed") else tabPage(category)
        }, readDetail = { throw ApiException(404, "Flight unavailable") })
        vm.refresh()
        vm.setCategory(FlightCategory.PerLanding)
        vm.openFlight("denied")
        assertEquals(listOf("allowed"), vm.state.value.forCategory(FlightCategory.MyFlights).items.map { it.id })
        vm.setCategory(FlightCategory.MyFlights)
        assertEquals(2, reads)
        assertEquals(listOf("allowed"), vm.state.value.items.map { it.id })
    }

    @Test
    fun changingGlobalSearchInvalidatesOtherTabsOldQuery() {
        val requests = mutableListOf<Pair<FlightCategory, String?>>()
        val vm = model(categoryList = { category, _, _, search ->
            requests += category to search
            tabPage(category, search ?: "old")
        })
        vm.refresh()
        vm.setCategory(FlightCategory.PerLanding)
        vm.openSearch()
        vm.setSearch("new")
        vm.refresh()
        vm.setCategory(FlightCategory.MyFlights)
        assertEquals(listOf("new"), vm.state.value.items.map { it.id })
        assertEquals(listOf(FlightCategory.MyFlights to null, FlightCategory.PerLanding to null,
            FlightCategory.PerLanding to "new", FlightCategory.MyFlights to "new"), requests)
    }

    @Test
    fun closingSearchRestoresNormalPagesWithoutRefetchingThem() {
        val requests = mutableListOf<Triple<FlightCategory, Int, String?>>()
        val vm = model(searchDebounce = {}, categoryList = { category, number, _, query ->
            requests += Triple(category, number, query)
            tabPage(category, if (query == null) "normal-$number" else "match").copy(
                page = number, totalCount = if (query == null) 2 else 1,
                hasNextPage = query == null && number == 1)
        })
        vm.refreshSilently()
        vm.loadMore()
        vm.openSearch()
        assertTrue(vm.state.value.isSearchOpen)
        assertEquals(2, requests.size)
        vm.setSearch("123")
        assertEquals(listOf("match"), vm.state.value.items.map { it.id })
        vm.closeSearch()
        assertFalse(vm.state.value.isSearchOpen)
        assertEquals("", vm.state.value.search)
        assertEquals(listOf("normal-1", "normal-2"), vm.state.value.items.map { it.id })
        assertEquals(2, vm.state.value.page)
        assertTrue(vm.state.value.hasLoadedList)
        assertFalse(vm.state.value.isLoading)
        assertEquals(listOf(Triple(FlightCategory.MyFlights, 1, null), Triple(FlightCategory.MyFlights, 2, null),
            Triple(FlightCategory.MyFlights, 1, "123")), requests)
    }

    @Test
    fun searchLocationKeepsTheQueryAndClearRestoresThatTabsNormalEmptyResult() {
        val requests = mutableListOf<Pair<FlightCategory, String?>>()
        val vm = model(searchDebounce = {}, categoryList = { category, _, _, query ->
            requests += category to query
            if (query == null && category == FlightCategory.PerLanding) tabPage(category, *emptyArray())
            else tabPage(category, query ?: "normal")
        })
        vm.refreshSilently()
        vm.setCategory(FlightCategory.PerLanding)
        vm.setCategory(FlightCategory.MyFlights)
        vm.openSearch()
        vm.setSearch("456")
        vm.setCategory(FlightCategory.PerLanding)
        assertTrue(vm.state.value.isSearchOpen)
        assertEquals("456", vm.state.value.search)
        assertEquals(listOf("456"), vm.state.value.items.map { it.id })
        vm.setSearch("")
        assertTrue(vm.state.value.isSearchOpen)
        assertTrue(vm.state.value.hasLoadedList)
        assertTrue(vm.state.value.items.isEmpty())
        assertFalse(vm.state.value.isLoading)
        vm.closeSearch()
        assertFalse(vm.state.value.isSearchOpen)
        vm.setCategory(FlightCategory.MyFlights)
        assertEquals(listOf("normal"), vm.state.value.items.map { it.id })
        assertEquals(listOf(FlightCategory.MyFlights to null, FlightCategory.PerLanding to null,
            FlightCategory.MyFlights to "456", FlightCategory.PerLanding to "456"), requests)
    }

    @Test
    fun closingSearchRejectsQueuedInputAndLateRepliesBeforeTheyCanReplaceNormalRows() {
        val pending = CompletableDeferred<FlightPage>()
        val requests = mutableListOf<String?>()
        val vm = model(searchDebounce = {}, categoryList = { category, _, _, query ->
            requests += query
            if (query == null) tabPage(category, "normal") else pending.await()
        })
        vm.refreshSilently()
        vm.openSearch()
        vm.setSearch("pending")
        assertTrue(vm.state.value.isLoading)
        vm.closeSearch()
        vm.setSearch("queued IME value")
        pending.complete(tabPage(FlightCategory.MyFlights, "late-search"))
        assertEquals(listOf("normal"), vm.state.value.items.map { it.id })
        assertFalse(vm.state.value.isSearchOpen)
        assertEquals("", vm.state.value.search)
        assertFalse(vm.state.value.isLoading)
        assertEquals(listOf(null, "pending"), requests)
    }

    @Test
    fun aChangedStationDuringSearchCannotRestoreThePreviousStationsNormalCache() {
        var station = "station-a"
        val newNormal = CompletableDeferred<FlightPage>()
        val vm = model(searchDebounce = {}, categoryList = { category, _, _, query ->
            if (station == "station-b" && query == null) newNormal.await()
            else tabPage(category, "$station-${query ?: "normal"}").copy(stationId = station)
        })
        vm.refreshSilently()
        vm.openSearch()
        station = "station-b"
        vm.setSearch("match")
        assertEquals("station-b", vm.state.value.stationId)
        vm.closeSearch()
        assertTrue(vm.state.value.items.isEmpty())
        assertFalse(vm.state.value.hasLoadedList)
        assertEquals("station-b", vm.state.value.stationId)
        newNormal.complete(tabPage(FlightCategory.MyFlights, "station-b-normal").copy(stationId = station))
        assertEquals(listOf("station-b-normal"), vm.state.value.items.map { it.id })
    }

    @Test
    fun hiddenNormalSearchCacheExpiresBeforeItCanBeRestored() {
        var at = Instant.parse("2026-11-03T11:00:00Z")
        val ticks = Channel<Unit>(Channel.UNLIMITED)
        var reads = 0
        val vm = model(now = { at }, expiryWait = { ticks.receive() }, searchDebounce = {},
            categoryList = { category, _, _, query ->
                reads++
                val result = tabPage(category, query ?: "normal")
                if (query == null) result else result.copy(items = result.items.map {
                    it.copy(scheduledDepartureUtc = "2026-11-04T12:00:00Z")
                })
            })
        vm.refreshSilently()
        vm.openSearch()
        vm.setSearch("lasting")
        at = Instant.parse("2026-11-04T00:00:00.000000001Z")
        ticks.trySend(Unit)
        assertEquals(listOf("lasting"), vm.state.value.items.map { it.id })
        vm.closeSearch()
        assertTrue(vm.state.value.items.isEmpty())
        assertTrue(vm.state.value.hasLoadedList)
        assertEquals(2, reads)
    }

    @Test
    fun knownAccessLossDuringSearchCannotReviveTheNormalCache() {
        var denied = false
        val vm = model(searchDebounce = {}, categoryList = { category, _, _, _ ->
            if (denied) throw ApiException(403, "Access lost")
            tabPage(category, "normal")
        })
        vm.refreshSilently()
        vm.openSearch()
        denied = true
        vm.setSearch("denied")
        assertTrue(vm.state.value.permissionDenied)
        vm.closeSearch()
        assertTrue(vm.state.value.items.isEmpty())
        assertTrue(vm.state.value.retainedTabs.isEmpty())
        assertFalse(vm.state.value.hasLoadedList)
        assertTrue(vm.state.value.permissionDenied)
    }

    @Test
    fun syncWhileSearchingKeepsNormalPagesVisibleUntilTheyAreRevalidatedAfterClose() {
        val changes = MutableSharedFlow<Unit>(extraBufferCapacity = 1)
        val cached = MutableStateFlow(FlightSyncStatus(lastSuccessUtc = "2026-11-03T11:00:00Z"))
        val pendingNormal = CompletableDeferred<FlightPage>()
        var updated = false
        val vm = model(searchDebounce = {}, changes = changes, cacheStatus = cached,
            categoryList = { category, _, _, query ->
                when {
                    query != null -> tabPage(category, "match")
                    updated -> pendingNormal.await()
                    else -> tabPage(category, "normal")
                }
            })
        vm.refreshSilently()
        vm.openSearch()
        vm.setSearch("match")
        updated = true
        changes.tryEmit(Unit)
        vm.closeSearch()
        assertEquals(listOf("normal"), vm.state.value.items.map { it.id })
        assertTrue(vm.state.value.isRefreshing)
        assertFalse(vm.state.value.showRefreshIndicator)
        pendingNormal.complete(tabPage(FlightCategory.MyFlights, "updated"))
        assertEquals(listOf("updated"), vm.state.value.items.map { it.id })
        assertFalse(vm.state.value.isRefreshing)
    }

    @Test
    fun rapidQueryChangesDebounceToOnlyTheLatestScopedRequest() {
        val debounce = CompletableDeferred<Unit>()
        val requests = mutableListOf<String?>()
        val vm = model(searchDebounce = { debounce.await() }, list = { _, _, query ->
            requests += query
            page(query ?: "normal")
        })
        vm.refreshSilently()
        vm.openSearch()
        vm.setSearch("first")
        vm.setSearch("latest")
        assertEquals(listOf<String?>(null), requests)
        debounce.complete(Unit)
        assertEquals(listOf(null, "latest"), requests)
        assertEquals(listOf("latest"), vm.state.value.items.map { it.id })
        vm.closeSearch()
        assertEquals(listOf("normal"), vm.state.value.items.map { it.id })
    }

    @Test
    fun aDeniedDetailDuringSearchIsAlsoRemovedFromHiddenNormalRows() {
        val vm = model(searchDebounce = {}, categoryList = { category, _, _, query ->
            if (query == null) tabPage(category, "denied", "allowed") else tabPage(category, "match")
        }, readDetail = { throw ApiException(404, "Flight unavailable") })
        vm.refreshSilently()
        vm.openSearch()
        vm.setSearch("match")
        vm.openFlight("denied")
        vm.closeSearch()
        assertEquals(listOf("allowed"), vm.state.value.items.map { it.id })
        assertEquals(1L, vm.state.value.totalCount)
    }

    private fun tabPage(category: FlightCategory, vararg ids: String = arrayOf("$category")) = FlightPage(
        items = ids.map { id -> row(id).copy(isMyFlight = category == FlightCategory.MyFlights,
            isPerLanding = category == FlightCategory.PerLanding, isMyAdHocFlight = category == FlightCategory.AdHoc) },
        totalCount = ids.size.toLong(), stationId = "station")

    private fun model(
        permissions: List<String> = listOf("operations.flights.view"),
        list: suspend (Int, Int, String?) -> FlightPage = { _, _, _ -> page("one") },
        readDetail: suspend (String) -> FlightDetail = { detail(it) },
        options: suspend (String) -> List<AssignedEmployee> = { emptyList() },
        send: suspend (String, List<String>, String) -> Unit = { _, _, _ -> },
        online: MutableStateFlow<Boolean> = MutableStateFlow(true),
        changes: Flow<Unit> = emptyFlow(),
        cacheStatus: Flow<FlightSyncStatus> = flowOf(FlightSyncStatus()),
        profile: (suspend () -> AuthenticatedUser)? = null,
        clearCache: suspend () -> Unit = {},
        categoryList: (suspend (FlightCategory, Int, Int, String?) -> FlightPage)? = null,
        now: () -> Instant = { Instant.parse("2026-11-03T11:00:00Z") },
        expiryWait: suspend (Long) -> Unit = { delay(it) },
        pruneCache: suspend () -> Unit = {},
        searchDebounce: suspend () -> Unit = { delay(300) },
    ) = FlightsViewModel(
        fetchProfile = profile ?: { AuthenticatedUser("user", "staff@example.test", "Staff", "StationStaff", permissions = permissions) },
        fetchFlights = categoryList ?: { _, number, size, search -> list(number, size, search) },
        fetchFlight = readDetail, fetchInvitationOptions = options,
        sendInvitations = send, coroutineScope = scope, online = online,
        cacheChanges = changes, cacheStatus = cacheStatus, clearCachedFlights = clearCache,
        now = now, waitForExpiryCheck = expiryWait, pruneCachedFlights = pruneCache,
        waitForSearchDebounce = searchDebounce,
    )

    private fun page(id: String) = FlightPage(items = listOf(row(id)), totalCount = 1)
    private fun row(id: String) = FlightListItem(id, "123", customerName = "Airline", stationIataCode = "JED",
        operationTypeName = "Transit", scheduledArrivalUtc = "2026-11-03T10:00:00Z",
        scheduledDepartureUtc = "2026-11-03T12:00:00Z", isPerLanding = false, isMyFlight = true, stationTimeZoneId = "Asia/Riyadh")
    private fun detail(id: String) = FlightDetail(id, "123", "customer", customerName = "Airline",
        stationId = "station", stationIata = "JED", stationName = "Jeddah", operationTypeId = "operation",
        operationTypeName = "Transit", scheduledArrivalUtc = "2026-11-03T10:00:00Z",
        scheduledDepartureUtc = "2026-11-03T12:00:00Z", isPerLanding = false,
        createdAtUtc = "2026-11-03T09:00:00Z", rowVersion = "v1", stationTimeZoneId = "Asia/Riyadh")
}
