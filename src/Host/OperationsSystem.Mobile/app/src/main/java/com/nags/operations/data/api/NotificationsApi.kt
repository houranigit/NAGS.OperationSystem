package com.nags.operations.data.api

import com.nags.operations.data.TokenStore
import com.nags.operations.data.notifications.NotificationPageDto
import com.nags.operations.data.notifications.RegisterDeviceTokenRequest
import com.nags.operations.data.notifications.RevokeDeviceTokenRequest
import com.nags.operations.data.notifications.UnreadNotificationCountDto
import com.nags.operations.data.api.HttpClientFactory.bodyOrThrow
import io.ktor.client.HttpClient
import io.ktor.client.request.get
import io.ktor.client.request.parameter
import io.ktor.client.request.post
import io.ktor.client.request.setBody
import io.ktor.client.statement.bodyAsText
import io.ktor.http.ContentType
import io.ktor.http.contentType
import io.ktor.http.isSuccess

class NotificationsApi(
    tokenStore: TokenStore,
    private val client: HttpClient = HttpClientFactory.create(tokenStore),
) {
    private fun url(path: String) = HttpClientFactory.url("api/v1/notifications/${path.trimStart('/')}")

    suspend fun inbox(page: Int = 1, pageSize: Int = 20, unreadOnly: Boolean = false): NotificationPageDto =
        client.get(url("me")) {
            parameter("page", page)
            parameter("pageSize", pageSize)
            parameter("unreadOnly", unreadOnly)
        }.bodyOrThrow()

    suspend fun unreadCount(): Int =
        client.get(url("me/unread-count")).bodyOrThrow<UnreadNotificationCountDto>().count

    suspend fun markRead(id: String) = postNoContent("$id/read")
    suspend fun markAllRead() = postNoContent("me/mark-all-read")
    suspend fun archive(id: String) = postNoContent("$id/archive")
    suspend fun archiveAll() = postNoContent("me/archive-all")

    suspend fun registerDevice(body: RegisterDeviceTokenRequest) =
        postNoContent("me/devices", body)

    suspend fun revokeDevice(token: String) =
        postNoContent("me/devices/revoke", RevokeDeviceTokenRequest(token))

    private suspend fun postNoContent(path: String) {
        val response = client.post(url(path))
        if (!response.status.isSuccess()) {
            throw com.nags.operations.data.ApiException(response.status.value, response.bodyAsText())
        }
    }

    private suspend inline fun <reified T> postNoContent(path: String, body: T) {
        val response = client.post(url(path)) {
            contentType(ContentType.Application.Json)
            setBody(body)
        }
        if (!response.status.isSuccess()) {
            throw com.nags.operations.data.ApiException(response.status.value, response.bodyAsText())
        }
    }
}

