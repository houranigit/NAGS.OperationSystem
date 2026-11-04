package com.nags.operations.ui.flights

import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.CoroutineStart
import kotlinx.coroutines.Deferred
import kotlinx.coroutines.async
import kotlinx.coroutines.sync.Semaphore
import kotlinx.coroutines.sync.withPermit

/** Session-memory thumbnails share customer requests; refresh discards misses and replaced logos. */
internal class CustomerLogoCache(
    private val scope: CoroutineScope,
    private val fetch: suspend (String) -> ByteArray?,
    private val maxBytes: Int = 4 * 1024 * 1024,
    private val maxEntries: Int = 24,
) {
    private val lock = Any()
    private val downloads = Semaphore(4)
    private val cache = LinkedHashMap<String, ByteArray?>(16, 0.75f, true)
    private val pending = mutableMapOf<String, Deferred<ByteArray?>>()
    private var byteCount = 0
    private var generation = 0L

    suspend fun load(customerId: String): ByteArray? {
        val (request, version) = synchronized(lock) {
            if (cache.containsKey(customerId)) return cache[customerId]
            val version = generation
            val request = pending[customerId] ?: run {
                lateinit var task: Deferred<ByteArray?>
                task = scope.async(start = CoroutineStart.LAZY) {
                    try {
                        val bytes = try {
                            downloads.withPermit { fetch(customerId) }
                        } catch (cancelled: CancellationException) {
                            throw cancelled
                        } catch (_: Exception) {
                            null
                        }
                        synchronized(lock) {
                            if (generation != version) throw CancellationException("Customer logos refreshed")
                            cache[customerId] = bytes
                            byteCount += bytes?.size ?: 0
                            val entries = cache.entries.iterator()
                            while (cache.size > maxEntries || byteCount > maxBytes) {
                                byteCount -= entries.next().value?.size ?: 0
                                entries.remove()
                            }
                        }
                        bytes
                    } finally {
                        synchronized(lock) {
                            if (pending[customerId] === task) pending.remove(customerId)
                        }
                    }
                }
                pending[customerId] = task
                task
            }
            request to version
        }
        val bytes = request.await()
        synchronized(lock) {
            if (generation != version) throw CancellationException("Customer logos refreshed")
        }
        return bytes
    }

    fun clear() {
        val requests = synchronized(lock) {
            generation++
            cache.clear()
            byteCount = 0
            pending.values.toList().also { pending.clear() }
        }
        requests.forEach { it.cancel() }
    }
}
