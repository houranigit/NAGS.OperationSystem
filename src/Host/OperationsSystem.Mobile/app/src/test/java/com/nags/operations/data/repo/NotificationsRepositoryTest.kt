package com.nags.operations.data.repo

import com.nags.operations.data.db.dao.NotificationDao
import com.nags.operations.data.db.entities.NotificationCategoryReadEntity
import com.nags.operations.data.db.entities.NotificationUnreadStateEntity
import com.nags.operations.data.notifications.CategoryReadReceiptDto
import com.nags.operations.data.db.entities.NotificationEntity
import com.nags.operations.data.db.entities.toEntity
import com.nags.operations.data.notifications.NotificationCategory
import com.nags.operations.data.notifications.NotificationDto
import com.nags.operations.data.notifications.NotificationKinds
import com.nags.operations.data.notifications.NotificationPageDto
import java.io.IOException
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.CoroutineDispatcher
import kotlin.coroutines.CoroutineContext
import kotlinx.coroutines.CoroutineStart
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.async
import kotlinx.coroutines.cancel
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.runBlocking
import org.junit.After
import org.junit.Assert.*
import org.junit.Test

class NotificationsRepositoryTest {
    private val dao = MemoryNotificationDao()
    private val subject = MutableStateFlow<String?>("employee")
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Unconfined)
    private var online = true
    private var categoryRead: suspend (NotificationCategory, String) -> CategoryReadReceiptDto = { _, _ ->
        CategoryReadReceiptDto("2026-11-09T12:00:00Z", 0)
    }
    private var count: suspend () -> Int = { 48 }
    private var acknowledge: suspend (String) -> Unit = {}
    private var bulkRead: suspend () -> Unit = {}
    private var bulkArchive: suspend () -> Unit = {}
    private var inbox: suspend (Int, Int, NotificationCategory?) -> NotificationPageDto = { page, size, _ ->
        NotificationPageDto(emptyList(), page, size, 0)
    }
    private fun createRepository(processScope: CoroutineScope = scope) = NotificationsRepository(
        fetchInbox = { page, size, category -> inbox(page, size, category) },
        fetchUnreadCount = { count() },
        sendMarkRead = { acknowledge(it) }, sendMarkAllRead = { bulkRead() }, sendArchive = {}, sendArchiveAll = { bulkArchive() },
        dao = dao, subjectFlow = subject, currentSubject = { subject.value }, appScope = processScope,
        sendCategoryRead = { category, cutoff -> categoryRead(category, cutoff) }, isOnline = { online },
    )

    private val repository = createRepository()

    @After
    fun tearDown() = scope.cancel()

    @Test
    fun readingCachedRowsAdjustsTheWholeInboxTotalOnceEvenWhenCountRefreshFails() = runBlocking {
        dao.upsert(item("first").toEntity("employee"))
        dao.upsert(item("second").toEntity("employee"))
        repository.refreshUnreadCount()
        count = { throw IOException() }

        repository.markRead("first")
        assertEquals(47, repository.observeUnreadCount().first())
        repository.markRead("first")
        assertEquals(47, repository.observeUnreadCount().first())
        repository.archive("second")
        assertEquals(46, repository.observeUnreadCount().first())
        assertTrue(dao.getById("first", "employee")!!.isRead)
    }

    @Test
    fun openingAnUncachedOrAlreadyReadNotificationReconcilesInsteadOfGuessingTheBadge() = runBlocking {
        repository.refreshUnreadCount()
        count = { 48 } // The off-page notification was already read on another device.
        repository.markRead("off-page")
        repository.markRead("off-page")
        assertEquals(48, repository.observeUnreadCount().first())
    }

    @Test
    fun pushDoesNotReplaceAnAuthoritativeTotalWithThePartialCacheOrDoubleCountRetries() = runBlocking {
        repository.refreshUnreadCount()
        count = { throw IOException() }
        repository.ingest(item("push"), "employee")
        repository.ingest(item("push"), "employee")
        assertEquals(48, repository.observeUnreadCount().first())
        count = { 49 }
        repository.refreshUnreadCount()
        assertEquals(49, repository.observeUnreadCount().first())
        assertFalse(repository.ingest(item("wrong-account"), "other"))
    }

    @Test
    fun unreadResponseStartedBeforeAReadCannotRestoreTheOldBadge() = runBlocking {
        dao.upsert(item("first").toEntity("employee"))
        repository.refreshUnreadCount()
        val staleCount = CompletableDeferred<Int>()
        count = { staleCount.await() }
        val request = async(start = CoroutineStart.UNDISPATCHED) { repository.refreshUnreadCount() }
        count = { throw IOException() }
        repository.markRead("first")
        staleCount.complete(48)
        request.await()
        assertEquals(47, repository.observeUnreadCount().first())
    }

    @Test
    fun countResponseThatAlreadyIncludesAReadIsNotDecrementedAgain() = runBlocking {
        dao.upsert(item("first").toEntity("employee"))
        repository.refreshUnreadCount()
        val response = CompletableDeferred<Int>()
        count = { response.await() }
        val concurrentCount = async(start = CoroutineStart.UNDISPATCHED) { repository.refreshUnreadCount() }
        acknowledge = {
            response.complete(47)
            concurrentCount.await()
            count = { throw IOException() }
        }
        repository.markRead("first")
        assertEquals(47, repository.observeUnreadCount().first())
    }

    @Test
    fun concurrentReadsEachAdjustTheTotalWhenNoNewServerCountHasArrived() = runBlocking {
        dao.upsert(item("first").toEntity("employee"))
        dao.upsert(item("second").toEntity("employee"))
        repository.refreshUnreadCount()
        count = { throw IOException() }
        val firstResponse = CompletableDeferred<Unit>()
        val secondResponse = CompletableDeferred<Unit>()
        acknowledge = { if (it == "first") firstResponse.await() else secondResponse.await() }
        val first = async(start = CoroutineStart.UNDISPATCHED) { repository.markRead("first") }
        val second = async(start = CoroutineStart.UNDISPATCHED) { repository.markRead("second") }
        firstResponse.complete(Unit)
        first.await()
        secondResponse.complete(Unit)
        second.await()
        assertEquals(46, repository.observeUnreadCount().first())
    }

    @Test
    fun categoryRefreshKeepsOtherTabsAndReadReceiptsMadeWhileLoading() = runBlocking {
        val invitation = item("invitation", NotificationKinds.EmployeeInvitedToFlight)
        dao.upsert(invitation.toEntity("employee"))
        dao.upsert(item("reminder", NotificationKinds.FlightReminder).toEntity("employee"))
        dao.upsert(item("old-invitation", NotificationKinds.EmployeeInvitedToFlight).toEntity("employee"))
        dao.upsert(item("archived", NotificationKinds.EmployeeInvitedToFlight).toEntity("employee"))
        repository.archive("archived")
        val response = CompletableDeferred<NotificationPageDto>()
        inbox = { _, _, category ->
            assertEquals(NotificationCategory.Invitations, category)
            response.await()
        }
        val refresh = async(start = CoroutineStart.UNDISPATCHED) {
            repository.refresh(category = NotificationCategory.Invitations)
        }
        repository.markRead(invitation.id)
        response.complete(NotificationPageDto(listOf(invitation, item("archived", invitation.kind)), 1, 20, 2))
        refresh.await()

        assertEquals(listOf("invitation"), repository.observeInbox(false, NotificationCategory.Invitations).first().map { it.id })
        assertTrue(repository.observeInbox(false, NotificationCategory.Invitations).first().single().isRead)
        assertEquals(listOf("reminder"), repository.observeInbox(false, NotificationCategory.Reminders).first().map { it.id })
        assertTrue(repository.observeInbox(true, NotificationCategory.Invitations).first().isEmpty())
    }

    @Test
    fun readOnOpenOutlivesTheCallingScreenAndFailedReadsRemainUnread() = runBlocking {
        dao.upsert(item("first").toEntity("employee"))
        val response = CompletableDeferred<Unit>()
        acknowledge = { response.await() }
        val screenScope = CoroutineScope(SupervisorJob() + Dispatchers.Unconfined)
        val opening = screenScope.async { repository.markReadOnOpen("first") }
        opening.await()
        screenScope.cancel()
        response.complete(Unit)
        assertTrue(dao.getById("first", "employee")!!.isRead)

        dao.upsert(item("failed").toEntity("employee"))
        acknowledge = { throw IOException() }
        repository.markReadOnOpen("failed")
        assertFalse(dao.getById("failed", "employee")!!.isRead)
    }

    @Test
    fun oldAccountResponsesCannotChangeTheNewAccountsBadgeOrCache() = runBlocking {
        dao.upsert(item("first").toEntity("employee"))
        repository.refreshUnreadCount()
        val response = CompletableDeferred<Unit>()
        acknowledge = { response.await() }
        val read = async(start = CoroutineStart.UNDISPATCHED) { repository.markRead("first") }
        subject.value = "other"
        response.complete(Unit)
        read.await()
        assertEquals(0, repository.observeUnreadCount().first())
        assertTrue(repository.observeInbox(false).first().isEmpty())
        assertFalse(dao.getById("first", "employee")!!.isRead)
    }

    @Test
    fun bulkOperationsDoNotReadOrArchivePushesThatArriveAfterTheyStart() = runBlocking {
        dao.upsert(item("old").toEntity("employee"))
        val readResponse = CompletableDeferred<Unit>()
        bulkRead = { readResponse.await() }
        count = { throw IOException() }
        val read = async(start = CoroutineStart.UNDISPATCHED) { repository.markAllRead() }
        repository.ingest(item("after-read-start"), "employee")
        readResponse.complete(Unit)
        read.await()
        assertFalse(dao.getById("after-read-start", "employee")!!.isRead)
        assertEquals(1, repository.observeUnreadCount().first())

        val archiveResponse = CompletableDeferred<Unit>()
        bulkArchive = { archiveResponse.await() }
        val archive = async(start = CoroutineStart.UNDISPATCHED) { repository.archiveAll() }
        repository.ingest(item("after-archive-start"), "employee")
        archiveResponse.complete(Unit)
        archive.await()
        assertEquals(listOf("after-archive-start"), repository.observeInbox(false).first().map { it.id })
        assertEquals(1, repository.observeUnreadCount().first())
    }

    @Test
    fun staleCategoryOrStartupResponsesCannotReplaceNewerTabContents() = runBlocking {
        val stale = CompletableDeferred<NotificationPageDto>()
        inbox = { _, _, _ -> stale.await() }
        val first = async(start = CoroutineStart.UNDISPATCHED) {
            repository.refresh(category = NotificationCategory.Invitations)
        }
        val current = item("current", NotificationKinds.EmployeeInvitedToFlight)
        inbox = { page, size, _ -> NotificationPageDto(listOf(current), page, size, 1) }
        repository.refresh(category = NotificationCategory.Invitations)
        stale.complete(NotificationPageDto(listOf(item("stale", current.kind)), 1, 20, 1))
        first.await()
        assertEquals(listOf("current"), repository.observeInbox(false).first().map { it.id })

        val startup = CompletableDeferred<NotificationPageDto>()
        inbox = { _, _, _ -> startup.await() }
        val startupRequest = async(start = CoroutineStart.UNDISPATCHED) { repository.refresh() }
        inbox = { page, size, _ -> NotificationPageDto(listOf(current), page, size, 1) }
        repository.refresh(category = NotificationCategory.Invitations)
        startup.complete(NotificationPageDto(emptyList(), 1, 20, 0))
        startupRequest.await()
        assertEquals(listOf("current"), repository.observeInbox(false).first().map { it.id })
    }

    @Test
    fun categoryRefreshPreservesAPushReceivedWhileTheServerPageWasLoading() = runBlocking {
        val response = CompletableDeferred<NotificationPageDto>()
        inbox = { _, _, _ -> response.await() }
        val refresh = async(start = CoroutineStart.UNDISPATCHED) {
            repository.refresh(category = NotificationCategory.Invitations)
        }
        repository.ingest(item("push", NotificationKinds.EmployeeInvitedToFlight), "employee")
        response.complete(NotificationPageDto(emptyList(), 1, 20, 0))
        refresh.await()
        assertEquals(listOf("push"), repository.observeInbox(false, NotificationCategory.Invitations).first().map { it.id })
    }

    @Test
    fun refreshingAfterBulkActionsReconcilesPushesIncludedByTheServersBulkOperation() = runBlocking {
        val push = item("included-by-server")
        bulkRead = { repository.ingest(push, "employee") }
        count = { 0 }
        repository.markAllRead()
        inbox = { page, size, _ -> NotificationPageDto(listOf(push.copy(isRead = true)), page, size, 1) }
        repository.refresh(category = NotificationCategory.FlightUpdates)
        assertTrue(repository.observeInbox(false).first().single().isRead)
        assertEquals(0, repository.observeUnreadCount().first())

        bulkArchive = { repository.ingest(item("also-archived-by-server"), "employee") }
        repository.archiveAll()
        inbox = { page, size, _ -> NotificationPageDto(emptyList(), page, size, 0) }
        repository.refresh(category = NotificationCategory.FlightUpdates)
        assertTrue(repository.observeInbox(false).first().isEmpty())
        assertEquals(0, repository.observeUnreadCount().first())
    }

    @Test
    fun openingATabOfflinePersistsReadStateAndTheWholeInboxBadgeAcrossRepositoryRestart() = runBlocking {
        dao.upsert(item("invitation", NotificationKinds.EmployeeInvitedToFlight).toEntity("employee"))
        dao.upsert(item("reminder", NotificationKinds.FlightReminder).toEntity("employee"))
        repository.refreshUnreadCount()
        online = false
        val opened = repository.acknowledgeCategory(NotificationCategory.Invitations)!!
        assertNull(opened.serverReadAtUtc)
        assertTrue(dao.getById("invitation", "employee")!!.isRead)
        assertFalse(dao.getById("reminder", "employee")!!.isRead)
        assertEquals(47, repository.observeUnreadCount().first())
        repository.acknowledgeCategory(NotificationCategory.Invitations)
        assertEquals(1, dao.pendingCategoryReads("employee").single().unreadDelta)

        val restarted = createRepository()
        assertEquals(47, restarted.observeUnreadCount().first())
        assertTrue(restarted.observeInbox(false, NotificationCategory.Invitations).first().single().isRead)
        var sent: Pair<NotificationCategory, String>? = null
        categoryRead = { category, cutoff ->
            sent = category to cutoff
            CategoryReadReceiptDto("2026-11-09T13:00:00Z", 20)
        }
        online = true
        restarted.syncPendingReads()
        assertEquals(NotificationCategory.Invitations to "2026-11-09T10:00:00Z", sent)
        assertTrue(dao.pendingCategoryReads("employee").isEmpty())
        assertEquals(20, restarted.observeUnreadCount().first())
        assertEquals(20, createRepository().observeUnreadCount().first())
    }

    @Test
    fun leavingTheScreenDoesNotCancelThePersistedCategoryAcknowledgement() = runBlocking {
        dao.upsert(item("first").toEntity("employee"))
        val response = CompletableDeferred<CategoryReadReceiptDto>()
        categoryRead = { _, _ -> response.await() }
        val request = async(start = CoroutineStart.UNDISPATCHED) { repository.acknowledgeCategory(NotificationCategory.FlightUpdates) }
        assertTrue(dao.getById("first", "employee")!!.isRead)
        assertTrue(dao.pendingCategoryReads("employee").isNotEmpty())
        request.cancel()
        response.complete(CategoryReadReceiptDto("2026-11-09T12:00:00Z", 0))
        repository.syncPendingReads()
        assertTrue(dao.pendingCategoryReads("employee").isEmpty())
        assertEquals(0, repository.observeUnreadCount().first())
    }

    @Test
    fun aNewerCutoffQueuedDuringSyncIsAcknowledgedWithoutLosingItsPendingRead() = runBlocking {
        dao.upsert(item("first").toEntity("employee"))
        val firstResponse = CompletableDeferred<CategoryReadReceiptDto>()
        val cutoffs = mutableListOf<String>()
        categoryRead = { _, cutoff ->
            cutoffs += cutoff
            if (cutoffs.size == 1) firstResponse.await() else CategoryReadReceiptDto("2026-11-09T12:05:00Z", 0)
        }
        val first = async(start = CoroutineStart.UNDISPATCHED) { repository.acknowledgeCategory(NotificationCategory.FlightUpdates) }
        dao.upsert(item("newer").copy(createdAtUtc = "2026-11-09T11:00:00Z").toEntity("employee"))
        val second = async(start = CoroutineStart.UNDISPATCHED) { repository.acknowledgeCategory(NotificationCategory.FlightUpdates) }
        firstResponse.complete(CategoryReadReceiptDto("2026-11-09T12:00:00Z", 1))
        first.await()
        second.await()
        assertEquals(listOf("2026-11-09T10:00:00Z", "2026-11-09T11:00:00Z"), cutoffs)
        assertTrue(dao.pendingCategoryReads("employee").isEmpty())
        assertEquals(0, repository.observeUnreadCount().first())
    }

    @Test
    fun cachedHistorySurvivesFirstPageRefreshAndRemainsAvailableOffline() = runBlocking {
        val newest = item("newest").copy(createdAtUtc = "2026-11-09T11:00:00Z")
        val older = item("older")
        inbox = { page, size, _ -> NotificationPageDto(listOf(if (page == 1) newest else older), page, size, 2,
            totalPages = 2, hasNextPage = page == 1) }
        repository.refresh(pageSize = 1, category = NotificationCategory.FlightUpdates)
        repository.refresh(page = 2, pageSize = 1, category = NotificationCategory.FlightUpdates)
        repository.refresh(pageSize = 1, category = NotificationCategory.FlightUpdates)
        assertEquals(listOf("newest", "older"), repository.observeInbox(false).first().map { it.id })
        online = false
        val cached = createRepository().refresh(category = NotificationCategory.FlightUpdates)
        assertTrue(cached.isFromCache)
        assertFalse(cached.hasNextPage)
        assertEquals(listOf("newest", "older"), cached.items.map { it.id })
    }

    @Test
    fun authorizationFailuresAreNotHiddenByTheDeviceCache() = runBlocking {
        dao.upsert(item("first").toEntity("employee"))
        inbox = { _, _, _ -> throw com.nags.operations.data.ApiException(403, "Denied") }
        val failure = runCatching { repository.refresh(category = NotificationCategory.FlightUpdates) }.exceptionOrNull()
        assertTrue(failure is com.nags.operations.data.ApiException && failure.statusCode == 403)
    }

    @Test
    fun pendingReadWatermarkCoversOlderCachedRowsWhileNewerAlertsStayUnread() = runBlocking {
        dao.upsert(item("first").toEntity("employee"))
        online = false
        repository.acknowledgeCategory(NotificationCategory.FlightUpdates)
        repository.ingest(item("delayed-older").copy(createdAtUtc = "2026-11-09T09:00:00Z"), "employee")
        repository.ingest(item("newer").copy(createdAtUtc = "2026-11-09T11:00:00Z"), "employee")
        assertTrue(dao.getById("delayed-older", "employee")!!.isRead)
        assertFalse(dao.getById("newer", "employee")!!.isRead)
    }

    @Test
    fun aDelayedOlderPushDoesNotBecomeUnreadAfterItsCategoryAcknowledgementCompleted() = runBlocking {
        dao.upsert(item("first").toEntity("employee"))
        repository.acknowledgeCategory(NotificationCategory.FlightUpdates)
        assertTrue(dao.pendingCategoryReads("employee").isEmpty())
        online = false
        repository.ingest(item("delayed").copy(createdAtUtc = "2026-11-09T09:00:00Z"), "employee")
        assertTrue(dao.getById("delayed", "employee")!!.isRead)
        assertEquals(0, repository.observeUnreadCount().first())
        assertTrue(dao.pendingCategoryReads("employee").isNotEmpty())
        online = true
        repository.syncPendingReads()
        assertTrue(dao.pendingCategoryReads("employee").isEmpty())
    }

    @Test
    fun aServerReceiptIsNotDecrementedAgainForPendingReadsAlreadyAcknowledgedOnAnotherDevice() = runBlocking {
        dao.upsert(item("invitation", NotificationKinds.EmployeeInvitedToFlight).toEntity("employee"))
        dao.upsert(item("reminder", NotificationKinds.FlightReminder).toEntity("employee"))
        count = { 100 }
        repository.refreshUnreadCount()
        online = false
        repository.acknowledgeCategory(NotificationCategory.Invitations)
        repository.acknowledgeCategory(NotificationCategory.Reminders)
        categoryRead = { category, _ ->
            if (category == NotificationCategory.Reminders) throw IOException()
            // The portal already read the reminder too;98 already excludes both rows.
            CategoryReadReceiptDto("2026-11-09T12:00:00Z", 98)
        }
        online = true
        repository.syncPendingReads()
        assertEquals(98, repository.observeUnreadCount().first())
        assertEquals(98, createRepository().observeUnreadCount().first())
        assertEquals(NotificationCategory.Reminders.name, dao.pendingCategoryReads("employee").single().category)
    }

    @Test
    fun pendingReadsAreNotSentUsingAnotherAccountAndLogoutRemovesTheirLocalData() = runBlocking {
        dao.upsert(item("first").toEntity("employee"))
        online = false
        repository.acknowledgeCategory(NotificationCategory.FlightUpdates)
        subject.value = "other"
        online = true
        var calls = 0
        categoryRead = { _, _ -> calls++; CategoryReadReceiptDto("2026-11-09T12:00:00Z", 0) }
        repository.syncPendingReads()
        assertEquals(0, calls)
        assertEquals(0, repository.observeUnreadCount().first())
        repository.clearLocal()
        assertTrue(dao.pendingCategoryReads("employee").isEmpty())
        assertNull(dao.unreadTotal("employee"))
    }

    @Test
    fun aDetachedReadIntentCannotMoveToANewAccountWhileWaitingForDispatch() = runBlocking {
        val dispatcher = QueuedNotificationDispatcher()
        val delayedScope = CoroutineScope(SupervisorJob() + dispatcher)
        try {
            dao.upsert(item("old-account").toEntity("employee"))
            val delayed = createRepository(delayedScope)
            val request = async(start = CoroutineStart.UNDISPATCHED) { delayed.acknowledgeCategory(NotificationCategory.FlightUpdates) }
            delayed.clearLocal()
            subject.value = "other"
            dao.upsert(item("new-account").toEntity("other"))
            dispatcher.runPending()
            assertNull(request.await())
            assertFalse(dao.getById("new-account", "other")!!.isRead)
            assertTrue(dao.pendingCategoryReads("other").isEmpty())
        } finally {
            delayedScope.cancel()
        }
    }

    @Test
    fun categoryMappingIncludesAssignmentsScheduleChangesAndUnknownLegacyKinds() {
        assertTrue(NotificationCategory.Invitations.includes(NotificationKinds.EmployeeInvitedToFlight))
        assertTrue(NotificationCategory.Reminders.includes(NotificationKinds.FlightReminder))
        for (kind in listOf(NotificationKinds.StaffAssignedToFlight, NotificationKinds.FlightScheduleUpdated, "LegacyKind")) {
            assertTrue(NotificationCategory.FlightUpdates.includes(kind))
            assertFalse(NotificationCategory.Invitations.includes(kind))
            assertFalse(NotificationCategory.Reminders.includes(kind))
        }
    }

    private fun item(id: String, kind: String = NotificationKinds.StaffAssignedToFlight) = NotificationDto(
        id, kind, "Title", "Body", "عنوان", "نص", isRead = false, createdAtUtc = "2026-11-09T10:00:00Z",
    )
}

private class QueuedNotificationDispatcher : CoroutineDispatcher() {
    private val pending = ArrayDeque<Runnable>()
    override fun dispatch(context: CoroutineContext, block: Runnable) { pending.addLast(block) }
    fun runPending() { while (pending.isNotEmpty()) pending.removeFirst().run() }
}

private class MemoryNotificationDao : NotificationDao {
    private val rows = MutableStateFlow<List<NotificationEntity>>(emptyList())
    private val totals = MutableStateFlow<Map<String, NotificationUnreadStateEntity>>(emptyMap())
    private val categoryReads = mutableMapOf<Pair<String, String>, NotificationCategoryReadEntity>()
    override fun observeUnreadTotal(recipientUserId: String): Flow<NotificationUnreadStateEntity?> = totals.map { it[recipientUserId] }
    override suspend fun unreadTotal(recipientUserId: String) = totals.value[recipientUserId]
    override suspend fun upsertUnreadTotal(row: NotificationUnreadStateEntity) { totals.value = totals.value + (row.recipientUserId to row) }
    override suspend fun deleteUnreadTotals() { totals.value = emptyMap() }
    override suspend fun categoryRead(recipientUserId: String, category: String) = categoryReads[recipientUserId to category]
    override suspend fun pendingCategoryReads(recipientUserId: String) = categoryReads.values.filter { it.recipientUserId == recipientUserId && it.pending }
    override suspend fun upsertCategoryRead(row: NotificationCategoryReadEntity) { categoryReads[row.recipientUserId to row.category] = row }
    override suspend fun deleteCategoryReads() { categoryReads.clear() }
    override fun observeAll(recipientUserId: String): Flow<List<NotificationEntity>> = rows.map { list ->
        list.filter { it.recipientUserId == recipientUserId && !it.isArchived }.sortedByDescending { it.createdAtUtc }
    }
    override fun observeUnreadCount(recipientUserId: String): Flow<Int> = observeAll(recipientUserId).map { list -> list.count { !it.isRead } }
    override suspend fun getById(id: String, recipientUserId: String) = rows.value.firstOrNull { it.id == id && it.recipientUserId == recipientUserId }
    override suspend fun upsert(row: NotificationEntity) { rows.value = rows.value.filterNot { it.id == row.id } + row }
    override suspend fun upsertAll(rows: List<NotificationEntity>) { rows.forEach { upsert(it) } }
    override suspend fun markRead(id: String, recipientUserId: String, readAtUtc: String) {
        rows.value = rows.value.map { if (it.id == id && it.recipientUserId == recipientUserId) it.copy(isRead = true, readAtUtc = readAtUtc) else it }
    }
    override suspend fun markReadSnapshot(recipientUserId: String, ids: List<String>, readAtUtc: String) {
        rows.value = rows.value.map { if (it.id in ids && it.recipientUserId == recipientUserId && !it.isArchived) it.copy(isRead = true, readAtUtc = it.readAtUtc ?: readAtUtc) else it }
    }
    override suspend fun archive(id: String, recipientUserId: String) {
        rows.value = rows.value.map { if (it.id == id && it.recipientUserId == recipientUserId) it.copy(isArchived = true, isRead = true) else it }
    }
    override suspend fun archiveSnapshot(recipientUserId: String, ids: List<String>) {
        rows.value = rows.value.map { if (it.id in ids && it.recipientUserId == recipientUserId) it.copy(isArchived = true, isRead = true) else it }
    }
    override suspend fun archivedIds(recipientUserId: String) = rows.value.filter { it.recipientUserId == recipientUserId && it.isArchived }.map { it.id }
    override suspend fun deleteVisible(recipientUserId: String) {
        rows.value = rows.value.filterNot { it.recipientUserId == recipientUserId && !it.isArchived }
    }
    override suspend fun deleteVisibleCategory(recipientUserId: String, category: String) {
        rows.value = rows.value.filterNot { it.recipientUserId == recipientUserId && !it.isArchived && NotificationCategory.valueOf(category).includes(it.kind) }
    }
    override suspend fun scrubArchivedTombstones() { /* Nothing displays archived rows in this fake. */ }
    override suspend fun deleteAllVisible() { rows.value = rows.value.filter { it.isArchived } }
}
