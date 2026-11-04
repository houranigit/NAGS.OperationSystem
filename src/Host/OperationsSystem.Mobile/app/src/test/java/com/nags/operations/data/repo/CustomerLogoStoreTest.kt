package com.nags.operations.data.repo

import com.nags.operations.data.CustomerLogoDownload

import java.io.File
import java.io.IOException
import java.nio.file.Files
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.CoroutineStart
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.NonCancellable
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.async
import kotlinx.coroutines.cancel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withContext
import org.junit.After
import org.junit.Assert.*
import org.junit.Test

class CustomerLogoStoreTest {
    private val folder = Files.createTempDirectory("customer-logo-store").toFile()
    private val owner = MutableStateFlow<String?>("http://branch/|account")
    private val online = MutableStateFlow(true)
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Unconfined)
    private val reads = mutableListOf<Pair<String, String?>>()
    private var fetch: suspend (String, String?) -> CustomerLogoDownload? = { _, version ->
        CustomerLogoDownload(version ?: "v1", (version ?: "v1").toByteArray())
    }

    @After
    fun tearDown() { scope.cancel(); folder.deleteRecursively() }

    private fun store(writer: ((File, ByteArray) -> Unit)? = null) = CustomerLogoStore(
        folder, { id, version -> reads += id to version; fetch(id, version) }, owner, { owner.value }, online,
        scope, Dispatchers.Unconfined, writer ?: ::writeLogoFileAtomically,
    )
    private suspend fun register(store: CustomerLogoStore, vararg versions: Pair<String, String?>) =
        store.reconcileFlights(versions.toMap(), store.beginMetadataRead(), complete = true)
    private fun images() = folder.walkTopDown().filter { it.extension == "img" }.toList()

    @Test
    fun unchangedCatalogAndFlightReadsKeepFilesAndRestartReusesThemOffline() = runBlocking {
        val first = store()
        register(first, "customer" to "v1")
        assertArrayEquals("v1".toByteArray(), first.load("customer"))
        folder.walkTopDown().filter { it.isFile }.forEach { assertTrue(it.setLastModified(2000L)) }
        val before = folder.walkTopDown().filter { it.isFile }.associate { it.name to it.lastModified() }
        first.reconcileCustomers(mapOf("customer" to "v1"), first.beginMetadataRead())
        first.reconcileFlights(mapOf("customer" to "v1"), first.beginMetadataRead(), complete = false)
        assertArrayEquals("v1".toByteArray(), first.load("customer"))
        assertEquals(before, folder.walkTopDown().filter { it.isFile }.associate { it.name to it.lastModified() })
        online.value = false
        val restarted = store()
        assertArrayEquals("v1".toByteArray(), restarted.load("customer"))
        assertEquals(listOf("customer" to "v1"), reads)
        online.value = true
        restarted.reconcileCustomers(mapOf("customer" to "v2"), restarted.beginMetadataRead())
        assertArrayEquals("v2".toByteArray(), restarted.load("customer"))
    }

    @Test
    fun changedVersionsReplaceOnlyTheirImageAndNoneRemovesOnlyThatCustomer() = runBlocking {
        val store = store()
        register(store, "one" to "v1", "two" to "v1")
        store.load("one")
        val firstName = images().single().name
        store.load("two")
        val retained = images().single { it.name != firstName }
        assertTrue(retained.setLastModified(2000L))
        val otherName = retained.name
        store.reconcileCustomers(mapOf("one" to "v2", "two" to "v1"), store.beginMetadataRead())
        fetch = { _, version -> if (version == "v2") throw IOException("Offline") else CustomerLogoDownload(version!!, version.toByteArray()) }
        assertArrayEquals("v1".toByteArray(), store.load("one"))
        assertEquals(2, images().size)
        fetch = { _, version -> CustomerLogoDownload(version!!, version.toByteArray()) }
        assertArrayEquals("v2".toByteArray(), store.load("one"))
        assertEquals(2, images().size)
        assertEquals(2000L, images().single { it.name == otherName }.lastModified())
        store.reconcileCustomers(mapOf("one" to "none"), store.beginMetadataRead())
        assertNull(store.load("one"))
        assertEquals(1, images().size)
        assertArrayEquals("v1".toByteArray(), store.load("two"))
    }

    @Test
    fun ownersAndApiEnvironmentsNeverReuseEachOthersFilesAndCleanupDeletesThem() = runBlocking {
        val store = store()
        register(store, "customer" to "v1"); store.load("customer")
        for (different in listOf("http://branch/|other", "http://other-api/|account")) {
            owner.value = different
            assertNull(store.load("customer"))
            register(store, "customer" to "v2")
            assertArrayEquals("v2".toByteArray(), store.load("customer"))
        }
        assertEquals(3, images().size)
        store.clearOwner("http://branch/|account")
        assertEquals(2, images().size)
        store.clearAll()
        assertFalse(folder.exists())
        assertNull(store.load("customer"))
    }

    @Test
    fun missingOrCorruptFilesAreRepairedWithoutChangingTheKnownVersion() = runBlocking {
        val store = store()
        register(store, "customer" to "v1"); store.load("customer")
        images().single().delete()
        assertArrayEquals("v1".toByteArray(), this@CustomerLogoStoreTest.store().load("customer"))
        images().single().writeBytes(byteArrayOf(9))
        assertArrayEquals("v1".toByteArray(), this@CustomerLogoStoreTest.store().load("customer"))
        assertEquals(listOf("customer" to "v1", "customer" to "v1", "customer" to "v1"), reads)
    }

    @Test
    fun imageCommitFollowedByManifestFailureKeepsOldFileAndRestartRemovesOrphan() = runBlocking {
        var failManifest = false
        val store = store { file, bytes ->
            if (failManifest && file.name == "manifest.json") throw IOException("Disk full")
            writeLogoFileAtomically(file, bytes)
        }
        register(store, "customer" to "v1"); store.load("customer")
        store.reconcileCustomers(mapOf("customer" to "v2"), store.beginMetadataRead())
        failManifest = true
        assertArrayEquals("v1".toByteArray(), store.load("customer"))
        assertEquals(2, images().size)
        online.value = false
        val restarted = this@CustomerLogoStoreTest.store()
        assertArrayEquals("v1".toByteArray(), restarted.load("customer"))
        assertEquals(1, images().size)
        assertFalse(folder.walkTopDown().any { it.extension == "tmp" })
        online.value = true
        assertArrayEquals("v2".toByteArray(), restarted.load("customer"))
        assertEquals(1, images().size)
    }

    @Test
    fun concurrentCustomersShareDownloadsAndChangedMetadataRejectsLateBytes() = runBlocking {
        val response = CompletableDeferred<CustomerLogoDownload?>()
        fetch = { _, version -> if (version == "v1") withContext(NonCancellable) { response.await() }
            else CustomerLogoDownload(version!!, version.toByteArray()) }
        val store = store()
        register(store, "customer" to "v1")
        val first = async(start = CoroutineStart.UNDISPATCHED) { runCatching { store.load("customer") } }
        val second = async(start = CoroutineStart.UNDISPATCHED) { runCatching { store.load("customer") } }
        assertEquals(1, reads.size)
        store.reconcileCustomers(mapOf("customer" to "v2"), store.beginMetadataRead())
        assertArrayEquals("v2".toByteArray(), store.load("customer"))
        response.complete(CustomerLogoDownload("v1", "v1".toByteArray()))
        assertTrue(first.await().isFailure)
        assertTrue(second.await().isFailure)
        assertArrayEquals("v2".toByteArray(), store.load("customer"))
        assertEquals(1, images().size)
        assertEquals(2, reads.size)
    }

    @Test
    fun startupWithoutAnOwnerDoesNotPurgePersistedImagesAndTransientMissesRetry() = runBlocking {
        val first = store()
        register(first, "customer" to "v1"); first.load("customer")
        owner.value = null
        val restarted = store()
        assertNull(restarted.load("customer"))
        assertEquals(1, images().size)
        owner.value = "http://branch/|account"
        assertArrayEquals("v1".toByteArray(), restarted.load("customer"))
        restarted.reconcileCustomers(mapOf("customer" to "v2"), restarted.beginMetadataRead())
        fetch = { _, _ -> null }
        assertArrayEquals("v1".toByteArray(), restarted.load("customer"))
        assertFalse(folder.walkTopDown().filter { it.name == "manifest.json" }.single().readText().contains("none"))
        fetch = { _, version -> CustomerLogoDownload(version!!, version.toByteArray()) }
        restarted.reconcileCustomers(mapOf("customer" to "v2"), restarted.beginMetadataRead())
        assertArrayEquals("v2".toByteArray(), restarted.load("customer"))
    }

    @Test
    fun anUnknownVersionDownloadFencesEarlierCatalogResponses() = runBlocking {
        val store = store()
        register(store, "customer" to null)
        val oldCatalog = store.beginMetadataRead()
        val response = CompletableDeferred<CustomerLogoDownload?>()
        fetch = { _, _ -> response.await() }
        val pending = async(start = CoroutineStart.UNDISPATCHED) { store.load("customer") }
        store.reconcileCustomers(mapOf("customer" to "old"), oldCatalog)
        response.complete(CustomerLogoDownload("new", "new".toByteArray()))
        assertArrayEquals("new".toByteArray(), pending.await())
        assertArrayEquals("new".toByteArray(), store.load("customer"))
        assertEquals(1, reads.size)
    }

    @Test
    fun aFailedGetDoesNotSuppressTheOnlySuccessfulCatalogObservation() = runBlocking {
        val store = store()
        register(store, "customer" to "v1")
        val earlierCatalog = store.beginMetadataRead()
        fetch = { _, _ -> throw IOException("Transient failure") }
        assertNull(store.load("customer"))
        store.reconcileCustomers(mapOf("customer" to "v2"), earlierCatalog)
        fetch = { _, version -> CustomerLogoDownload(version!!, version.toByteArray()) }
        assertArrayEquals("v2".toByteArray(), store.load("customer"))
    }

    @Test
    fun requestStartOrderWinsForBothMetadataReplyOrders() = runBlocking {
        for (olderRepliesFirst in listOf(true, false)) {
            val store = store()
            store.clearAll()
            register(store, "customer" to "v0")
            val older = store.beginMetadataRead()
            val newer = store.beginMetadataRead()
            if (olderRepliesFirst) {
                store.reconcileCustomers(mapOf("customer" to "v1"), older)
                store.reconcileCustomers(mapOf("customer" to "v2"), newer)
            } else {
                store.reconcileCustomers(mapOf("customer" to "v2"), newer)
                store.reconcileCustomers(mapOf("customer" to "v1"), older)
            }
            assertArrayEquals("v2".toByteArray(), store.load("customer"))
        }
    }

    @Test
    fun onlyNewerFlightReadsCanPreserveMembershipAgainstAnOlderCompleteRegister() = runBlocking {
        val store = store()
        register(store, "old" to "v1")
        val full = store.beginMetadataRead()
        store.reconcileCustomers(mapOf("old" to "v2"), store.beginMetadataRead())
        store.reconcileFlights(mapOf("new" to "v1"), store.beginMetadataRead(), complete = false)
        store.reconcileFlights(emptyMap(), full, complete = true)
        assertNull(store.load("old"))
        assertArrayEquals("v1".toByteArray(), store.load("new"))
    }

    @Test
    fun aCompleteVisibilityCheckFencesOlderPartialAndFullRegisters() = runBlocking {
        val store = store()
        register(store, "customer" to "v1"); store.load("customer")
        val stale = store.beginMetadataRead()
        store.reconcileFlights(emptyMap(), store.beginMetadataRead(), complete = true)
        store.reconcileFlights(mapOf("customer" to "v1"), stale, complete = false)
        store.reconcileFlights(mapOf("customer" to "v1"), stale, complete = true)
        assertNull(store.load("customer"))
        assertTrue(images().isEmpty())
        assertEquals(1, reads.size)
    }

    @Test
    fun lateMetadataCannotUndoRemovalOrRestoreFilesAfterSessionCleanup() = runBlocking {
        val store = store()
        register(store, "customer" to "v1"); store.load("customer")
        val stale = store.beginMetadataRead()
        store.reconcileCustomers(mapOf("customer" to "none"), store.beginMetadataRead())
        store.reconcileFlights(mapOf("customer" to "v1"), stale, complete = false)
        assertNull(store.load("customer"))
        assertTrue(images().isEmpty())
        store.clearAll()
        store.reconcileFlights(mapOf("customer" to "v1"), stale, complete = true)
        assertNull(store.load("customer"))
        assertTrue(images().isEmpty())
    }
}
