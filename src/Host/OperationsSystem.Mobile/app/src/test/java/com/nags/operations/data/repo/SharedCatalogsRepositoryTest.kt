package com.nags.operations.data.repo

import com.nags.operations.data.ApiException
import com.nags.operations.data.SharedCatalog
import com.nags.operations.data.StaffCatalogItem
import com.nags.operations.data.StationStaffCatalogSnapshot
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.jsonObject
import com.nags.operations.data.db.dao.SharedCatalogDao
import com.nags.operations.data.db.entities.SharedCatalogSnapshotEntity
import java.io.IOException
import java.nio.file.Files
import com.nags.operations.data.CustomerLogoDownload
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CoroutineStart
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.launch
import kotlinx.coroutines.runBlocking
import org.junit.Assert.*
import org.junit.Test

class SharedCatalogsRepositoryTest {
    private val owner = MutableStateFlow<String?>("account-a")
    private val dao = MemoryCatalogDao()
    private var clock = "2026-11-03T10:00:00Z"
    private var elapsedNanos = 0L
    private var durationMs = 25L

    @Test
    fun taskResourceValidationReadsOneOwnerScopedSnapshotWithoutAnObservableSubscription() = runBlocking {
        val repository = repository()
        repository.refresh(setOf(SharedCatalog.Tools, SharedCatalog.Materials, SharedCatalog.GeneralSupports))
        dao.put(SharedCatalogSnapshotEntity("account-b", "tools", """[{"id":"foreign","name":"Foreign","calculationType":"Quantity"}]"""))
        val subscriptions = dao.observations
        val reads = dao.snapshotReads

        val catalogs = repository.taskResourceCatalogs()

        assertEquals(mapOf("tool" to 1), catalogs.tools)
        assertEquals(mapOf("material" to 0), catalogs.materials)
        assertEquals(mapOf("support" to 0), catalogs.generalSupports)
        assertEquals(subscriptions, dao.observations)
        assertEquals(reads + 1, dao.snapshotReads)
        owner.value = "account-b"
        assertEquals(mapOf("foreign" to 0), repository.taskResourceCatalogs().tools)
        assertTrue(repository.taskResourceCatalogs().materials.isEmpty())
        owner.value = null
        assertTrue(repository.taskResourceCatalogs().tools.isEmpty())
    }

    @Test
    fun taskResourceValidationOmitsDeniedMissingAndUnknownTypesWhileRetainingUsableOfflineData() = runBlocking {
        val repository = repository { throw IOException("Offline") }
        dao.put(SharedCatalogSnapshotEntity("account-a", "tools", payload(SharedCatalog.Tools), permissionDenied = true))
        dao.put(SharedCatalogSnapshotEntity("account-a", "materials",
            """[{"id":"numeric","name":"Numeric","calculationType":"1"},{"id":"unsupported","name":"Unsupported","calculationType":"Future"}]""",
            error = "Transient refresh failed", lastSuccessUtc = clock))
        dao.put(SharedCatalogSnapshotEntity("account-b", "general-supports", payload(SharedCatalog.GeneralSupports)))

        val catalogs = repository.taskResourceCatalogs()

        assertTrue(catalogs.tools.isEmpty())
        assertEquals(mapOf("numeric" to 1), catalogs.materials)
        assertTrue(catalogs.generalSupports.isEmpty())
    }

    @Test
    fun taskResourceSnapshotCannotReturnRowsAfterAnAccountSwitchOrLocalCacheRevocation() = runBlocking {
        val repository = repository()
        for (switchOwner in listOf(true, false)) {
            owner.value = "account-a"
            dao.put(SharedCatalogSnapshotEntity("account-a", "tools", payload(SharedCatalog.Tools)))
            dao.afterSnapshotRead = { if (switchOwner) owner.value = "account-b" else repository.clearLocal() }

            assertTrue(repository.taskResourceCatalogs().tools.isEmpty())

            dao.afterSnapshotRead = {}
        }
    }

    @Test
    fun legacyUnscopedStaffCacheIsHiddenUntilFreshScopedSyncAndThenSurvivesRestart() = runBlocking {
        val oldRows = Json.parseToJsonElement(payload(SharedCatalog.StationStaff)).jsonObject.getValue("items").toString()
        dao.put(SharedCatalogSnapshotEntity("account-a", "staff-members", oldRows, 1,
            lastSuccessUtc = clock, lastDurationMs = 25))
        val repo = repository()
        assertTrue(repo.stationStaffFlow().first().isEmpty())
        val stale = repo.observeStatus().first().single { it.kind == SharedCatalog.StationStaff }
        assertEquals(0, stale.itemCount)
        assertNull(stale.lastSuccessUtc)
        repo.refresh(setOf(SharedCatalog.StationStaff))
        assertEquals("station", repo.stationStaffFlow().first().single().stationId)
        val restarted = repository { throw IOException("Offline") }
        assertEquals("staff", restarted.stationStaffFlow().first().single().id)
        owner.value = "another-account"
        assertTrue(restarted.stationStaffFlow().first().isEmpty())
    }

    @Test
    fun crossStationRowsCannotReplaceTheLastAuthoritativelyScopedStaffSnapshot() = runBlocking {
        var malformed = false
        val repo = repository { kind ->
            if (kind == SharedCatalog.StationStaff && malformed) Json.encodeToString(StationStaffCatalogSnapshot(
                "current-station", "station", listOf(StaffCatalogItem("foreign", "Foreign", "E2", "other-station", "BBB", "Engineer"))))
            else payload(kind)
        }
        repo.refresh(setOf(SharedCatalog.StationStaff))
        val original = dao.snapshot("account-a", "staff-members")!!
        malformed = true
        repo.refresh(setOf(SharedCatalog.StationStaff))
        val retained = dao.snapshot("account-a", "staff-members")!!
        assertEquals(original.payloadJson, retained.payloadJson)
        assertFalse(retained.payloadJson.contains("other-station"))
        assertNotNull(retained.error)
        assertEquals(listOf("staff"), repo.stationStaffFlow().first().map { it.id })
    }

    @Test
    fun customerMetadataUpdatesOnlyChangedImagesAndCatalogDenialKeepsFlightAuthorizedFiles() = runBlocking {
        val folder = Files.createTempDirectory("catalog-logos").toFile()
        var reads = 0
        var version = "v1"
        var denied = false
        val logos = CustomerLogoStore(folder, { _, expected -> reads++; CustomerLogoDownload(expected!!, expected.toByteArray()) },
            owner, { owner.value }, MutableStateFlow(true), this, Dispatchers.Unconfined)
        val repository = SharedCatalogsRepository(
            fetchCatalog = { if (denied) throw ApiException(403, "Denied") else """[{"id":"customer","name":"Customer","logoVersion":"$version"}]""" },
            dao = dao, ownerFlow = owner, currentOwner = { owner.value }, now = { clock }, logoStore = logos,
        )
        try {
            logos.reconcileFlights(mapOf("customer" to "v1"), logos.beginMetadataRead(), complete = true)
            repository.refresh(setOf(SharedCatalog.Customers))
            assertArrayEquals("v1".toByteArray(), logos.load("customer"))
            repository.refresh(setOf(SharedCatalog.Customers))
            assertArrayEquals("v1".toByteArray(), logos.load("customer"))
            assertEquals(1, reads)
            assertEquals("v1", repository.customersFlow().first().single().logoVersion)
            assertEquals(1, repository.observeStatus().first().single { it.kind == SharedCatalog.Customers }.itemCount)
            denied = true
            repository.refresh(setOf(SharedCatalog.Customers))
            assertArrayEquals("v1".toByteArray(), logos.load("customer"))
            assertEquals(1, reads)
            denied = false; version = "v2"
            repository.refresh(setOf(SharedCatalog.Customers))
            assertArrayEquals("v2".toByteArray(), logos.load("customer"))
            assertEquals(2, reads)
            assertEquals(1, repository.observeStatus().first().single { it.kind == SharedCatalog.Customers }.itemCount)
            assertFalse(dao.snapshot("account-a", "customers")!!.payloadJson.contains("bytes"))
        } finally { folder.deleteRecursively() }
    }

    @Test
    fun successfulRefreshPersistsAllCanonicalCatalogsAndTheirMetadata() = runBlocking {
        val repository = repository()
        repository.refresh()

        assertEquals(SharedCatalog.entries.size, dao.rows.value.size)
        assertEquals("A320", repository.aircraftTypesFlow().first().single().model)
        assertNull(repository.customersFlow().first().single().iataCode)
        assertTrue(repository.servicesFlow().first().single().isAircraftPerLanding)
        assertEquals("Duration", repository.toolsFlow().first().single().calculationType)
        assertEquals("Quantity", repository.materialsFlow().first().single().calculationType)
        assertEquals("Support", repository.generalSupportsFlow().first().single().name)
        assertEquals("05", repository.ataChaptersFlow().first().single().code)
        assertTrue(repository.observeStatus().first().all {
            it.itemCount == 1 && it.lastSuccessUtc == clock && it.lastAttemptUtc == clock &&
                it.error == null && !it.permissionDenied && it.lastDurationMs == durationMs
        })
        // The options route's entity concurrency metadata is not part of the reference cache.
        assertFalse(dao.snapshot("account-a", "ata-chapters")!!.payloadJson.contains("rowVersion"))
        assertFalse(repository.isSyncing.value)
    }

    @Test
    fun targetedToolRefreshLeavesEveryOtherSnapshotAndItsMetadataUntouched() = runBlocking {
        val fetched = mutableListOf<SharedCatalog>()
        var toolName = "Tool"
        var customerFails = false
        val repository = repository { kind ->
            fetched += kind
            if (customerFails && kind == SharedCatalog.Customers) throw IOException("Offline")
            payload(kind).let { if (kind == SharedCatalog.Tools) it.replace("\"Tool\"", "\"$toolName\"") else it }
        }
        repository.refresh()
        customerFails = true
        repository.refresh(setOf(SharedCatalog.Customers))
        val previous = dao.rows.value.associateBy { it.catalog }
        assertNotNull(previous.getValue("customers").error)
        fetched.clear()
        dao.writes.clear()
        clock = "2026-11-03T10:01:00Z"
        durationMs = 71L
        toolName = "Updated tool"

        repository.refresh(setOf(SharedCatalog.Tools))

        assertEquals(listOf(SharedCatalog.Tools), fetched)
        assertEquals(listOf("tools"), dao.writes.map { it.catalog })
        assertEquals("Updated tool", repository.toolsFlow().first().single().name)
        val updated = dao.snapshot("account-a", "tools")!!
        assertEquals(clock, updated.lastAttemptUtc)
        assertEquals(clock, updated.lastSuccessUtc)
        assertEquals(71L, updated.lastDurationMs)
        for ((catalog, snapshot) in previous.filterKeys { it != "tools" }) {
            assertEquals(snapshot, dao.snapshot("account-a", catalog))
        }
        assertFalse(repository.isSyncing.value)
    }

    @Test
    fun staffRefreshCachesOnlyCanonicalOptionsAndLeavesOtherCatalogsUntouched() = runBlocking {
        val fetched = mutableListOf<SharedCatalog>()
        var staffName = "Employee"
        val repository = repository { kind ->
            fetched += kind
            payload(kind).let { if (kind == SharedCatalog.StationStaff) it.replace("Employee", staffName) else it }
        }
        repository.refresh()
        val previous = dao.rows.value.associateBy { it.catalog }
        fetched.clear()
        dao.writes.clear()
        clock = "2026-11-04T10:01:00Z"
        staffName = "Updated employee"

        repository.refresh(setOf(SharedCatalog.StationStaff))

        assertEquals(listOf(SharedCatalog.StationStaff), fetched)
        assertEquals(listOf("staff-members"), dao.writes.map { it.catalog })
        val staff = repository.stationStaffFlow().first().single()
        assertEquals("Updated employee", staff.fullName)
        assertEquals("EMP-1", staff.employeeId)
        assertEquals("station", staff.stationId)
        assertEquals("AAA", staff.stationCode)
        assertEquals("Technician", staff.manpowerTypeName)
        val snapshot = dao.snapshot("account-a", "staff-members")!!
        assertEquals(clock, snapshot.lastSuccessUtc)
        assertFalse(snapshot.payloadJson.contains("email"))
        for ((catalog, original) in previous.filterKeys { it != "staff-members" }) {
            assertEquals(original, dao.snapshot("account-a", catalog))
        }
    }

    @Test
    fun aNewStationStaffSnapshotRemovesPreviouslyVisibleStationRows() = runBlocking {
        var moved = false
        val repository = repository { kind ->
            payload(kind).let {
                if (moved && kind == SharedCatalog.StationStaff) {
                    it.replace("\"staff\"", "\"next-staff\"")
                        .replace("\"station\"", "\"new-station\"")
                } else it
            }
        }
        repository.refresh(setOf(SharedCatalog.StationStaff))
        assertEquals("station", repository.stationStaffFlow().first().single().stationId)
        moved = true

        repository.refresh(setOf(SharedCatalog.StationStaff))

        val staff = repository.stationStaffFlow().first()
        assertEquals(listOf("next-staff"), staff.map { it.id })
        assertEquals(listOf("new-station"), staff.map { it.stationId })
    }

    @Test
    fun deniedStaffRefreshEvictsStaffAndKeepsOtherCatalogSnapshots() = runBlocking {
        var denied = false
        val repository = repository { kind ->
            if (denied && kind == SharedCatalog.StationStaff) throw ApiException(403, "Denied")
            payload(kind)
        }
        repository.refresh()
        val previous = dao.rows.value.filter { it.catalog != "staff-members" }
        denied = true

        repository.refresh(setOf(SharedCatalog.StationStaff))

        assertTrue(repository.stationStaffFlow().first().isEmpty())
        val status = repository.observeStatus().first().single { it.kind == SharedCatalog.StationStaff }
        assertTrue(status.permissionDenied)
        assertEquals(0, status.itemCount)
        assertNull(status.lastSuccessUtc)
        for (snapshot in previous) {
            assertEquals(snapshot, dao.snapshot("account-a", snapshot.catalog))
        }
    }

    @Test
    fun subsetRefreshFetchesOnlyRequestedCatalogsInCanonicalOrder() = runBlocking {
        val fetched = mutableListOf<SharedCatalog>()
        val repository = repository { kind -> fetched += kind; payload(kind) }

        repository.refresh(linkedSetOf(SharedCatalog.Materials, SharedCatalog.AircraftTypes, SharedCatalog.Tools))

        assertEquals(listOf(SharedCatalog.AircraftTypes, SharedCatalog.Tools, SharedCatalog.Materials), fetched)
        assertEquals(listOf("aircraft-types", "tools", "materials"), dao.rows.value.map { it.catalog })
        assertTrue(repository.observeStatus().first().filter {
            it.kind !in setOf(SharedCatalog.AircraftTypes, SharedCatalog.Tools, SharedCatalog.Materials)
        }.all { it.lastSuccessUtc == null && it.itemCount == 0 })
    }

    @Test
    fun aTransientCatalogFailureRetainsItsSnapshotWhileOtherCatalogsRefresh() = runBlocking {
        var failing = false
        val repository = repository { kind ->
            if (failing && kind == SharedCatalog.Customers) throw IOException("Offline")
            payload(kind)
        }
        repository.refresh()
        val previous = dao.snapshot("account-a", "customers")!!
        // A wall-clock adjustment does not affect the monotonic download duration.
        clock = "2026-11-03T09:00:00Z"
        durationMs = 87L
        failing = true
        repository.refresh()

        val retained = dao.snapshot("account-a", "customers")!!
        assertEquals(previous.payloadJson, retained.payloadJson)
        assertEquals(previous.lastSuccessUtc, retained.lastSuccessUtc)
        assertEquals(previous.lastDurationMs, retained.lastDurationMs)
        assertEquals(25L, retained.lastDurationMs)
        assertEquals(clock, retained.lastAttemptUtc)
        assertNotNull(retained.error)
        assertEquals(clock, dao.snapshot("account-a", "aircraft-types")!!.lastSuccessUtc)
        assertEquals(87L, dao.snapshot("account-a", "aircraft-types")!!.lastDurationMs)
        assertEquals("Airline", repository.customersFlow().first().single().name)
    }

    @Test
    fun deniedCatalogAccessDropsOnlyThatCatalogAndItsLastSuccess() = runBlocking {
        var denied = false
        val repository = repository { kind ->
            if (denied && kind == SharedCatalog.Customers) throw ApiException(403, "Denied")
            payload(kind)
        }
        repository.refresh()
        val previousAircraft = dao.snapshot("account-a", "aircraft-types")!!
        denied = true
        repository.refresh(setOf(SharedCatalog.Customers))

        assertTrue(repository.customersFlow().first().isEmpty())
        assertEquals(1, repository.aircraftTypesFlow().first().size)
        assertEquals(previousAircraft, dao.snapshot("account-a", "aircraft-types"))
        val status = repository.observeStatus().first().single { it.kind == SharedCatalog.Customers }
        assertEquals(0, status.itemCount)
        assertNull(status.lastSuccessUtc)
        assertNull(status.lastDurationMs)
        assertTrue(status.permissionDenied)
        assertNotNull(status.error)
    }

    @Test
    fun unauthorizedResponseClearsPersistedCatalogsEvenIfHttpAlreadyRemovedTokens() = runBlocking {
        repository().refresh()
        assertTrue(dao.rows.value.isNotEmpty())
        val repository = repository {
            owner.value = null
            throw ApiException(401, "Expired session")
        }
        repository.refresh(setOf(SharedCatalog.Tools))

        assertTrue(dao.rows.value.isEmpty())
        assertTrue(repository.aircraftTypesFlow().first().isEmpty())
        assertFalse(repository.isSyncing.value)
    }

    @Test
    fun aLateResponseCannotWriteAfterAccountSwitchCleanup() = runBlocking {
        val response = CompletableDeferred<String>()
        var reads = 0
        val repository = repository {
            reads++
            response.await()
        }
        val pending = launch(start = CoroutineStart.UNDISPATCHED) { repository.refresh() }
        assertTrue(repository.isSyncing.value)
        owner.value = "account-b"
        repository.clearLocal()
        response.complete(payload(SharedCatalog.AircraftTypes))
        pending.join()

        assertTrue(dao.rows.value.isEmpty())
        assertTrue(repository.aircraftTypesFlow().first().isEmpty())
        assertEquals(1, reads)
    }

    @Test
    fun cleanupAlsoInvalidatesLateResponsesWhenTheSameAccountSignsBackIn() = runBlocking {
        val response = CompletableDeferred<String>()
        val repository = repository { response.await() }
        val pending = launch(start = CoroutineStart.UNDISPATCHED) { repository.refresh() }
        repository.clearLocal()
        response.complete(payload(SharedCatalog.AircraftTypes))
        pending.join()

        assertTrue(dao.rows.value.isEmpty())
    }

    @Test
    fun readFlowsNeverExposeAnotherAccountOrSignedOutCache() = runBlocking {
        val repository = repository()
        repository.refresh()
        owner.value = "account-b"
        assertTrue(repository.customersFlow().first().isEmpty())
        owner.value = null
        assertTrue(repository.customersFlow().first().isEmpty())
        assertTrue(repository.observeStatus().first().all { it.lastSuccessUtc == null && it.itemCount == 0 })
    }

    @Test
    fun copiedAccountIdsOnDifferentApiServersNeverShareCachedCatalogs() = runBlocking {
        fun forServer(baseUrl: String, model: String) = SharedCatalogsRepository(
            fetchCatalog = { kind -> payload(kind).replace("A320", model) },
            dao = dao,
            ownerFlow = owner.map { sharedCatalogOwnerKey(baseUrl, it) },
            currentOwner = { sharedCatalogOwnerKey(baseUrl, owner.value) },
            now = { clock },
        )
        val currentBranch = forServer("http://10.0.2.2:5213/", "Branch aircraft")
        val handover = forServer("http://10.0.2.2:5212", "Handover aircraft")
        currentBranch.refresh()

        assertTrue(handover.aircraftTypesFlow().first().isEmpty())
        handover.refresh()
        assertEquals("Branch aircraft", currentBranch.aircraftTypesFlow().first().single().model)
        assertEquals("Handover aircraft", handover.aircraftTypesFlow().first().single().model)
        assertEquals(SharedCatalog.entries.size * 2, dao.rows.value.size)
    }

    @Test
    fun malformedSuccessPayloadDoesNotReplaceTheLastValidSnapshot() = runBlocking {
        var malformed = false
        val repository = repository { kind ->
            if (malformed && kind == SharedCatalog.AircraftTypes) "[{\"id\":\"incomplete\"}]" else payload(kind)
        }
        repository.refresh()
        val previous = dao.snapshot("account-a", "aircraft-types")!!
        malformed = true
        repository.refresh()

        val retained = dao.snapshot("account-a", "aircraft-types")!!
        assertEquals(previous.payloadJson, retained.payloadJson)
        assertEquals(previous.lastSuccessUtc, retained.lastSuccessUtc)
        assertNotNull(retained.error)
        assertEquals("A320", repository.aircraftTypesFlow().first().single().model)
    }

    @Test
    fun concurrentRefreshRequestsDoNotRaceSnapshotReplacement() = runBlocking {
        val response = CompletableDeferred<String>()
        var reads = 0
        val repository = repository { kind ->
            reads++
            if (kind == SharedCatalog.AircraftTypes) response.await() else payload(kind)
        }
        val pending = launch(start = CoroutineStart.UNDISPATCHED) { repository.refresh() }
        repository.refresh()
        assertEquals(1, reads)
        response.complete(payload(SharedCatalog.AircraftTypes))
        pending.join()

        assertEquals(SharedCatalog.entries.size, reads)
        assertEquals(SharedCatalog.entries.size, dao.rows.value.size)
    }

    private fun repository(fetch: suspend (SharedCatalog) -> String = ::payload) = SharedCatalogsRepository(
        fetchCatalog = fetch, dao = dao, ownerFlow = owner, currentOwner = { owner.value }, now = { clock },
        monotonicNanos = { elapsedNanos.also { elapsedNanos += durationMs * 1_000_000L } },
    )

    private fun payload(kind: SharedCatalog): String = when (kind) {
        SharedCatalog.AircraftTypes -> """[{"id":"aircraft","manufacturer":"Airbus","model":"A320"}]"""
        SharedCatalog.Customers -> """[{"id":"customer","iataCode":null,"name":"Airline"}]"""
        SharedCatalog.Services -> """[{"id":"service","name":"Per Landing","isAircraftPerLanding":true}]"""
        SharedCatalog.Tools -> """[{"id":"tool","name":"Tool","calculationType":"Duration"}]"""
        SharedCatalog.Materials -> """[{"id":"material","name":"Material","calculationType":"Quantity"}]"""
        SharedCatalog.GeneralSupports -> """[{"id":"support","name":"Support","calculationType":"Quantity"}]"""
        SharedCatalog.AtaChapters -> """[{"id":"chapter","categoryId":"category","categoryName":"General","code":"05","title":"Time limits","isActive":true,"categoryIsActive":true,"rowVersion":"ignored"}]"""
        SharedCatalog.StationStaff -> """{"scope":"current-station","stationId":"station","items":[{"id":"staff","fullName":"Employee","employeeId":"EMP-1","stationId":"station","stationCode":"AAA","manpowerTypeName":"Technician","email":"ignored@example.test"}]}"""
    }
}

private class MemoryCatalogDao : SharedCatalogDao {
    val rows = MutableStateFlow<List<SharedCatalogSnapshotEntity>>(emptyList())
    val writes = mutableListOf<SharedCatalogSnapshotEntity>()
    var observations = 0
    var snapshotReads = 0
    var afterSnapshotRead: suspend () -> Unit = {}

    override fun observe(ownerSubject: String): Flow<List<SharedCatalogSnapshotEntity>> {
        observations++
        return rows.map { snapshots -> snapshots.filter { it.ownerSubject == ownerSubject } }
    }

    override suspend fun snapshots(ownerSubject: String): List<SharedCatalogSnapshotEntity> {
        snapshotReads++
        val result = rows.value.filter { it.ownerSubject == ownerSubject }
        afterSnapshotRead()
        return result
    }

    override suspend fun snapshot(ownerSubject: String, catalog: String): SharedCatalogSnapshotEntity? =
        rows.value.firstOrNull { it.ownerSubject == ownerSubject && it.catalog == catalog }

    override suspend fun put(snapshot: SharedCatalogSnapshotEntity) {
        writes += snapshot
        rows.value = rows.value.filterNot {
            it.ownerSubject == snapshot.ownerSubject && it.catalog == snapshot.catalog
        } + snapshot
    }

    override suspend fun clearAll() { rows.value = emptyList() }
}
