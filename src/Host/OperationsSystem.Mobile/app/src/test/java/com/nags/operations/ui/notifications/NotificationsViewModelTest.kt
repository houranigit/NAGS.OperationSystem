package com.nags.operations.ui.notifications

import com.nags.operations.data.notifications.CategoryReadAcknowledgement
import com.nags.operations.data.notifications.CategoryReadResult
import com.nags.operations.data.notifications.NotificationCategory
import com.nags.operations.data.notifications.NotificationDto
import com.nags.operations.data.notifications.NotificationKinds
import com.nags.operations.data.notifications.NotificationPageDto
import java.io.IOException
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.NonCancellable
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.emptyFlow
import kotlinx.coroutines.flow.flowOf
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.withContext
import org.junit.After
import org.junit.Assert.*
import org.junit.Test

class NotificationsViewModelTest {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Unconfined)

    @After
    fun tearDown() = scope.cancel()

    @Test
    fun categoryChangesReplaceTheInboxObserverAndKeepBothNewAndOldRows() {
        val feeds = NotificationCategory.entries.associateWith { MutableStateFlow(listOf(row(it.name), row("old", true))) }
        val vm = model(inbox = { feeds.getValue(it) })
        assertEquals(1, feeds.getValue(NotificationCategory.Invitations).subscriptionCount.value)

        vm.setCategory(NotificationCategory.Reminders)

        assertEquals(0, feeds.getValue(NotificationCategory.Invitations).subscriptionCount.value)
        assertEquals(1, feeds.getValue(NotificationCategory.Reminders).subscriptionCount.value)
        assertEquals(listOf("Reminders", "old"), vm.state.value.items.map { it.id })
        assertEquals(setOf("Reminders"), vm.state.value.newIds)
    }

    @Test
    fun categoryChangesResetPagingAndNextPagesStayWithinTheSelectedCategory() {
        val nextPage = CompletableDeferred<NotificationPageDto>()
        val requests = mutableListOf<Pair<Int, NotificationCategory>>()
        val vm = model(fetch = { number, category ->
            requests += number to category
            if (number == 2) nextPage.await() else page(number, hasMore = true)
        })
        assertEquals(NotificationCategory.Invitations, vm.state.value.category)
        vm.loadMore()
        vm.loadMore()
        assertEquals(listOf(1 to NotificationCategory.Invitations, 2 to NotificationCategory.Invitations), requests)
        nextPage.complete(page(2))
        assertEquals(2, vm.state.value.page)

        vm.setCategory(NotificationCategory.FlightUpdates)
        assertEquals(1 to NotificationCategory.FlightUpdates, requests.last())
        assertEquals(1, vm.state.value.page)
        assertTrue(vm.state.value.hasMore)
        assertFalse(vm.state.value.isLoadingMore)
        vm.loadMore()
        assertEquals(2 to NotificationCategory.FlightUpdates, requests.last())
    }

    @Test
    fun aLateRefreshCannotOverwriteTheSameCategoryAfterSwitchingAwayAndBack() {
        val oldResponse = CompletableDeferred<NotificationPageDto>()
        var invitationReads = 0
        val vm = model(fetch = { number, category ->
            if (category == NotificationCategory.Invitations && ++invitationReads == 1) {
                withContext(NonCancellable) { oldResponse.await() }
            } else page(number, hasMore = true)
        })

        vm.setCategory(NotificationCategory.Reminders)
        vm.setCategory(NotificationCategory.Invitations)
        assertTrue(vm.state.value.hasMore)
        oldResponse.complete(page(9))

        assertEquals(NotificationCategory.Invitations, vm.state.value.category)
        assertEquals(1, vm.state.value.page)
        assertTrue(vm.state.value.hasMore)
        assertFalse(vm.state.value.isLoading)
        assertNull(vm.state.value.error)
    }

    @Test
    fun aLatePagingFailureDoesNotChangeTheNewTabOrItsLoadingState() {
        val oldResponse = CompletableDeferred<NotificationPageDto>()
        val vm = model(fetch = { number, _ ->
            if (number == 2) withContext(NonCancellable) { oldResponse.await() }
            else page(number, hasMore = true)
        })
        vm.loadMore()
        vm.setCategory(NotificationCategory.Reminders)
        oldResponse.completeExceptionally(IOException("Old request"))

        assertEquals(NotificationCategory.Reminders, vm.state.value.category)
        assertEquals(1, vm.state.value.page)
        assertTrue(vm.state.value.hasMore)
        assertFalse(vm.state.value.isLoadingMore)
        assertFalse(vm.state.value.isLoading)
        assertNull(vm.state.value.error)
    }

    @Test
    fun refreshingInvalidatesAnInFlightNextPageWithoutChangingTheCurrentCategory() {
        val oldPage = CompletableDeferred<NotificationPageDto>()
        var firstPageReads = 0
        val vm = model(fetch = { number, _ ->
            if (number == 2) withContext(NonCancellable) { oldPage.await() }
            else page(1, hasMore = ++firstPageReads == 1)
        })
        vm.loadMore()
        vm.refresh(userInitiated = true)
        oldPage.complete(page(2, hasMore = true))

        assertEquals(1, vm.state.value.page)
        assertFalse(vm.state.value.hasMore)
        assertFalse(vm.state.value.isLoadingMore)
        assertFalse(vm.state.value.isRefreshing)
    }

    @Test
    fun changingTabsClearsOldRowsAndErrors_AndRetriesOnlyTheSelectedCategory() {
        val requests = mutableListOf<NotificationCategory>()
        var fail = true
        val vm = model(inbox = { category ->
            flowOf(if (category == NotificationCategory.Invitations) listOf(row("invitation")) else emptyList())
        }, fetch = { number, category ->
            requests += category
            if (category == NotificationCategory.Reminders && fail) throw IOException()
            page(number)
        })
        assertEquals(listOf("invitation"), vm.state.value.items.map { it.id })

        vm.setCategory(NotificationCategory.Reminders)
        assertTrue(vm.state.value.items.isEmpty())
        assertNotNull(vm.state.value.error)
        assertFalse(vm.state.value.isLoading)
        fail = false
        vm.refresh(userInitiated = true)

        assertEquals(NotificationCategory.Reminders, requests.last())
        assertNull(vm.state.value.error)
        assertFalse(vm.state.value.isRefreshing)
    }

    @Test
    fun openingAnUnreadNotificationStartsTheRepositoryReadBeforeNavigationCanCancelTheViewModel() {
        val events = mutableListOf<String>()
        val vm = model(read = { events += "read:$it" })

        vm.open(row("opened"), onOpenFlight = {
            events += "open:${it.flightId}"
            scope.cancel()
        })

        assertEquals(listOf("read:opened", "open:flight"), events)
    }

    @Test
    fun expiredReminderOpensAreReadWithoutNavigation_AndAlreadyReadRowsDoNotWriteAgain() {
        val read = mutableListOf<String>()
        var navigations = 0
        val vm = model(read = { read += it })
        val expired = row("expired").copy(kind = NotificationKinds.FlightReminder,
            payload = mapOf("flightId" to "flight", "scheduledArrivalUtc" to "2000-01-01T00:00:00Z"))

        vm.open(expired, onOpenFlight = { navigations++ })
        vm.open(row("read", true), onOpenFlight = { navigations++ })

        assertEquals(listOf("expired"), read)
        assertEquals(1, navigations)
    }

    @Test
    fun successfulArchiveAllRefreshesTheCurrentCategoryFromPageOne() {
        val completed = CompletableDeferred<Unit>()
        val requests = mutableListOf<Pair<Int, NotificationCategory>>()
        val vm = model(fetch = { number, category ->
            requests += number to category
            page(number, hasMore = number == 1)
        }, archiveAll = { completed.await() })
        vm.loadMore()
        vm.archiveAll()
        vm.setCategory(NotificationCategory.Reminders)
        vm.loadMore()
        assertEquals(2 to NotificationCategory.Reminders, requests.last())
        completed.complete(Unit)

        assertEquals(1 to NotificationCategory.Reminders, requests.last())
        assertEquals(1, vm.state.value.page)
        assertTrue(vm.state.value.hasMore)
    }

    @Test
    fun failedArchiveAllDoesNotRefreshTheInbox() {
        var reads = 0
        val vm = model(fetch = { number, _ -> reads++; page(number) }, archiveAll = { throw IOException() })

        vm.archiveAll()

        assertEquals(1, reads)
        assertFalse(vm.state.value.isLoading)
        assertFalse(vm.state.value.isRefreshing)
    }

    @Test
    fun acknowledgingLoadedRowsKeepsTheVisitTint_AndReceiptStampsIdentifyNewlyReadOlderPages() {
        val rows = MutableStateFlow(listOf(row("new"), row("old", true).copy(readAtUtc = "2026-11-09T11:00:00Z")))
        var acknowledgments = 0
        val vm = model(inbox = { rows }, acknowledge = {
            acknowledgments++
            val stamp = "2026-11-09T13:00:0${acknowledgments}Z"
            rows.value = rows.value.map { if (it.isRead) it else it.copy(isRead = true, readAtUtc = stamp) }
            CategoryReadResult("2026-11-09T12:00:00Z", stamp, stamp)
        })
        assertEquals(setOf("new"), vm.state.value.newIds)

        vm.acknowledgeLoadedCategory()
        vm.acknowledgeLoadedCategory()

        assertEquals(1, acknowledgments)
        assertTrue(vm.state.value.items.all { it.isRead })
        assertEquals(setOf("new"), vm.state.value.newIds)
        rows.value += listOf(
            row("older-new", true).copy(createdAtUtc = "2026-11-09T10:00:00Z", readAtUtc = "2026-11-09T13:00:01+00:00"),
            row("older-old", true).copy(createdAtUtc = "2026-11-09T09:00:00Z", readAtUtc = "2026-11-09T11:00:00Z"),
        )
        assertEquals(setOf("new", "older-new"), vm.state.value.newIds)

        vm.endVisit()
        vm.beginVisit()
        vm.acknowledgeLoadedCategory()

        assertEquals(2, acknowledgments)
        assertTrue(vm.state.value.newIds.isEmpty())
    }

    @Test
    fun anEmptyCacheWaitsForLoadedRows_AndNoCategoryIsAcknowledgedWhileThePageIsClosed() {
        val rows = MutableStateFlow<List<NotificationDto>>(emptyList())
        val acknowledged = mutableListOf<NotificationCategory>()
        val vm = model(inbox = { rows }, acknowledge = {
            acknowledged += it
            val stamp = "2026-11-09T13:00:00Z"
            rows.value = rows.value.map { item -> item.copy(isRead = true, readAtUtc = stamp) }
            CategoryReadResult("2026-11-09T12:00:00Z", stamp)
        })
        vm.acknowledgeLoadedCategory()
        assertTrue(acknowledged.isEmpty())
        rows.value = listOf(row("loaded"))
        vm.acknowledgeLoadedCategory()
        assertEquals(listOf(NotificationCategory.Invitations), acknowledged)

        vm.endVisit()
        rows.value += row("arrived-later")
        vm.acknowledgeLoadedCategory()
        assertEquals(1, acknowledged.size)
        vm.beginVisit()
        assertEquals(setOf("arrived-later"), vm.state.value.newIds)
        vm.acknowledgeLoadedCategory()
        assertEquals(2, acknowledged.size)
    }

    @Test
    fun rowsArrivingDuringAnAcknowledgmentAreCapturedAndAcknowledgedAfterItCompletes() {
        val rows = MutableStateFlow(listOf(row("first")))
        val firstReceipt = CompletableDeferred<CategoryReadResult?>()
        var acknowledgments = 0
        val vm = model(inbox = { rows }, acknowledge = {
            acknowledgments++
            if (acknowledgments == 1) firstReceipt.await()
            else {
                rows.value = rows.value.map { item -> item.copy(isRead = true, readAtUtc = "2026-11-09T13:00:02Z") }
                CategoryReadResult("2026-11-09T12:01:00Z", "2026-11-09T13:00:02Z")
            }
        })
        vm.acknowledgeLoadedCategory()
        rows.value += row("second").copy(createdAtUtc = "2026-11-09T12:01:00Z")
        vm.acknowledgeLoadedCategory()
        assertEquals(1, acknowledgments)

        firstReceipt.complete(CategoryReadResult("2026-11-09T12:00:00Z", "2026-11-09T13:00:01Z"))

        assertEquals(2, acknowledgments)
        assertEquals(setOf("first", "second"), vm.state.value.newIds)
        assertTrue(vm.state.value.items.all { it.isRead })
    }

    @Test
    fun aReceiptFromThePreviousCategoryCannotTintTheCurrentVisit() {
        val oldReceipt = CompletableDeferred<CategoryReadResult?>()
        val vm = model(inbox = { category ->
            flowOf(if (category == NotificationCategory.Invitations) listOf(row("new"))
                else listOf(row("read-reminder", true).copy(readAtUtc = "2026-11-09T13:00:01Z")))
        }, acknowledge = { withContext(NonCancellable) { oldReceipt.await() } })
        vm.acknowledgeLoadedCategory()
        vm.setCategory(NotificationCategory.Reminders)

        oldReceipt.complete(CategoryReadResult("2026-11-09T12:00:00Z", "2026-11-09T13:00:01Z"))

        assertEquals(NotificationCategory.Reminders, vm.state.value.category)
        assertTrue(vm.state.value.newIds.isEmpty())
    }

    @Test
    fun reconnectReceiptsTintNewlyReadOlderRowsOnlyForTheVisitThatCreatedThePendingAcknowledgment() {
        val rows = MutableStateFlow(listOf(row("new")))
        val receipts = MutableSharedFlow<CategoryReadAcknowledgement>(extraBufferCapacity = 1)
        val local = "2026-11-09T13:00:01Z"
        val server = "2026-11-09T13:00:02Z"
        val vm = model(inbox = { rows }, readEvents = receipts, acknowledge = {
            rows.value = rows.value.map { item -> item.copy(isRead = true, readAtUtc = local) }
            CategoryReadResult("2026-11-09T12:00:00Z", local)
        })
        vm.acknowledgeLoadedCategory()
        rows.value += row("older", true).copy(createdAtUtc = "2026-11-09T10:00:00Z", readAtUtc = server)
        assertEquals(setOf("new"), vm.state.value.newIds)

        receipts.tryEmit(CategoryReadAcknowledgement(NotificationCategory.Invitations, local, server))

        assertEquals(setOf("new", "older"), vm.state.value.newIds)
        vm.endVisit()
        vm.beginVisit()
        receipts.tryEmit(CategoryReadAcknowledgement(NotificationCategory.Invitations, local, server))
        assertTrue(vm.state.value.newIds.isEmpty())
    }

    @Test
    fun automaticPagingAdvancesThroughOverlappingCachedPages_AndStopsForErrorsOrOfflineFallback() {
        val initial = NotificationsViewModel.UiState(items = List(60) { row("$it") },
            isLoading = false, hasMore = true, page = 1)
        assertEquals(1, nextNotificationPageToLoad(initial, 58))
        assertNull(nextNotificationPageToLoad(initial.copy(isLoadingMore = true), 58))
        assertEquals(2, nextNotificationPageToLoad(initial.copy(page = 2), 58))
        assertEquals(3, nextNotificationPageToLoad(initial.copy(page = 3), 58))
        assertNull(nextNotificationPageToLoad(initial.copy(loadMoreError = "Retry"), 58))
        assertNull(nextNotificationPageToLoad(initial.copy(isFromCache = true), 58))
        assertNull(nextNotificationPageToLoad(initial, 1))

        val vm = model(fetch = { number, _ -> page(number).copy(isFromCache = true) })
        assertTrue(vm.state.value.isFromCache)
        assertFalse(vm.state.value.hasMore)
        assertFalse(vm.state.value.isLoading)
    }

    private fun model(
        inbox: (NotificationCategory) -> Flow<List<NotificationDto>> = { flowOf(emptyList()) },
        fetch: suspend (Int, NotificationCategory) -> NotificationPageDto = { number, _ -> page(number) },
        read: (String) -> Unit = {},
        acknowledge: suspend (NotificationCategory) -> CategoryReadResult? = { null },
        archiveAll: suspend () -> Unit = {},
        readEvents: Flow<CategoryReadAcknowledgement> = emptyFlow(),
    ) = NotificationsViewModel(inbox, fetch, acknowledge, read, {}, archiveAll, readEvents, scope).also { it.beginVisit() }

    private fun page(number: Int, hasMore: Boolean = false) = NotificationPageDto(
        page = number, pageSize = 20, totalCount = 1, hasNextPage = hasMore,
    )

    private fun row(id: String, read: Boolean = false) = NotificationDto(
        id = id, kind = NotificationKinds.EmployeeInvitedToFlight,
        titleEn = "Invitation", bodyEn = "Flight invitation", titleAr = "دعوة", bodyAr = "دعوة رحلة",
        payload = mapOf("flightId" to "flight"), isRead = read, createdAtUtc = "2026-11-09T12:00:00Z",
    )
}
