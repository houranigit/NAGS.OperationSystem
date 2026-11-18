package com.nags.operations.data.repo

import android.content.Context
import com.nags.operations.data.TokenStore
import com.nags.operations.data.api.HttpClientFactory
import com.nags.operations.data.api.NotificationsApi
import com.nags.operations.data.notifications.NotificationPreferenceKey
import com.nags.operations.data.notifications.NotificationPreferences
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock

/** The server owns these values. The private cache is read-only while offline. */
class NotificationPreferencesRepository internal constructor(
    private val currentOwner: suspend () -> String?,
    private val fetch: suspend () -> NotificationPreferences,
    private val patch: suspend (NotificationPreferenceKey, Boolean) -> NotificationPreferences,
    private val readCache: (String) -> NotificationPreferences?,
    private val writeCache: (String, NotificationPreferences) -> Unit,
) {
    constructor(context: Context, tokens: TokenStore, api: NotificationsApi) : this(
        currentOwner = {
            if (tokens.getAccessToken().isNullOrBlank()) null
            else sharedCatalogOwnerKey(HttpClientFactory.baseUrl, tokens.getSessionSubject())
        },
        fetch = api::preferences,
        patch = api::updatePreference,
        readCache = { owner ->
            context.applicationContext.getSharedPreferences("notification_preferences", Context.MODE_PRIVATE)
                .getString(owner, null)?.let { raw ->
                    runCatching { HttpClientFactory.json.decodeFromString<NotificationPreferences>(raw) }.getOrNull()
                }
        },
        writeCache = { owner, values ->
            context.applicationContext.getSharedPreferences("notification_preferences", Context.MODE_PRIVATE)
                .edit().putString(owner, HttpClientFactory.json.encodeToString(values)).apply()
        },
    )

    private val mutex = Mutex()

    suspend fun cached(): NotificationPreferences? = currentOwner()?.let(readCache)

    suspend fun refresh(): NotificationPreferences = mutex.withLock {
        val owner = checkNotNull(currentOwner()) { "Sign in to load notification settings." }
        val values = fetch()
        check(currentOwner() == owner) { "The signed-in account changed. Reopen notification settings." }
        writeCache(owner, values)
        values
    }

    suspend fun update(key: NotificationPreferenceKey, enabled: Boolean): NotificationPreferences = mutex.withLock {
        val owner = checkNotNull(currentOwner()) { "Sign in to update notification settings." }
        val values = patch(key, enabled)
        check(currentOwner() == owner) { "The signed-in account changed. Reopen notification settings." }
        writeCache(owner, values)
        values
    }
}
