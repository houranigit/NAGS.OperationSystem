package com.nags.operations.data

import kotlinx.serialization.Serializable

/** Mirrors `LoginRequest` on the backend (`Identity.Api.Endpoints.IdentityRequests`). */
@Serializable
data class LoginRequest(
    val email: String,
    val password: String,
)

/**
 * Response of `POST /api/v1/identity/auth/mobile/login`. The server answers with either the
 * token set (no MFA) or an MFA challenge (`mfaRequired` + `mfaToken`); the nullable fields let
 * one DTO cover both shapes.
 */
@Serializable
data class MobileLoginResponse(
    val accessToken: String? = null,
    val accessTokenExpiresAtUtc: String? = null,
    val refreshToken: String? = null,
    val refreshTokenExpiresAtUtc: String? = null,
    val mfaRequired: Boolean = false,
    val mfaToken: String? = null,
)

/** Mirrors `MobileTokensResponse`: the refresh token travels in the JSON body for mobile. */
@Serializable
data class MobileTokensResponse(
    val accessToken: String,
    val accessTokenExpiresAtUtc: String,
    val refreshToken: String,
    val refreshTokenExpiresAtUtc: String,
)

@Serializable
data class LoginMfaRequest(val mfaToken: String, val code: String)

@Serializable
data class RefreshRequest(val refreshToken: String)

@Serializable
data class MobileLogoutRequest(val refreshToken: String?)

/** Mirrors `AuthenticatedUserDto` from `GET /api/v1/identity/me`. */
@Serializable
data class AuthenticatedUser(
    val id: String,
    val email: String,
    val displayName: String,
    val userType: String,
    val externalReferenceId: String? = null,
    val permissions: List<String> = emptyList(),
)
