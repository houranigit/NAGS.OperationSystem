package com.nags.operations.data.sync

import com.nags.operations.data.SharedCatalog
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.yield
import org.junit.Assert.assertEquals
import org.junit.Test

class FlightOfflineReconnectTest {
    @Test fun reconnectAndForegroundRefreshReprepareFlightsTogetherWithCatalogs() = runBlocking {
        val scope = CoroutineScope(SupervisorJob() + Dispatchers.Unconfined)
        val online = MutableStateFlow(false)
        var flightPreparations = 0
        val catalogs = mutableListOf<Set<SharedCatalog>>()
        val scheduler = CatalogSyncScheduler(MutableStateFlow("token"), online,
            { catalogs += it }, {}, scope, refreshFlightCache = { flightPreparations++ })
        try {
            scheduler.start()
            repeat(20) { yield() }
            assertEquals(0, flightPreparations)
            online.value = true
            repeat(20) { yield() }
            assertEquals(1, flightPreparations)
            online.value = false
            repeat(20) { yield() }
            scheduler.refreshNow()
            repeat(20) { yield() }
            assertEquals(1, flightPreparations)
            online.value = true
            repeat(20) { yield() }
            assertEquals(2, flightPreparations)
            scheduler.refreshNow() // The foreground/manual entry point uses the same complete target set.
            repeat(20) { yield() }
            assertEquals(3, flightPreparations)
            assertEquals(List(3) { SharedCatalog.entries.toSet() }, catalogs)
            scheduler.stop()
        } finally { scope.cancel() }
    }
}
