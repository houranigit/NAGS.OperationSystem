package com.nags.operations.data.repo

import com.nags.operations.data.TokenStore
import com.nags.operations.data.api.NotificationsApi
import com.nags.operations.data.db.NotificationsDatabase
import com.nags.operations.data.db.dao.NotificationDao
import com.nags.operations.data.db.entities.NotificationEntity
import com.nags.operations.data.db.entities.NotificationCategoryReadEntity
import com.nags.operations.data.db.entities.NotificationUnreadStateEntity
import com.nags.operations.data.db.entities.toDto
import com.nags.operations.data.db.entities.toEntity
import com.nags.operations.data.notifications.NotificationDto
import com.nags.operations.data.notifications.NotificationCategory
import com.nags.operations.data.notifications.NotificationPageDto
import com.nags.operations.data.notifications.CategoryReadReceiptDto
import com.nags.operations.data.notifications.CategoryReadResult
import com.nags.operations.data.notifications.CategoryReadAcknowledgement
import com.nags.operations.data.ApiException
import io.ktor.client.plugins.HttpRequestTimeoutException
import java.io.IOException
import java.time.Instant
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.launch
import kotlinx.coroutines.async
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.filter
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.flatMapLatest
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.flowOf
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import java.time.OffsetDateTime
import java.time.ZoneOffset
import java.util.concurrent.atomic.AtomicLong

@OptIn(ExperimentalCoroutinesApi::class)
class NotificationsRepository internal constructor(
    private val fetchInbox: suspend (Int, Int, NotificationCategory?) -> NotificationPageDto,
    private val fetchUnreadCount: suspend () -> Int,
    private val sendMarkRead: suspend (String) -> Unit,
    private val sendMarkAllRead: suspend () -> Unit,
    private val sendArchive: suspend (String) -> Unit,
    private val sendArchiveAll: suspend () -> Unit,
    private val dao: NotificationDao,
    private val subjectFlow: Flow<String?>,
    private val currentSubject: suspend () -> String?,
    private val appScope: CoroutineScope,
    private val sendCategoryRead: suspend (NotificationCategory, String) -> CategoryReadReceiptDto = { _, _ -> error("Category reads are not configured.") },
    private val isOnline: () -> Boolean = { true },
) {
    constructor(api: NotificationsApi, db: NotificationsDatabase, tokenStore: TokenStore, appScope: CoroutineScope, online: StateFlow<Boolean>) : this(
        fetchInbox = { page, size, category -> api.inbox(page, size, unreadOnly = false, category = category) },
        fetchUnreadCount = api::unreadCount,
        sendMarkRead = api::markRead,
        sendMarkAllRead = api::markAllRead,
        sendArchive = api::archive,
        sendArchiveAll = api::archiveAll,
        dao = db.notificationDao(),
        subjectFlow = tokenStore.sessionSubjectFlow,
        currentSubject = tokenStore::getSessionSubject,
        appScope = appScope,
        sendCategoryRead = api::readCategory,
        isOnline = { online.value },
    )

    private data class RemoteUnreadCount(val recipientUserId: String, val count: Int, val revision: Long)
    private data class ReadAcknowledgement(val recipientUserId: String, val value: CategoryReadAcknowledgement)

    private val remoteUnreadCount = MutableStateFlow<RemoteUnreadCount?>(null)
    private val readAcknowledgements = MutableSharedFlow<ReadAcknowledgement>(extraBufferCapacity = 64)
    private val writeMutex = Mutex()
    private val readSyncMutex = Mutex()
    private val sessionGeneration = AtomicLong(0)
    private val unreadRequestVersions = LatestRequestVersion()
    private val inboxRequestVersions: Map<NotificationCategory?, LatestRequestVersion> =
        (NotificationCategory.entries + listOf(null)).associateWith { LatestRequestVersion() }
    private val cacheRequestVersions = LatestRequestVersion()

    fun observeReadAcknowledgements(): Flow<CategoryReadAcknowledgement> = subjectFlow.flatMapLatest { subject ->
        readAcknowledgements.filter { it.recipientUserId.equals(subject, ignoreCase = true) }.map { it.value }
    }

    fun observeInbox(unreadOnly: Boolean, category: NotificationCategory? = null): Flow<List<NotificationDto>> =
        subjectFlow.flatMapLatest { subject ->
            if (subject.isNullOrBlank()) {
                flowOf(emptyList())
            } else {
                dao.observeAll(subject).map { rows ->
                    rows.asSequence()
                        .map { it.toDto() }
                        .filter { !unreadOnly || !it.isRead }
                        .filter { category == null || category.includes(it.kind) }
                        .toList()
                }
            }
        }

    fun observeUnreadCount(): Flow<Int> =
        subjectFlow.flatMapLatest { subject ->
            if (subject.isNullOrBlank()) {
                flowOf(0)
            } else {
                combine(dao.observeUnreadCount(subject), dao.observeUnreadTotal(subject), remoteUnreadCount) { local, saved, remote ->
                    remote?.takeIf { it.recipientUserId.equals(subject, ignoreCase = true) }?.count ?: saved?.count ?: local
                }
            }
        }.distinctUntilChanged()

    suspend fun refresh(page: Int = 1, pageSize: Int = PAGE_SIZE, category: NotificationCategory? = null): NotificationPageDto {
        if (category == null) inboxRequestVersions.values.forEach { it.next() }
        val versions = inboxRequestVersions.getValue(category)
        val requestVersion = versions.next()
        val cacheVersion = cacheRequestVersions.next()
        val subject = currentSubject()
            ?.takeIf(String::isNotBlank)
            ?: error("A notification inbox refresh requires an authenticated subject.")
        val initialIds = writeMutex.withLock { dao.observeAll(subject).first().map { it.id }.toHashSet() }
        syncPendingReads()
        val result = try {
            if (!isOnline()) throw IOException("Notifications are available from the device cache.")
            fetchInbox(page, pageSize, category)
        } catch (error: Exception) {
            if (error is CancellationException) throw error
            if (error !is IOException && error !is HttpRequestTimeoutException && (error !is ApiException || error.statusCode < 500)) throw error
            if (!isCurrentSubject(subject)) throw error
            val cached = dao.observeAll(subject).first().filter { category == null || category.includes(it.kind) }
            if (cached.isEmpty()) throw error
            return NotificationPageDto(cached.map { it.toDto() }, page, pageSize, cached.size.toLong(),
                totalPages = page, isFromCache = true)
        }
        writeMutex.withLock {
            if (!isCurrentSubject(subject)) return@withLock
            if (!versions.isLatest(requestVersion) || (category == null && !cacheRequestVersions.isLatest(cacheVersion))) return@withLock
            val rows = result.items.map { mergeIncoming(it.toEntity(subject), dao.getById(it.id, subject)) }
            if (page == 1) {
                val receivedWhileLoading = dao.observeAll(subject).first().filter {
                    (category == null || category.includes(it.kind)) && rows.none { row -> row.id == it.id } &&
                        (it.id !in initialIds || (result.hasNextPage && rows.lastOrNull()?.let { oldest ->
                            !instant(it.createdAtUtc).isAfter(instant(oldest.createdAtUtc))
                        } == true))
                }
                dao.replaceAll(subject, rows + receivedWhileLoading, category?.name)
            } else {
                val archived = dao.archivedIds(subject).toHashSet()
                dao.upsertAll(rows.filterNot { it.id in archived })
            }
        }
        if (isCurrentSubject(subject)) refreshUnreadCountSafely()
        return result
    }

    suspend fun refreshUnreadCount() {
        val subject = currentSubject()?.takeIf(String::isNotBlank) ?: run {
            remoteUnreadCount.value = null
            return
        }
        syncPendingReads()
        if (dao.pendingCategoryReads(subject).isNotEmpty() || !isOnline()) return
        val requestVersion = unreadRequestVersions.next()
        val count = fetchUnreadCount().coerceAtLeast(0)
        writeMutex.withLock {
            if (unreadRequestVersions.isLatest(requestVersion) && isCurrentSubject(subject)) {
                setUnreadCount(subject, count, requestVersion)
            }
        }
    }

    /** Persist locally first; the application-owned job continues when the inbox is closed. */
    suspend fun acknowledgeCategory(category: NotificationCategory): CategoryReadResult? {
        // Bind the intent to the caller's account before the detached job can be delayed.
        val generation = sessionGeneration.get()
        val subject = currentSubject()?.takeIf(String::isNotBlank) ?: return null
        return appScope.async {
            val queued = writeMutex.withLock {
                if (!isCurrentSubject(subject) || sessionGeneration.get() != generation) return@withLock null
                val rows = dao.observeAll(subject).first().filter { category.includes(it.kind) }
                val latest = rows.maxByOrNull { instant(it.createdAtUtc) } ?: return@withLock null
                val previous = dao.categoryRead(subject, category.name)
                val cutoff = previous?.throughCreatedAtUtc?.takeIf { instant(it).isAfter(instant(latest.createdAtUtc)) }
                    ?: latest.createdAtUtc
                val unreadIds = rows.filterNot { it.isRead }.map { it.id }
                val readAt = OffsetDateTime.now(ZoneOffset.UTC).toString()
                val pending = NotificationCategoryReadEntity(subject, category.name, cutoff, readAt,
                    pending = true, unreadDelta = (previous?.takeIf { it.pending }?.unreadDelta ?: 0) + unreadIds.size)
                val observed = remoteUnreadCount.value?.takeIf { it.recipientUserId.equals(subject, ignoreCase = true) }
                val count = observed?.count ?: dao.unreadTotal(subject)?.count ?: dao.observeUnreadCount(subject).first()
                val total = NotificationUnreadStateEntity(subject, (count - unreadIds.size).coerceAtLeast(0))
                dao.queueCategoryRead(pending, unreadIds, total)
                unreadRequestVersions.next()
                remoteUnreadCount.value = RemoteUnreadCount(subject, total.count, observed?.revision ?: 0)
                pending
            } ?: return@async null
            syncPendingReads()
            val acknowledged = dao.categoryRead(subject, category.name)
            CategoryReadResult(queued.throughCreatedAtUtc, queued.localReadAtUtc,
                acknowledged?.takeIf { !it.pending && it.throughCreatedAtUtc == queued.throughCreatedAtUtc }?.serverReadAtUtc)
        }.await()
    }

    suspend fun syncPendingReads() {
        if (!isOnline()) return
        try {
            readSyncMutex.withLock {
                val subject = currentSubject()?.takeIf(String::isNotBlank) ?: return@withLock
                val generation = sessionGeneration.get()
                while (isCurrentSubject(subject) && sessionGeneration.get() == generation) {
                    val sent = dao.pendingCategoryReads(subject).firstOrNull() ?: break
                    val receipt = sendCategoryRead(NotificationCategory.valueOf(sent.category), sent.throughCreatedAtUtc)
                    writeMutex.withLock {
                        if (!isCurrentSubject(subject) || sessionGeneration.get() != generation) return@withLock
                        val current = dao.categoryRead(subject, sent.category) ?: return@withLock
                        val superseded = current.throughCreatedAtUtc != sent.throughCreatedAtUtc || current.localReadAtUtc != sent.localReadAtUtc
                        val completed = current.copy(pending = superseded,
                            unreadDelta = if (superseded) (current.unreadDelta - sent.unreadDelta).coerceAtLeast(0) else 0,
                            serverReadAtUtc = receipt.readAtUtc)
                        // Other devices may already have read other pending categories. The server
                        // count must not have their local deltas subtracted a second time.
                        val total = NotificationUnreadStateEntity(subject, receipt.unreadCount.coerceAtLeast(0))
                        dao.completeCategoryRead(completed, total)
                        val revision = unreadRequestVersions.next()
                        remoteUnreadCount.value = RemoteUnreadCount(subject, total.count, revision)
                        readAcknowledgements.tryEmit(ReadAcknowledgement(subject, CategoryReadAcknowledgement(
                            NotificationCategory.valueOf(sent.category), sent.localReadAtUtc, receipt.readAtUtc)))
                    }
                }
            }
        } catch (error: CancellationException) {
            throw error
        } catch (_: Exception) {
            // The durable cutoff remains pending for the next foreground/reconnect/refresh.
        }
    }

    suspend fun ingest(notification: NotificationDto, recipientUserId: String?): Boolean {
        val currentSubject = currentSubject()?.takeIf(String::isNotBlank) ?: return false
        val recipient = recipientUserId?.takeIf(String::isNotBlank) ?: currentSubject
        if (!recipient.equals(currentSubject, ignoreCase = true)) return false

        val accepted = writeMutex.withLock {
            if (!isCurrentSubject(currentSubject)) return@withLock false
            val existing = dao.getById(notification.id, currentSubject)
            if (existing?.isArchived == true) return@withLock false
            dao.upsert(mergeIncoming(notification.toEntity(currentSubject), existing))
            // Keep the last total until REST reconciles it: the cache holds only loaded pages,
            // and a concurrent count response may already include this push.
            unreadRequestVersions.next()
            true
        }
        if (accepted) refreshUnreadCountSafely()
        return accepted
    }

    /** Navigation must not cancel the read acknowledgement when the inbox leaves the stack. */
    fun markReadOnOpen(id: String) {
        appScope.launch {
            try {
                markRead(id)
            } catch (error: CancellationException) {
                throw error
            } catch (_: Exception) {
                // A failed acknowledgement stays unread and can be retried on the next open.
            }
        }
    }

    suspend fun markRead(id: String) {
        val subject = currentSubject()?.takeIf(String::isNotBlank) ?: return
        val observedCount = remoteUnreadCount.value
        sendMarkRead(id)
        writeMutex.withLock {
            if (!isCurrentSubject(subject)) return@withLock
            val wasUnread = dao.getById(id, subject)?.let { !it.isRead && !it.isArchived } == true
            dao.markRead(id, subject, OffsetDateTime.now(ZoneOffset.UTC).toString())
            adjustUnreadCount(subject, if (wasUnread) -1 else 0, observedCount)
        }
        if (isCurrentSubject(subject)) refreshUnreadCountSafely()
    }

    suspend fun markAllRead() {
        val subject = currentSubject()?.takeIf(String::isNotBlank) ?: return
        val ids = dao.observeAll(subject).first().map { it.id }
        sendMarkAllRead()
        writeMutex.withLock {
            if (!isCurrentSubject(subject)) return@withLock
            dao.markReadSnapshot(subject, ids, OffsetDateTime.now(ZoneOffset.UTC).toString())
            val revision = unreadRequestVersions.next()
            setUnreadCount(subject, dao.observeUnreadCount(subject).first(), revision)
        }
        if (isCurrentSubject(subject)) refreshUnreadCountSafely()
    }

    suspend fun archive(id: String) {
        val subject = currentSubject()?.takeIf(String::isNotBlank) ?: return
        val observedCount = remoteUnreadCount.value
        sendArchive(id)
        writeMutex.withLock {
            if (!isCurrentSubject(subject)) return@withLock
            val wasUnread = dao.getById(id, subject)?.let { !it.isRead && !it.isArchived } == true
            dao.archive(id, subject)
            adjustUnreadCount(subject, if (wasUnread) -1 else 0, observedCount)
        }
        if (isCurrentSubject(subject)) refreshUnreadCountSafely()
    }

    suspend fun archiveAll() {
        val subject = currentSubject()?.takeIf(String::isNotBlank) ?: return
        val ids = dao.observeAll(subject).first().map { it.id }
        sendArchiveAll()
        writeMutex.withLock {
            if (!isCurrentSubject(subject)) return@withLock
            dao.archiveSnapshot(subject, ids)
            val revision = unreadRequestVersions.next()
            setUnreadCount(subject, dao.observeUnreadCount(subject).first(), revision)
        }
        if (isCurrentSubject(subject)) refreshUnreadCountSafely()
    }

    suspend fun clearLocal() {
        writeMutex.withLock {
            // Keep only stable-id/account tombstones so a delayed FCM retry cannot resurrect an
            // archived item after logout/login. Scrub all user-facing content before retention.
            dao.scrubArchivedTombstones()
            dao.deleteAllVisible()
            dao.deleteCategoryReads()
            dao.deleteUnreadTotals()
            sessionGeneration.incrementAndGet()
            inboxRequestVersions.values.forEach { it.next() }
            cacheRequestVersions.next()
            unreadRequestVersions.next()
            remoteUnreadCount.value = null
        }
    }

    private suspend fun refreshUnreadCountSafely() {
        try {
            refreshUnreadCount()
        } catch (error: CancellationException) {
            throw error
        } catch (_: Exception) {
            // Preserve the last authoritative total when its endpoint is unavailable.
        }
    }

    private suspend fun adjustUnreadCount(subject: String, delta: Int, observedCount: RemoteUnreadCount?) {
        unreadRequestVersions.next()
        val current = remoteUnreadCount.value?.takeIf { it.recipientUserId.equals(subject, ignoreCase = true) }
            ?: dao.unreadTotal(subject)?.let { RemoteUnreadCount(subject, it.count, 0) }
        current?.let {
            // A count reconciled during the write may already include it; never subtract twice.
            val adjustment = if (it.revision == observedCount?.revision || (observedCount == null && it.revision == 0L)) delta else 0
            setUnreadCount(subject, (it.count + adjustment).coerceAtLeast(0), it.revision)
        }
    }

    private suspend fun setUnreadCount(subject: String, count: Int, revision: Long) {
        dao.upsertUnreadTotal(NotificationUnreadStateEntity(subject, count))
        remoteUnreadCount.value = RemoteUnreadCount(subject, count, revision)
    }

    private suspend fun mergeIncoming(incoming: NotificationEntity, existing: NotificationEntity?): NotificationEntity {
        if (existing?.isRead == true && !incoming.isRead)
            return incoming.copy(isRead = true, readAtUtc = existing.readAtUtc)
        val category = NotificationCategory.entries.first { it.includes(incoming.kind) }
        val read = incoming.recipientUserId?.let { dao.categoryRead(it, category.name) }
        if (!incoming.isRead && read != null && !instant(incoming.createdAtUtc).isAfter(instant(read.throughCreatedAtUtc))) {
            // An old FCM delivery can arrive after the category was acknowledged. Reapply its
            // cutoff idempotently so late persistence on the server is reconciled as well.
            if (!read.pending) dao.upsertCategoryRead(read.copy(pending = true, unreadDelta = 0))
            return incoming.copy(isRead = true, readAtUtc = read.serverReadAtUtc ?: read.localReadAtUtc)
        }
        return incoming
    }

    private fun instant(value: String): Instant = OffsetDateTime.parse(value).toInstant()

    private suspend fun isCurrentSubject(expected: String): Boolean =
        currentSubject()?.equals(expected, ignoreCase = true) == true

    companion object {
        const val PAGE_SIZE = 20
    }
}

internal class LatestRequestVersion {
    private val version = AtomicLong(0)

    fun next(): Long = version.incrementAndGet()

    fun isLatest(candidate: Long): Boolean = version.get() == candidate
}
