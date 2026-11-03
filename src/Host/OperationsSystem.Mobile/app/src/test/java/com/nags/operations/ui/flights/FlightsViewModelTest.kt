package com.nags.operations.ui.flights

import com.nags.operations.data.ApiException
import com.nags.operations.data.AssignedEmployee
import com.nags.operations.data.AuthenticatedUser
import com.nags.operations.data.FlightDetail
import com.nags.operations.data.FlightListItem
import com.nags.operations.data.FlightPage
import java.io.IOException
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
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
        val vm = model(permissions = emptyList(), list = { _, _, _ -> reads++; page("one") },
            readDetail = { reads++; detail(it) })
        vm.refresh()
        vm.openFlight("one")
        assertEquals(0, reads)
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
    fun deniedDetailDoesNotFallBackToAListSnapshot() {
        val vm = model(readDetail = { throw ApiException(403, "Flight unavailable") })
        vm.refresh()
        vm.openFlight("one")
        assertNull(vm.state.value.detail)
        assertEquals("Flight unavailable", vm.state.value.detailError)
        assertEquals(1, vm.state.value.items.size)
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

    private fun model(
        permissions: List<String> = listOf("operations.flights.view"),
        list: suspend (Int, Int, String?) -> FlightPage = { _, _, _ -> page("one") },
        readDetail: suspend (String) -> FlightDetail = { detail(it) },
        options: suspend (String) -> List<AssignedEmployee> = { emptyList() },
        send: suspend (String, List<String>, String) -> Unit = { _, _, _ -> },
    ) = FlightsViewModel(
        fetchProfile = { AuthenticatedUser("user", "staff@example.test", "Staff", "StationStaff", permissions = permissions) },
        fetchFlights = list, fetchFlight = readDetail, fetchInvitationOptions = options,
        sendInvitations = send, coroutineScope = scope,
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
