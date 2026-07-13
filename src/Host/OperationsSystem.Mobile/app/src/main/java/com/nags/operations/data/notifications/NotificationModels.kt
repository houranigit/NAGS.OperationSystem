package com.nags.operations.data.notifications

import com.nags.operations.data.api.HttpClientFactory
import kotlinx.serialization.Serializable

@Serializable
data class NotificationDto(
    val id: String,
    val kind: String,
    val titleEn: String,
    val bodyEn: String,
    val titleAr: String,
    val bodyAr: String,
    val payload: Map<String, String> = emptyMap(),
    val isRead: Boolean,
    val createdAtUtc: String,
    val readAtUtc: String? = null,
)

@Serializable
data class NotificationPageDto(
    val items: List<NotificationDto> = emptyList(),
    val page: Int,
    val pageSize: Int,
    val totalCount: Long,
    val totalPages: Int = 0,
    val hasPreviousPage: Boolean = false,
    val hasNextPage: Boolean = false,
)

@Serializable
data class UnreadNotificationCountDto(val count: Int)

@Serializable
data class RegisterDeviceTokenRequest(
    val token: String,
    val platform: String = "Android",
    val deviceId: String,
    val locale: String,
    val appVersion: String,
)

@Serializable
data class RevokeDeviceTokenRequest(val token: String)

data class NotificationOpenRequest(
    val notificationId: String?,
    val flightId: String,
    val recipientUserId: String? = null,
)

data class NotificationPushPayload(
    val id: String,
    val kind: String,
    val recipientUserId: String?,
    val titleEn: String,
    val bodyEn: String,
    val titleAr: String,
    val bodyAr: String,
    val flightId: String?,
    val flightNumber: String?,
    val inviterName: String?,
    val createdAtUtc: String,
) {
    fun toDto(): NotificationDto = NotificationDto(
        id = id,
        kind = kind,
        titleEn = titleEn,
        bodyEn = bodyEn,
        titleAr = titleAr,
        bodyAr = bodyAr,
        payload = buildMap {
            flightId?.let { put("flightId", it) }
            flightNumber?.let { put("flightNumber", it) }
            inviterName?.let { put("inviterName", it) }
        },
        isRead = false,
        createdAtUtc = createdAtUtc,
    )

    fun openRequest(): NotificationOpenRequest? = flightId?.takeIf(String::isNotBlank)?.let {
        NotificationOpenRequest(id, it, recipientUserId)
    }

    companion object {
        private val supportedKinds = setOf("StaffAssignedToFlight", "EmployeeInvitedToFlight")

        fun fromData(data: Map<String, String>): NotificationPushPayload? {
            val id = (data["notificationId"] ?: data["id"]).orEmpty().trim()
            val kind = data["kind"].orEmpty().trim()
            if (id.isEmpty() || kind !in supportedKinds) return null

            val nestedPayload = data["payloadJson"]?.let { raw ->
                runCatching {
                    HttpClientFactory.json.decodeFromString<Map<String, String>>(raw)
                }.getOrNull()
            }.orEmpty()
            fun value(name: String): String? = data[name]?.takeIf(String::isNotBlank)
                ?: nestedPayload[name]?.takeIf(String::isNotBlank)

            return NotificationPushPayload(
                id = id,
                kind = kind,
                recipientUserId = value("recipientUserId"),
                titleEn = value("titleEn").orEmpty(),
                bodyEn = value("bodyEn").orEmpty(),
                titleAr = value("titleAr").orEmpty(),
                bodyAr = value("bodyAr").orEmpty(),
                flightId = value("flightId"),
                flightNumber = value("flightNumber"),
                inviterName = value("inviterName"),
                createdAtUtc = value("createdAtUtc") ?: value("createdAt")
                    ?: java.time.OffsetDateTime.now(java.time.ZoneOffset.UTC).toString(),
            )
        }
    }
}

fun NotificationDto.flightId(): String? = payload["flightId"]?.takeIf(String::isNotBlank)

