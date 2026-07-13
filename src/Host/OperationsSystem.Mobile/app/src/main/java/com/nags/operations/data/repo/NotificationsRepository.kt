package com.nags.operations.data.repo

import com.nags.operations.data.TokenStore
import com.nags.operations.data.api.NotificationsApi
import com.nags.operations.data.db.AppDatabase
import com.nags.operations.data.db.entities.toDto
import com.nags.operations.data.db.entities.toEntity
import com.nags.operations.data.notifications.NotificationDto
import com.nags.operations.data.notifications.NotificationPageDto
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.flatMapLatest
import kotlinx.coroutines.flow.flowOf
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import java.time.OffsetDateTime
import java.time.ZoneOffset
import java.util.concurrent.atomic.AtomicLong

@OptIn(ExperimentalCoroutinesApi::class)
class NotificationsRepository(
    private val api: NotificationsApi,
    private val db: AppDatabase,
    private val tokenStore: TokenStore,
) {
    private data class RemoteUnreadCount(val recipientUserId: String, val count: Int)

    private val remoteUnreadCount = MutableStateFlow<RemoteUnreadCount?>(null)
    private val writeMutex = Mutex()
    private val unreadRequestVersions = LatestRequestVersion()

    fun observeInbox(unreadOnly: Boolean): Flow<List<NotificationDto>> =
        tokenStore.sessionSubjectFlow.flatMapLatest { subject ->
            if (subject.isNullOrBlank()) {
                flowOf(emptyList())
            } else {
                db.notificationDao().observeAll(subject).map { rows ->
                    rows.asSequence()
                        .map { it.toDto() }
                        .filter { !unreadOnly || !it.isRead }
                        .toList()
                }
            }
        }

    fun observeUnreadCount(): Flow<Int> =
        tokenStore.sessionSubjectFlow.flatMapLatest { subject ->
            if (subject.isNullOrBlank()) {
                flowOf(0)
            } else {
                combine(db.notificationDao().observeUnreadCount(subject), remoteUnreadCount) { local, remote ->
                    remote?.takeIf { it.recipientUserId.equals(subject, ignoreCase = true) }?.count ?: local
                }
            }
        }.distinctUntilChanged()

    suspend fun refresh(page: Int = 1, pageSize: Int = PAGE_SIZE): NotificationPageDto {
        val subject = tokenStore.getSessionSubject()
            ?.takeIf(String::isNotBlank)
            ?: error("A notification inbox refresh requires an authenticated subject.")
        val result = api.inbox(page, pageSize, unreadOnly = false)
        val rows = result.items.map { it.toEntity(subject) }
        writeMutex.withLock {
            if (!isCurrentSubject(subject)) return@withLock
            if (page == 1) {
                db.notificationDao().replaceAll(subject, rows)
            } else {
                val archived = db.notificationDao().archivedIds(subject).toHashSet()
                db.notificationDao().upsertAll(rows.filterNot { it.id in archived })
            }
        }
        if (isCurrentSubject(subject)) refreshUnreadCount()
        return result
    }

    suspend fun refreshUnreadCount() {
        val requestVersion = unreadRequestVersions.next()
        val subject = tokenStore.getSessionSubject()?.takeIf(String::isNotBlank) ?: run {
            if (unreadRequestVersions.isLatest(requestVersion)) remoteUnreadCount.value = null
            return
        }
        val count = api.unreadCount().coerceAtLeast(0)
        if (unreadRequestVersions.isLatest(requestVersion) && isCurrentSubject(subject)) {
            remoteUnreadCount.value = RemoteUnreadCount(subject, count)
        }
    }

    suspend fun ingest(notification: NotificationDto, recipientUserId: String?): Boolean {
        val currentSubject = tokenStore.getSessionSubject()?.takeIf(String::isNotBlank) ?: return false
        val recipient = recipientUserId?.takeIf(String::isNotBlank) ?: currentSubject
        if (!recipient.equals(currentSubject, ignoreCase = true)) return false

        val accepted = writeMutex.withLock {
            if (!isCurrentSubject(currentSubject)) return@withLock false
            val existing = db.notificationDao().getById(notification.id, currentSubject)
            if (existing?.isArchived == true) return@withLock false
            val incoming = notification.toEntity(currentSubject)
            db.notificationDao().upsert(
                existing?.let {
                    incoming.copy(
                        isRead = it.isRead,
                        readAtUtc = it.readAtUtc,
                    )
                } ?: incoming,
            )
            // A concurrent unread-count response may already include this push. Fall back to the
            // local row until an authoritative count is fetched instead of incrementing twice.
            unreadRequestVersions.next()
            remoteUnreadCount.value = null
            true
        }
        if (accepted) runCatching { refreshUnreadCount() }
        return accepted
    }

    suspend fun markRead(id: String) {
        val subject = tokenStore.getSessionSubject()?.takeIf(String::isNotBlank) ?: return
        api.markRead(id)
        writeMutex.withLock {
            if (!isCurrentSubject(subject)) return@withLock
            db.notificationDao().markRead(id, subject, OffsetDateTime.now(ZoneOffset.UTC).toString())
            unreadRequestVersions.next()
            remoteUnreadCount.value = null
        }
        refreshUnreadCountSafely()
    }

    suspend fun markAllRead() {
        val subject = tokenStore.getSessionSubject()?.takeIf(String::isNotBlank) ?: return
        api.markAllRead()
        writeMutex.withLock {
            if (!isCurrentSubject(subject)) return@withLock
            db.notificationDao().markAllRead(subject, OffsetDateTime.now(ZoneOffset.UTC).toString())
            unreadRequestVersions.next()
            remoteUnreadCount.value = RemoteUnreadCount(subject, 0)
        }
    }

    suspend fun archive(id: String) {
        val subject = tokenStore.getSessionSubject()?.takeIf(String::isNotBlank) ?: return
        api.archive(id)
        writeMutex.withLock {
            if (!isCurrentSubject(subject)) return@withLock
            db.notificationDao().archive(id, subject)
            unreadRequestVersions.next()
            remoteUnreadCount.value = null
        }
        refreshUnreadCountSafely()
    }

    suspend fun archiveAll() {
        val subject = tokenStore.getSessionSubject()?.takeIf(String::isNotBlank) ?: return
        api.archiveAll()
        writeMutex.withLock {
            if (!isCurrentSubject(subject)) return@withLock
            db.notificationDao().archiveAll(subject)
            unreadRequestVersions.next()
            remoteUnreadCount.value = RemoteUnreadCount(subject, 0)
        }
    }

    suspend fun clearLocal() {
        writeMutex.withLock {
            // Keep only stable-id/account tombstones so a delayed FCM retry cannot resurrect an
            // archived item after logout/login. Scrub all user-facing content before retention.
            db.notificationDao().scrubArchivedTombstones()
            db.notificationDao().deleteAllVisible()
            unreadRequestVersions.next()
            remoteUnreadCount.value = null
        }
    }

    private suspend fun refreshUnreadCountSafely() {
        runCatching { refreshUnreadCount() }
    }

    private suspend fun isCurrentSubject(expected: String): Boolean =
        tokenStore.getSessionSubject()?.equals(expected, ignoreCase = true) == true

    companion object {
        const val PAGE_SIZE = 20
    }
}

internal class LatestRequestVersion {
    private val version = AtomicLong(0)

    fun next(): Long = version.incrementAndGet()

    fun isLatest(candidate: Long): Boolean = version.get() == candidate
}
