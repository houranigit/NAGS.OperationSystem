package com.nags.operations.ui.notifications

import com.nags.operations.data.notifications.*
import java.io.IOException
import kotlinx.coroutines.*
import kotlinx.coroutines.flow.*
import org.junit.After
import org.junit.Assert.*
import org.junit.Test

class NotificationsViewModelTest {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Unconfined)
    @After fun tearDown() = scope.cancel()

    @Test fun onlyVerifiedFlightsCanOpenAndSchedulesStayInformational() {
        val id = "36780af4-97c1-4bb3-9af0-43c661b1ed92"
        val missing = "fe85f964-1315-4e73-b24e-31f4f0ad7f91"
        val available = row("available").copy(payload = mapOf("flightId" to id))
        val unavailable = row("missing").copy(payload = mapOf("flightId" to missing))
        val schedule = row("schedule", kind = NotificationKinds.FlightScheduleUpdated).copy(payload = mapOf("flightId" to id))
        val rows = MutableStateFlow(listOf(available, unavailable, schedule))
        val vm = model(rows, targets = { setOf(id) })
        assertEquals(setOf(id), vm.state.value.openableFlightIds)
        val opened = mutableListOf<String?>()
        vm.open(schedule, { opened += it.flightId }, { fail("Schedule updates must not navigate") })
        vm.open(unavailable, { opened += it.flightId })
        vm.open(available, { opened += it.flightId })
        assertEquals(listOf(id), opened)
    }

    @Test fun flightDisappearingAfterLoadIsRecheckedBeforeNavigation() {
        val id = "36780af4-97c1-4bb3-9af0-43c661b1ed92"
        val notification = row("flight").copy(payload = mapOf("flightId" to id))
        var exists = true
        val vm = model(MutableStateFlow(listOf(notification)), targets = { if (exists) setOf(id) else emptySet() })
        assertTrue(id in vm.state.value.openableFlightIds)
        exists = false
        vm.open(notification, { fail("A removed flight must not navigate") })
        assertTrue(vm.state.value.openableFlightIds.isEmpty())
        assertTrue(vm.state.value.openingIds.isEmpty())
        assertNotNull(vm.state.value.actionError)
    }

    @Test fun olderBatchCannotReenableAFlightThatFailedTheOpenRecheck() {
        val id = "36780af4-97c1-4bb3-9af0-43c661b1ed92"
        val notification = row("flight").copy(payload = mapOf("flightId" to id))
        val stale = CompletableDeferred<Set<String>>()
        var calls = 0
        val vm = model(MutableStateFlow(listOf(notification)), targets = {
            when (++calls) { 1 -> setOf(id); 2 -> stale.await(); else -> emptySet() }
        })
        vm.open(notification, { fail("The flight disappeared") })
        stale.complete(setOf(id))
        assertTrue(vm.state.value.openableFlightIds.isEmpty())
    }

    @Test fun failedDeleteKeepsTheCardAndAllowsGestureToRestoreWithoutDuplicateRequests() {
        val notification = row("one")
        val rows = MutableStateFlow(listOf(notification))
        val response = CompletableDeferred<Unit>()
        var calls = 0
        var restored = 0
        val vm = model(rows, archive = { calls++; response.await() })
        vm.archive(notification) { restored++ }
        vm.archive(notification) { fail("Duplicate delete must not run") }
        assertEquals(setOf("one"), vm.state.value.archivingIds)
        response.completeExceptionally(IOException("Offline"))
        assertEquals(1, calls)
        assertEquals(1, restored)
        assertEquals(listOf(notification), vm.state.value.items)
        assertTrue(vm.state.value.archivingIds.isEmpty())
        assertNotNull(vm.state.value.actionError)
    }

    @Test fun completedDeleteRemovesOnlyItsCardAndClearsBusyState() {
        val notification = row("one")
        val rows = MutableStateFlow(listOf(notification, row("two")))
        val vm = model(rows, archive = { id -> rows.value = rows.value.filterNot { it.id == id } })
        vm.archive(notification) { fail("Successful delete must not restore") }
        assertEquals(listOf("two"), vm.state.value.items.map { it.id })
        assertTrue(vm.state.value.archivingIds.isEmpty())
        assertNull(vm.state.value.actionError)
    }

    @Test fun availabilityReturningAfterThePageClosesCannotEnableOldTargets() {
        val id = "36780af4-97c1-4bb3-9af0-43c661b1ed92"
        val response = CompletableDeferred<Set<String>>()
        val vm = model(MutableStateFlow(listOf(row("one").copy(payload = mapOf("flightId" to id)))),
            targets = { withContext(NonCancellable) { response.await() } })
        vm.endVisit()
        response.complete(setOf(id))
        assertTrue(vm.state.value.openableFlightIds.isEmpty())
    }

    @Test fun mixedNotificationsShareOneListAndKeepTheirTintUntilTheNextVisit() {
        val rows = MutableStateFlow(listOf(row("invite"), row("reminder", kind = NotificationKinds.FlightReminder),
            row("assignment", kind = NotificationKinds.StaffAssignedToFlight), row("old", read = true)))
        var reads = 0
        val vm = model(rows, acknowledge = {
            reads++
            rows.value = rows.value.map { if (it.isRead) it else it.copy(isRead = true, readAtUtc = "2026-11-09T13:00:00Z") }
            CategoryReadResult("2026-11-09T12:00:00Z", "2026-11-09T13:00:00Z")
        })
        assertEquals(4, vm.state.value.items.size)
        assertEquals(0, reads)
        vm.acknowledgeLoadedInbox()
        assertEquals(1, reads)
        assertEquals(setOf("invite", "reminder", "assignment"), vm.state.value.newIds)
        vm.endVisit(); vm.beginVisit()
        assertTrue(vm.state.value.newIds.isEmpty())
    }

    @Test fun olderUnloadedRowsUseTheVisitReceiptButAlreadyReadRowsStayOld() {
        val rows = MutableStateFlow(listOf(row("first")))
        val vm = model(rows, acknowledge = { CategoryReadResult("2026-11-09T12:00:00Z", "2026-11-09T12:59:00Z", "2026-11-09T13:00:00Z") })
        vm.acknowledgeLoadedInbox()
        rows.value = listOf(row("older", true).copy(readAtUtc = "2026-11-09T13:00:00Z"), row("old", true), row("first", true))
        assertEquals(setOf("first", "older"), vm.state.value.newIds)
    }

    @Test fun reconnectReceiptPreservesTintForTheCurrentVisitOnly() {
        val rows = MutableStateFlow(listOf(row("first")))
        val events = MutableSharedFlow<CategoryReadAcknowledgement>(extraBufferCapacity = 2)
        val vm = model(rows, events = events, acknowledge = { CategoryReadResult("2026-11-09T12:00:00Z", "2026-11-09T13:00:00Z") })
        vm.acknowledgeLoadedInbox()
        rows.value = listOf(row("first", true), row("older", true).copy(readAtUtc = "2026-11-09T14:00:00Z"))
        assertFalse("older" in vm.state.value.newIds)
        events.tryEmit(CategoryReadAcknowledgement(null, "2026-11-09T13:00:00Z", "2026-11-09T14:00:00Z"))
        assertTrue("older" in vm.state.value.newIds)
        vm.endVisit(); vm.beginVisit()
        events.tryEmit(CategoryReadAcknowledgement(null, "2026-11-09T13:00:00Z", "2026-11-09T14:00:00Z"))
        assertTrue(vm.state.value.newIds.isEmpty())
    }

    @Test fun notificationsArrivingDuringAcknowledgementAreAcknowledgedInTheNextPass() {
        val rows = MutableStateFlow(listOf(row("first")))
        val first = CompletableDeferred<CategoryReadResult?>()
        var calls = 0
        val vm = model(rows, acknowledge = {
            calls++
            if (calls == 1) first.await() else CategoryReadResult("2026-11-09T13:00:00Z", "2026-11-09T14:00:00Z")
        })
        vm.acknowledgeLoadedInbox()
        rows.value = rows.value + row("new").copy(createdAtUtc = "2026-11-09T13:00:00Z")
        vm.acknowledgeLoadedInbox()
        assertEquals(1, calls)
        first.complete(CategoryReadResult("2026-11-09T12:00:00Z", "2026-11-09T13:00:00Z"))
        assertEquals(2, calls)
        assertEquals(setOf("first", "new"), vm.state.value.newIds)
    }

    @Test fun hiddenPageDoesNotAcknowledgeAndLateReceiptsDoNotAffectTheNextVisit() {
        val response = CompletableDeferred<CategoryReadResult?>()
        var reads = 0
        val vm = model(MutableStateFlow(listOf(row("first"))), start = false, acknowledge = {
            reads++; withContext(NonCancellable) { response.await() }
        })
        vm.acknowledgeLoadedInbox(); assertEquals(0, reads)
        vm.beginVisit(); vm.acknowledgeLoadedInbox(); assertEquals(1, reads)
        vm.endVisit(); vm.beginVisit()
        response.complete(CategoryReadResult("2026-11-09T12:00:00Z", "2026-11-09T14:00:00Z"))
        vm.acknowledgeLoadedInbox()
        assertEquals(2, reads)
    }

    @Test fun failedRefreshRetainsCachedRowsAndCanRetry() {
        var fail = true
        val vm = model(MutableStateFlow(listOf(row("cached"))), fetch = {
            if (fail) throw IOException("offline") else page(it)
        })
        assertEquals(listOf("cached"), vm.state.value.items.map { it.id })
        assertNotNull(vm.state.value.error)
        fail = false; vm.refresh()
        assertNull(vm.state.value.error)
    }

    @Test fun olderRefreshCannotOverwriteNewerPageMetadata() {
        val old = CompletableDeferred<NotificationPageDto>()
        var calls = 0
        val vm = model(fetch = { if (++calls == 1) withContext(NonCancellable) { old.await() } else page(it) })
        vm.refresh()
        old.complete(page(1, true))
        assertFalse(vm.state.value.hasMore)
        assertFalse(vm.state.value.isLoading)
    }

    @Test fun pagingDoesNotDuplicateRequestsAndAdvancesThroughOverlappingCachedRows() {
        val next = CompletableDeferred<NotificationPageDto>()
        val requests = mutableListOf<Int>()
        val vm = model(MutableStateFlow(List(60) { row("$it") }), fetch = {
            requests += it; if (it == 2) next.await() else page(it, true)
        })
        vm.loadMore(); vm.loadMore()
        assertEquals(listOf(1, 2), requests)
        next.complete(page(2, true))
        assertEquals(2, nextNotificationPageToLoad(vm.state.value, 59))
        vm.loadMore(); assertEquals(listOf(1, 2, 3), requests)
        assertNull(nextNotificationPageToLoad(vm.state.value.copy(isFromCache = true), 59))
        assertNull(nextNotificationPageToLoad(vm.state.value.copy(loadMoreError = "retry"), 59))
    }

    @Test fun emptyFirstLoadReceivesAndAcknowledgesTheFirstCachedArrival() {
        val rows = MutableStateFlow(emptyList<NotificationDto>())
        var reads = 0
        val vm = model(rows, acknowledge = { reads++; CategoryReadResult("2026-11-09T12:00:00Z", "2026-11-09T13:00:00Z") })
        vm.acknowledgeLoadedInbox(); assertEquals(0, reads)
        rows.value = listOf(row("first")); vm.acknowledgeLoadedInbox()
        assertEquals(1, reads)
        assertEquals(setOf("first"), vm.state.value.newIds)
    }

    @Test fun openingAnExpiredReminderAcknowledgesWithoutOpeningItsFlight() {
        val events = mutableListOf<String>()
        val vm = model(read = { events += "read:$it" })
        vm.open(row("expired", kind = NotificationKinds.FlightReminder).copy(payload = mapOf("flightId" to "flight", "scheduledArrivalUtc" to "2000-01-01T00:00:00Z")),
            onOpenFlight = { events += "flight" })
        assertEquals(listOf("read:expired"), events)
    }

    private fun model(
        rows: Flow<List<NotificationDto>> = flowOf(emptyList()),
        fetch: suspend (Int) -> NotificationPageDto = { page(it) },
        acknowledge: suspend () -> CategoryReadResult? = { null },
        events: Flow<CategoryReadAcknowledgement> = emptyFlow(),
        read: (String) -> Unit = {}, start: Boolean = true,
        targets: suspend (List<String>) -> Set<String> = { it.toSet() },
        archive: suspend (String) -> Unit = {},
    ) = NotificationsViewModel({ rows }, fetch, acknowledge, read, archive, {}, events, scope,
        fetchOpenableFlightIds = targets).also { if (start) it.beginVisit() }
    private fun page(number: Int, more: Boolean = false) = NotificationPageDto(page = number, pageSize = 20, totalCount = 60, hasNextPage = more)
    private fun row(id: String, read: Boolean = false, kind: String = NotificationKinds.EmployeeInvitedToFlight) = NotificationDto(
        id, kind, "Title", "Body", "عنوان", "نص", isRead = read, createdAtUtc = "2026-11-09T12:00:00Z", readAtUtc = if (read) "2026-11-08T12:00:00Z" else null)
}
