package com.nags.operations.data

import android.content.Context
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.map
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import java.util.Base64

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
class TokenStore internal constructor(
    private val context: Context,
    private val protector: TokenProtector = AndroidKeystoreTokenProtector(),
) {
    private val accessKey = stringPreferencesKey("access_token")
    private val refreshKey = stringPreferencesKey("refresh_token")
    private val accessExpiryKey = stringPreferencesKey("access_expires_at")
    private val refreshExpiryKey = stringPreferencesKey("refresh_expires_at")
    private val userIdKey = stringPreferencesKey("user_id")
    private val displayNameKey = stringPreferencesKey("user_display_name")
    private val emailKey = stringPreferencesKey("user_email")
    private val employeeIdKey = stringPreferencesKey("employee_id")
    private val stationCodeKey = stringPreferencesKey("station_code")
    private val sessionSubjectKey = stringPreferencesKey("session_subject")

    val accessTokenFlow: Flow<String?> = context.dataStore.data.map { prefs ->
        prefs[accessKey]?.let(::unprotectOrNull)
    }
    val userIdFlow: Flow<String?> = context.dataStore.data.map { it[userIdKey] }
    val displayNameFlow: Flow<String?> = context.dataStore.data.map { it[displayNameKey] }

    suspend fun getAccessToken(): String? = context.dataStore.data
        .map { prefs -> prefs[accessKey]?.let(::unprotectOrNull) }
        .first()
    suspend fun getRefreshToken(): String? = context.dataStore.data
        .map { prefs -> prefs[refreshKey]?.let(::unprotectOrNull) }
        .first()
    suspend fun getUserId(): String? = context.dataStore.data.map { it[userIdKey] }.first()
    suspend fun getDisplayName(): String? = context.dataStore.data.map { it[displayNameKey] }.first()
    suspend fun getEmployeeId(): String? = context.dataStore.data.map { it[employeeIdKey] }.first()
    suspend fun getStationCode(): String? = context.dataStore.data.map { it[stationCodeKey] }.first()
    suspend fun getSessionSubject(): String? = context.dataStore.data.map { it[sessionSubjectKey] }.first()

    /**
     * Migrates tokens written by pre-Keystore builds and records the JWT owner used to protect
     * same-user offline data across an expired session.
     */
    suspend fun initializeSecureStorage() {
        val before = context.dataStore.data.first()
        val rawAccess = before[accessKey]
        val rawRefresh = before[refreshKey]
        val access = rawAccess?.let(::unprotectOrNull)
        val refresh = rawRefresh?.let(::unprotectOrNull)
        val inferredSubject = before[sessionSubjectKey]
            ?: access?.let(JwtSubject::fromAccessToken)
            ?: before[userIdKey]?.takeIf { it.isNotBlank() }

        context.dataStore.edit { prefs ->
            if (rawAccess != null && access != null && !protector.isProtected(rawAccess) && prefs[accessKey] == rawAccess) {
                prefs[accessKey] = protector.protect(access)
            }
            if (rawRefresh != null && refresh != null && !protector.isProtected(rawRefresh) && prefs[refreshKey] == rawRefresh) {
                prefs[refreshKey] = protector.protect(refresh)
            }
            if (prefs[sessionSubjectKey].isNullOrBlank() && !inferredSubject.isNullOrBlank()) {
                prefs[sessionSubjectKey] = inferredSubject
            }
        }
    }

    suspend fun saveLogin(
        accessToken: String,
        refreshToken: String,
        accessExpiresAt: String,
        refreshExpiresAt: String,
        userId: String,
        displayName: String,
        email: String,
        onAccountSwitch: suspend (previousSubject: String, newSubject: String) -> Unit = { _, _ -> },
    ) {
        val newSubject = requireNotNull(JwtSubject.fromAccessToken(accessToken)) {
            "The access token does not contain a valid subject claim."
        }
        val existing = context.dataStore.data.first()
        val previousSubject = existing[sessionSubjectKey]
            ?.takeIf { it.isNotBlank() }
            ?: existing[userIdKey]?.takeIf { it.isNotBlank() }
            ?: existing[accessKey]
                ?.let(::unprotectOrNull)
                ?.let(JwtSubject::fromAccessToken)

        if (previousSubject != null && !previousSubject.equals(newSubject, ignoreCase = true)) {
            // The callback must finish before the new bearer token becomes observable. This keeps
            // sync/outbox workers from ever running a previous user's rows under the new account.
            onAccountSwitch(previousSubject, newSubject)
        }

        context.dataStore.edit {
            it[accessKey] = protector.protect(accessToken)
            it[refreshKey] = protector.protect(refreshToken)
            it[accessExpiryKey] = accessExpiresAt
            it[refreshExpiryKey] = refreshExpiresAt
            if (userId.isNotBlank()) it[userIdKey] = userId
            it[displayNameKey] = displayName
            it[emailKey] = email
            it[sessionSubjectKey] = newSubject
        }
    }

    suspend fun saveIdentityProfile(userId: String, displayName: String, email: String) {
        context.dataStore.edit {
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
        val newSubject = requireNotNull(JwtSubject.fromAccessToken(accessToken)) {
            "The refreshed access token does not contain a valid subject claim."
        }
        val currentSubject = getSessionSubject()
        if (currentSubject != null && !currentSubject.equals(newSubject, ignoreCase = true)) {
            clearTokens()
            error("A token refresh attempted to change the authenticated account.")
        }
        context.dataStore.edit {
            it[accessKey] = protector.protect(accessToken)
            it[refreshKey] = protector.protect(refreshToken)
            it[accessExpiryKey] = accessExpiresAt
            it[refreshExpiryKey] = refreshExpiresAt
            it[sessionSubjectKey] = newSubject
        }
    }

    /** Drops only server credentials; cached identity and same-user offline work stay recoverable. */
    suspend fun clearTokens() {
        context.dataStore.edit { prefs ->
            listOf(accessKey, refreshKey, accessExpiryKey, refreshExpiryKey)
                .forEach { prefs.remove(it) }
        }
    }

    /** Removes credentials and the remembered owner. Use only after owner-scoped data is wiped. */
    suspend fun clearAll() {
        context.dataStore.edit { prefs ->
            listOf(
                accessKey, refreshKey, accessExpiryKey, refreshExpiryKey,
                userIdKey, displayNameKey, emailKey, employeeIdKey, stationCodeKey, sessionSubjectKey,
            ).forEach { prefs.remove(it) }
        }
    }

    private fun unprotectOrNull(raw: String): String? = runCatching { protector.unprotect(raw) }.getOrNull()
}

internal object JwtSubject {
    private val json = Json { ignoreUnknownKeys = true }

    fun fromAccessToken(token: String): String? = runCatching {
        val payload = token.split('.').getOrNull(1) ?: return null
        val decoded = Base64.getUrlDecoder().decode(payload.padBase64Url()).toString(Charsets.UTF_8)
        json.parseToJsonElement(decoded)
            .jsonObject["sub"]
            ?.jsonPrimitive
            ?.contentOrNull
            ?.takeIf { it.isNotBlank() }
    }.getOrNull()

    private fun String.padBase64Url(): String = this + "=".repeat((4 - length % 4) % 4)
}
