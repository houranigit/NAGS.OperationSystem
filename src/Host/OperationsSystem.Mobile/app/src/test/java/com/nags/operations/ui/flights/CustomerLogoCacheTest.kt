package com.nags.operations.ui.flights

import java.io.IOException
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.CoroutineStart
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.NonCancellable
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.async
import kotlinx.coroutines.cancel
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withContext
import org.junit.After
import org.junit.Assert.*
import org.junit.Test

class CustomerLogoCacheTest {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Unconfined)

    @After
    fun tearDown() = scope.cancel()

    @Test
    fun concurrentCardsShareARequestAndOneLeavingDoesNotCancelOtherCards() = runBlocking {
        var reads = 0
        val response = CompletableDeferred<ByteArray?>()
        val cache = CustomerLogoCache(scope, { reads++; response.await() })
        val first = async(start = CoroutineStart.UNDISPATCHED) { cache.load("customer") }
        val second = async(start = CoroutineStart.UNDISPATCHED) { cache.load("customer") }
        assertEquals(1, reads)
        first.cancel()
        response.complete(byteArrayOf(1, 2))
        assertArrayEquals(byteArrayOf(1, 2), second.await())
        assertArrayEquals(byteArrayOf(1, 2), cache.load("customer"))
        assertEquals(1, reads)
    }

    @Test
    fun refreshCancelsOldWaitersAndLateBytesCannotReplaceTheNewLogo() = runBlocking {
        var reads = 0
        val oldResponse = CompletableDeferred<ByteArray?>()
        val cache = CustomerLogoCache(scope, {
            if (++reads == 1) withContext(NonCancellable) { oldResponse.await() } else byteArrayOf(2)
        })
        val old = async(start = CoroutineStart.UNDISPATCHED) { runCatching { cache.load("customer") } }
        cache.clear()
        assertArrayEquals(byteArrayOf(2), cache.load("customer"))
        oldResponse.complete(byteArrayOf(1))
        assertTrue(old.await().isFailure)
        assertArrayEquals(byteArrayOf(2), cache.load("customer"))
        assertEquals(2, reads)
    }

    @Test
    fun byteBudgetEvictsOldEntriesAndMissingResultsHaveABoundedEntryCount() = runBlocking {
        val reads = mutableMapOf<String, Int>()
        val cache = CustomerLogoCache(scope, {
            reads[it] = (reads[it] ?: 0) + 1
            byteArrayOf(1, 2)
        }, maxBytes = 3, maxEntries = 2)
        cache.load("one")
        cache.load("two")
        cache.load("two")
        cache.load("one")
        assertEquals(mapOf("one" to 2, "two" to 1), reads)

        var misses = 0
        val missing = CustomerLogoCache(scope, { misses++; throw IOException() }, maxEntries = 1)
        assertNull(missing.load("one"))
        assertNull(missing.load("one"))
        assertEquals(1, misses)
        missing.load("two")
        missing.load("one")
        assertEquals(3, misses)
    }
}
