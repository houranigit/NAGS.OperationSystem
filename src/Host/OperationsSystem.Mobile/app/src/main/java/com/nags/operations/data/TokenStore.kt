package com.nags.operations.data

import android.content.Context
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.map

private val Context.dataStore by preferencesDataStore("auth")

/**
 * Persists the JWT pair (access + refresh) plus a small bag of identity data
 * the app needs to render the welcome surface without a round-trip. Backed by
 * DataStore so reads/writes are async and safe across process restarts.
 *
 * Everything is scoped to the login flow for now — once the app grows, the
 * EmployeeId / station code will live next to the JWT for the same reason
 * (one source of truth for "who is signed in").
 */
class TokenStore(private val context: Context) {
    private val accessKey = stringPreferencesKey("access_token")
    private val refreshKey = stringPreferencesKey("refresh_token")
    private val accessExpiryKey = stringPreferencesKey("access_expires_at")
    private val refreshExpiryKey = stringPreferencesKey("refresh_expires_at")
    private val userIdKey = stringPreferencesKey("user_id")
    private val displayNameKey = stringPreferencesKey("user_display_name")
    private val emailKey = stringPreferencesKey("user_email")
    private val employeeIdKey = stringPreferencesKey("employee_id")
    private val stationCodeKey = stringPreferencesKey("station_code")

    val accessTokenFlow: Flow<String?> = context.dataStore.data.map { it[accessKey] }
    val userIdFlow: Flow<String?> = context.dataStore.data.map { it[userIdKey] }
    val displayNameFlow: Flow<String?> = context.dataStore.data.map { it[displayNameKey] }

    suspend fun getAccessToken(): String? = context.dataStore.data.map { it[accessKey] }.first()
    suspend fun getRefreshToken(): String? = context.dataStore.data.map { it[refreshKey] }.first()
    suspend fun getUserId(): String? = context.dataStore.data.map { it[userIdKey] }.first()
    suspend fun getDisplayName(): String? = context.dataStore.data.map { it[displayNameKey] }.first()
    suspend fun getEmployeeId(): String? = context.dataStore.data.map { it[employeeIdKey] }.first()
    suspend fun getStationCode(): String? = context.dataStore.data.map { it[stationCodeKey] }.first()

    suspend fun saveLogin(
        accessToken: String,
        refreshToken: String,
        accessExpiresAt: String,
        refreshExpiresAt: String,
        userId: String,
        displayName: String,
        email: String,
    ) {
        context.dataStore.edit {
            it[accessKey] = accessToken
            it[refreshKey] = refreshToken
            it[accessExpiryKey] = accessExpiresAt
            it[refreshExpiryKey] = refreshExpiresAt
            it[userIdKey] = userId
            it[displayNameKey] = displayName
            it[emailKey] = email
        }
    }

    /**
     * Cached `/me` profile — refreshed during sync so screens can read station
     * and employee id without a per-navigation network round-trip.
     */
    suspend fun saveEmployeeProfile(
        employeeId: String,
        stationCode: String,
        fullName: String? = null,
    ) {
        context.dataStore.edit {
            it[employeeIdKey] = employeeId
            it[stationCodeKey] = stationCode
            if (!fullName.isNullOrBlank()) {
                it[displayNameKey] = fullName
            }
        }
    }

    /** Replace just the JWT pair after a successful refresh. */
    suspend fun saveTokensAfterRefresh(
        accessToken: String,
        refreshToken: String,
        accessExpiresAt: String,
        refreshExpiresAt: String,
    ) {
        context.dataStore.edit {
            it[accessKey] = accessToken
            it[refreshKey] = refreshToken
            it[accessExpiryKey] = accessExpiresAt
            it[refreshExpiryKey] = refreshExpiresAt
        }
    }

    suspend fun clear() {
        context.dataStore.edit { prefs ->
            listOf(
                accessKey, refreshKey, accessExpiryKey, refreshExpiryKey,
                userIdKey, displayNameKey, emailKey, employeeIdKey, stationCodeKey,
            ).forEach { prefs.remove(it) }
        }
    }
}
