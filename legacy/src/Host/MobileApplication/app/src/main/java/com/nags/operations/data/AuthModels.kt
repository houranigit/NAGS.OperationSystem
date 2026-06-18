package com.nags.operations.data

import kotlinx.serialization.Serializable

/** Mirrors `LoginCommand` on the backend (`Identity.Application.Commands.Login`). */
@Serializable
data class LoginRequest(
    val emailOrUsername: String,
    val password: String,
    val deviceInfo: String? = null,
    val ipAddress: String? = null,
    val userAgent: String? = null,
)

/** Mirrors `LoginCommandResponse` returned by `POST /api/identity/auth/login`. */
@Serializable
data class LoginResponse(
    val accessToken: String,
    val refreshToken: String,
    val accessTokenExpiresAt: String,
    val refreshTokenExpiresAt: String,
    val userId: String,
    val username: String,
    val email: String,
    val userType: String,
    val permissions: List<String> = emptyList(),
)

@Serializable
data class RefreshRequest(val refreshToken: String)
