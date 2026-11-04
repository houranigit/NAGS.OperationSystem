package com.nags.operations.data.repo

import com.nags.operations.data.CustomerLogoDownload
import java.io.File
import java.io.FileOutputStream
import java.io.IOException
import java.nio.file.Files
import java.nio.file.StandardCopyOption
import java.security.MessageDigest
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineDispatcher
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.CoroutineStart
import kotlinx.coroutines.Deferred
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.NonCancellable
import kotlinx.coroutines.async
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.emitAll
import kotlinx.coroutines.flow.flatMapLatest
import kotlinx.coroutines.flow.flow
import kotlinx.coroutines.flow.flowOf
import kotlinx.coroutines.flow.transformLatest
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.Semaphore
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.sync.withPermit
import kotlinx.coroutines.withContext
import kotlinx.serialization.Serializable
import kotlinx.serialization.Transient
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json

internal data class LogoMetadataRead(val owner: String, val generation: Long, val revision: Long)

/** Private, owner-scoped logo files survive restarts; only changed metadata or access loss invalidates them. */
@OptIn(ExperimentalCoroutinesApi::class)
class CustomerLogoStore internal constructor(
    private val root: File,
    private val fetch: suspend (String, String?) -> CustomerLogoDownload?,
    private val ownerFlow: Flow<String?>,
    private val currentOwner: suspend () -> String?,
    private val online: StateFlow<Boolean>,
    private val scope: CoroutineScope,
    private val dispatcher: CoroutineDispatcher = Dispatchers.IO,
    private val writeFile: (File, ByteArray) -> Unit = ::writeLogoFileAtomically,
) {
    private val mutex = Mutex()
    private val downloads = Semaphore(4)
    private val json = Json { ignoreUnknownKeys = true }
    private var activeOwner: String? = null
    private var generation = 0L
    private var revision = 0L
    private var membershipRevision = 0L
    private var entries = emptyMap<String, LogoEntry>()
    private val pending = mutableMapOf<String, PendingLogo>()
    private val revoked = mutableMapOf<String, Long>()
    private val memory = LinkedHashMap<String, ByteArray>(16, 0.75f, true)
    private var memoryBytes = 0
    private val state = MutableStateFlow(LogoState())

    internal suspend fun beginMetadataRead(): LogoMetadataRead? = withContext(dispatcher) {
        mutex.withLock {
            val owner = currentOwner() ?: return@withLock null
            activateLocked(owner)
            LogoMetadataRead(owner, generation, ++revision)
        }
    }

    /** Customer options update asset versions, never broaden the scoped flight register's access. */
    internal suspend fun reconcileCustomers(versions: Map<String, String?>, read: LogoMetadataRead?) =
        reconcile(versions, read, completeFlights = false, addCustomers = false)

    /** Only a complete scoped register may remove customers that no longer have a visible flight. */
    internal suspend fun reconcileFlights(versions: Map<String, String?>, read: LogoMetadataRead?, complete: Boolean) =
        reconcile(versions, read, completeFlights = complete, addCustomers = true)

    private suspend fun reconcile(
        versions: Map<String, String?>,
        read: LogoMetadataRead?,
        completeFlights: Boolean,
        addCustomers: Boolean,
    ) = withContext(dispatcher) {
        if (read == null) return@withContext
        mutex.withLock {
            if (!currentLocked(read.owner, read.generation)) return@withLock
            if (completeFlights && read.revision < membershipRevision) return@withLock
            val next = entries.toMutableMap()
            if (completeFlights) {
                next.entries.removeAll { (id, entry) -> id !in versions && entry.flightRevision <= read.revision }
                membershipRevision = read.revision
            }
            for ((id, incoming) in versions) {
                val old = next[id]
                if (old == null && (!addCustomers || (!completeFlights && read.revision < membershipRevision) || read.revision < (revoked[id] ?: 0))) continue
                val previous = old ?: LogoEntry()
                if (addCustomers) revoked.remove(id)
                // A response started before a newer observation cannot roll its asset version back.
                val version = if (incoming != null && previous.metadataRevision <= read.revision) incoming else previous.version
                val record = if (version == NONE) previous.copy(version = NONE, savedVersion = null, contentHash = null)
                    else previous.copy(version = version)
                next[id] = record.copy(
                    metadataRevision = if (incoming != null && previous.metadataRevision <= read.revision) read.revision else previous.metadataRevision,
                    flightRevision = if (addCustomers) maxOf(previous.flightRevision, read.revision) else previous.flightRevision,
                    retry = if (version != NONE && (record.savedVersion == null || record.savedVersion != version)) previous.retry + 1 else previous.retry,
                )
            }
            replaceLocked(next)
        }
    }

    fun observe(customerId: String): Flow<ByteArray?> = ownerFlow.distinctUntilChanged().flatMapLatest { owner ->
        if (owner == null) flowOf(null) else flow {
            withContext(dispatcher) { mutex.withLock { if (currentOwner() == owner) activateLocked(owner) } }
            emitAll(combine(state, online) { snapshot, connected ->
                LogoRequest(owner, snapshot.generation, customerId,
                    snapshot.entries[customerId]?.copy(metadataRevision = 0, flightRevision = 0).takeIf { snapshot.owner == owner }, connected)
            }.distinctUntilChanged().transformLatest { request ->
                val cached = try { readCached(request) }
                    catch (cancelled: CancellationException) { throw cancelled }
                    catch (_: Exception) { null }
                emit(cached)
                if (needsDownload(request, cached)) emit(load(customerId))
            }.distinctUntilChanged())
        }
    }

    /** Shared by observers; failures retain the last good file and are retried on later metadata/connectivity checks. */
    internal suspend fun load(customerId: String): ByteArray? {
        val request = withContext(dispatcher) { mutex.withLock {
            val owner = currentOwner() ?: return@withLock null
            activateLocked(owner)
            LogoRequest(owner, generation, customerId, entries[customerId], online.value)
        } } ?: return null
        val cached = try { readCached(request) }
            catch (cancelled: CancellationException) { throw cancelled }
            catch (_: Exception) { null }
        if (!needsDownload(request, cached)) return cached
        try {
            download(request)
        } catch (cancelled: CancellationException) {
            throw cancelled
        } catch (_: Exception) {
            return readCached(request)
        }
        return withContext(dispatcher) { mutex.withLock {
            if (!currentLocked(request.owner, request.generation)) return@withLock null
            readSavedLocked(request.customerId, entries[request.customerId])
        } }
    }

    /** A denied detail only revokes customers with no other flight in the last complete scoped snapshot. */
    internal suspend fun revokeCustomers(ids: Set<String>) = withContext(dispatcher) {
        if (ids.isEmpty()) return@withContext
        mutex.withLock {
            val owner = currentOwner() ?: return@withLock
            activateLocked(owner)
            val read = ++revision
            ids.forEach { revoked[it] = read }
            replaceLocked(entries - ids)
        }
    }

    /** Called only for session/access cleanup; ordinary list/search/catalog reloads keep every good image. */
    suspend fun clearAll() = withContext(dispatcher) {
        mutex.withLock {
            generation++
            pending.values.forEach { it.task.cancel() }
            pending.clear()
            memory.clear()
            memoryBytes = 0
            entries = emptyMap()
            activeOwner = null
            root.deleteRecursively()
            publishLocked()
        }
    }

    internal suspend fun clearOwner(owner: String) = withContext(dispatcher) {
        mutex.withLock {
            if (activeOwner == owner) {
                generation++
                pending.values.forEach { it.task.cancel() }
                pending.clear()
                memory.clear()
                memoryBytes = 0
                entries = emptyMap()
                activeOwner = null
                publishLocked()
            }
            ownerDirectory(owner).deleteRecursively()
        }
    }

    private fun needsDownload(request: LogoRequest, cached: ByteArray?): Boolean = request.connected &&
        request.entry != null && request.entry.version != NONE &&
        (cached == null || (request.entry.version != null && request.entry.version != request.entry.savedVersion))

    private suspend fun readCached(request: LogoRequest): ByteArray? = withContext(dispatcher) {
        mutex.withLock {
            if (!currentLocked(request.owner, request.generation)) return@withLock null
            readSavedLocked(request.customerId, entries[request.customerId])
        }
    }

    private fun readSavedLocked(id: String, entry: LogoEntry?): ByteArray? {
        if (entry == null || entry.version == NONE || entry.savedVersion == null) return null
        val file = imageFile(activeOwner!!, id, entry.savedVersion)
        val key = file.name
        if (file.isFile) memory[key]?.let { return it }
        val bytes = if (file.isFile && file.length() in 1..MAX_FILE_BYTES.toLong()) runCatching { file.readBytes() }.getOrNull() else null
        if (bytes == null || digest(bytes) != entry.contentHash) {
            replaceLocked(entries + (id to entry.copy(savedVersion = null, contentHash = null)))
            return null
        }
        rememberLocked(key, bytes)
        return bytes
    }

    private suspend fun download(request: LogoRequest) {
        val job = withContext(dispatcher) { mutex.withLock {
            if (!currentLocked(request.owner, request.generation) || !entries.containsKey(request.customerId) ||
                entries[request.customerId]?.version != request.entry?.version)
                throw CancellationException("Customer logo metadata changed")
            pending[request.customerId]?.task ?: run {
                val downloadRead = ++revision
                lateinit var task: Deferred<Unit>
                task = scope.async(dispatcher, start = CoroutineStart.LAZY) {
                    try {
                        val result = downloads.withPermit { fetch(request.customerId, request.entry?.version) } ?: return@async
                        if (result.bytes.isEmpty() || result.bytes.size > MAX_FILE_BYTES || result.version.isBlank() || result.version == NONE)
                            throw IOException("Invalid customer logo response")
                        mutex.withLock {
                            if (!currentLocked(request.owner, request.generation) || !entries.containsKey(request.customerId))
                                throw CancellationException("Customer logo metadata changed")
                            val observed = entries.getValue(request.customerId)
                            if (observed.metadataRevision > downloadRead && observed.version != result.version)
                                throw CancellationException("Customer logo metadata changed")
                            if (request.entry?.version != null && result.version != request.entry.version)
                                throw IOException("Customer logo version changed")
                            val old = entries[request.customerId] ?: return@withLock
                            val file = imageFile(request.owner, request.customerId, result.version)
                            writeFile(file, result.bytes)
                            pending.remove(request.customerId)
                            replaceLocked(entries + (request.customerId to old.copy(version = result.version,
                                savedVersion = result.version, contentHash = digest(result.bytes), metadataRevision = maxOf(old.metadataRevision, downloadRead))))
                            rememberLocked(file.name, result.bytes)
                        }
                    } finally {
                        withContext(NonCancellable) { mutex.withLock {
                            if (pending[request.customerId]?.task === task) pending.remove(request.customerId)
                        } }
                    }
                }
                pending[request.customerId] = PendingLogo(downloadRead, task)
                task
            }
        } }
        job.await()
    }

    private suspend fun currentLocked(owner: String, expectedGeneration: Long): Boolean =
        activeOwner == owner && generation == expectedGeneration && currentOwner() == owner

    private fun activateLocked(owner: String) {
        if (activeOwner == owner) return
        generation++
        revision = 0
        membershipRevision = 0
        revoked.clear()
        pending.values.forEach { it.task.cancel() }
        pending.clear()
        memory.clear()
        memoryBytes = 0
        activeOwner = owner
        val directory = ownerDirectory(owner)
        val manifest = File(directory, MANIFEST)
        entries = runCatching {
            if (manifest.isFile) json.decodeFromString<LogoManifest>(manifest.readText()).also {
                require(it.format == 1)
            }.entries else emptyMap()
        }.getOrElse { directory.deleteRecursively(); emptyMap() }
        cleanupLocked()
        publishLocked()
    }

    /** Write the new index before deleting superseded files; a crash can leave only harmless orphan files. */
    private fun replaceLocked(next: Map<String, LogoEntry>) {
        val oldPersisted = json.encodeToString(LogoManifest(entries = entries))
        val nextPersisted = json.encodeToString(LogoManifest(entries = next))
        if (oldPersisted != nextPersisted)
            writeFile(File(ownerDirectory(activeOwner!!), MANIFEST), nextPersisted.toByteArray())
        for ((id, old) in entries) {
            val request = pending[id]
            if (id !in next || (next[id]?.version != old.version && request != null && next.getValue(id).metadataRevision > request.read))
                pending.remove(id)?.task?.cancel()
        }
        entries = next.toMap()
        cleanupLocked()
        publishLocked()
    }

    private fun cleanupLocked() {
        val owner = activeOwner ?: return
        val keep = entries.mapNotNull { (id, entry) -> entry.savedVersion?.let { imageFile(owner, id, it).name } }.toSet() + MANIFEST
        ownerDirectory(owner).listFiles()?.filter { it.name !in keep }?.forEach { it.delete() }
        val retained = memory.keys.filter { it !in keep }
        retained.forEach { memoryBytes -= memory.remove(it)?.size ?: 0 }
    }

    private fun publishLocked() { state.value = LogoState(activeOwner, generation, entries.toMap()) }

    private fun rememberLocked(key: String, bytes: ByteArray) {
        memoryBytes -= memory.remove(key)?.size ?: 0
        memory[key] = bytes
        memoryBytes += bytes.size
        val iterator = memory.entries.iterator()
        while (memoryBytes > 4 * 1024 * 1024 || memory.size > 24) {
            memoryBytes -= iterator.next().value.size
            iterator.remove()
        }
    }

    private fun ownerDirectory(owner: String) = File(root, digest(owner.toByteArray()))
    private fun imageFile(owner: String, id: String, version: String) =
        File(ownerDirectory(owner), "${digest(id.toByteArray())}-${digest(version.toByteArray())}.img")

    private companion object {
        const val NONE = "none"
        const val MAX_FILE_BYTES = 2 * 1024 * 1024
        const val MANIFEST = "manifest.json"
    }
}

@Serializable
private data class LogoManifest(val format: Int = 1, val entries: Map<String, LogoEntry> = emptyMap())
@Serializable
private data class LogoEntry(
    val version: String? = null,
    val savedVersion: String? = null,
    val contentHash: String? = null,
    @Transient val metadataRevision: Long = 0,
    @Transient val flightRevision: Long = 0,
    @Transient val retry: Int = 0,
)
private data class LogoState(val owner: String? = null, val generation: Long = 0, val entries: Map<String, LogoEntry> = emptyMap())
private data class PendingLogo(val read: Long, val task: Deferred<Unit>)
private data class LogoRequest(val owner: String, val generation: Long, val customerId: String, val entry: LogoEntry?, val connected: Boolean)
private fun digest(bytes: ByteArray): String = MessageDigest.getInstance("SHA-256").digest(bytes).joinToString("") { "%02x".format(it) }
internal fun writeLogoFileAtomically(file: File, bytes: ByteArray) {
    file.parentFile!!.mkdirs()
    val temporary = File.createTempFile("logo-", ".tmp", file.parentFile)
    try {
        FileOutputStream(temporary).use { it.write(bytes); it.fd.sync() }
        Files.move(temporary.toPath(), file.toPath(), StandardCopyOption.ATOMIC_MOVE, StandardCopyOption.REPLACE_EXISTING)
    } finally { temporary.delete() }
}
