package com.nags.operations.ui.flights

import com.nags.operations.data.ApiException
import com.nags.operations.data.AssignedEmployee
import com.nags.operations.data.AuthenticatedUser
import com.nags.operations.data.FlightDetail
import com.nags.operations.data.FlightListItem
import com.nags.operations.data.FlightPage
import com.nags.operations.data.FlightSyncStatus
import java.io.IOException
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
        vm.setSearch(" 123 ")
        vm.refresh()
        vm.loadMore()
        assertEquals(listOf(1 to "123", 2 to "123"), requests)
        assertEquals(listOf("1", "2"), vm.state.value.items.map { it.id })
        assertEquals(2L, vm.state.value.totalCount)
        assertFalse(vm.state.value.hasMore)
    }

    @Test
    fun aPreviousSearchResponseCannotReplaceTheNewSearch() {
        val firstResponse = CompletableDeferred<FlightPage>()
        val vm = model(list = { _, _, search ->
            if (search == null) firstResponse.await() else page("new")
        })
        vm.refresh()
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
        var number = "123"
        var listReads = 0
        var detailReads = 0
        val vm = model(changes = changes, cacheStatus = cached,
            list = { _, _, _ -> listReads++; page("one") },
            readDetail = { detailReads++; detail(it).copy(flightNumber = number) })
        vm.refresh()
        vm.openFlight("one")
        number = "456"
        changes.tryEmit(Unit)
        assertEquals(2, listReads)
        assertEquals(2, detailReads)
        assertEquals("456", vm.state.value.detail!!.flightNumber)
        assertEquals(listOf("one"), vm.state.value.items.map { it.id })
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
    ) = FlightsViewModel(
        fetchProfile = profile ?: { AuthenticatedUser("user", "staff@example.test", "Staff", "StationStaff", permissions = permissions) },
        fetchFlights = list, fetchFlight = readDetail, fetchInvitationOptions = options,
        sendInvitations = send, coroutineScope = scope, online = online,
        cacheChanges = changes, cacheStatus = cacheStatus, clearCachedFlights = clearCache,
    )

    private fun page(id: String) = FlightPage(items = listOf(row(id)), totalCount = 1)
    private fun row(id: String) = FlightListItem(id, "123", customerName = "Airline", stationIataCode = "JED",
        operationTypeName = "Transit", scheduledArrivalUtc = "2026-11-03T10:00:00Z",
        scheduledDepartureUtc = "2026-11-03T12:00:00Z", isPerLanding = false)
    private fun detail(id: String) = FlightDetail(id, "123", "customer", customerName = "Airline",
        stationId = "station", stationIata = "JED", stationName = "Jeddah", operationTypeId = "operation",
        operationTypeName = "Transit", scheduledArrivalUtc = "2026-11-03T10:00:00Z",
        scheduledDepartureUtc = "2026-11-03T12:00:00Z", isPerLanding = false,
        createdAtUtc = "2026-11-03T09:00:00Z", rowVersion = "v1")
}
