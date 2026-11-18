package com.nags.operations.data.repo

import com.nags.operations.data.notifications.NotificationPreferenceKey
import com.nags.operations.data.notifications.NotificationPreferences
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.async
import kotlinx.coroutines.runBlocking
import org.junit.Assert.*
import org.junit.Test

class NotificationPreferencesRepositoryTest {
    @Test
    fun accountSwitchCannotPublishAnOldAccountsResponseToTheNewAccount() = runBlocking {
        var owner: String? = "api:user-a"
        val response = CompletableDeferred<NotificationPreferences>()
        val cache = mutableMapOf<String, NotificationPreferences>()
        val repository = NotificationPreferencesRepository({ owner }, { response.await() },
            { _, _ -> error("Unexpected write") }, cache::get, { key, value -> cache[key] = value })
        val pending = async { runCatching { repository.refresh() } }
        kotlinx.coroutines.yield()
        owner = "api:user-b"
        response.complete(NotificationPreferences(enabled = false))
        assertTrue(pending.await().isFailure)
        assertTrue(cache.isEmpty())
        assertNull(repository.cached())
    }

    @Test
    fun failedPatchKeepsAcknowledgedCacheAndSuccessfulPatchStoresEntireServerResponse() = runBlocking {
        val original = NotificationPreferences()
        val serverResponse = original.copy(flightAssignments = false, stationShares = false)
        var shouldFail = true
        var cached = original
        val repository = NotificationPreferencesRepository({ "api:user" }, { original }, { key, enabled ->
            assertEquals(NotificationPreferenceKey.FlightAssignments, key)
            assertFalse(enabled)
            if (shouldFail) throw java.io.IOException()
            serverResponse
        }, { cached }, { _, value -> cached = value })
        assertTrue(runCatching { repository.update(NotificationPreferenceKey.FlightAssignments, false) }.isFailure)
        assertEquals(original, repository.cached())
        shouldFail = false
        assertEquals(serverResponse, repository.update(NotificationPreferenceKey.FlightAssignments, false))
        assertEquals(serverResponse, repository.cached())
    }
}
