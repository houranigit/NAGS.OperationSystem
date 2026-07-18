package com.nags.operations.data.notifications

import com.nags.operations.data.api.HttpClientFactory
import java.time.Instant
import java.time.OffsetDateTime
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
    /** Null for schedule-level informational notifications that open My Flights without a sheet. */
    val flightId: String?,
    val recipientUserId: String? = null,
    val kind: String? = null,
    val scheduledArrivalUtc: String? = null,
    val leadTimeMinutes: Int? = null,
) {
    /** Reminder intents may outlive their useful OEM/FCM delivery window. */
    fun isExpiredReminder(now: Instant = Instant.now()): Boolean {
        if (kind != NotificationKinds.FlightReminder) return false
        val sta = scheduledArrivalUtc?.let(::parseNotificationInstant) ?: return true
        return !now.isBefore(sta)
    }
}

object NotificationKinds {
    const val StaffAssignedToFlight = "StaffAssignedToFlight"
    const val EmployeeInvitedToFlight = "EmployeeInvitedToFlight"
    const val FlightScheduleUpdated = "FlightScheduleUpdated"
    const val FlightReminder = "FlightReminder"
}

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
    val scheduledArrivalUtc: String?,
    val leadTimeMinutes: Int?,
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
            scheduledArrivalUtc?.let { put("scheduledArrivalUtc", it) }
            leadTimeMinutes?.let { put("leadTimeMinutes", it.toString()) }
        },
        isRead = false,
        createdAtUtc = createdAtUtc,
    )

    fun openRequest(now: Instant = Instant.now()): NotificationOpenRequest? {
        if (kind == NotificationKinds.FlightReminder && isExpiredReminder(now)) return null
        return NotificationOpenRequest(
            notificationId = id,
            flightId = flightId?.takeIf(String::isNotBlank),
            recipientUserId = recipientUserId,
            kind = kind,
            scheduledArrivalUtc = scheduledArrivalUtc,
            leadTimeMinutes = leadTimeMinutes,
        )
    }

    private fun isExpiredReminder(now: Instant): Boolean {
        val sta = scheduledArrivalUtc?.let(::parseNotificationInstant) ?: return true
        return !now.isBefore(sta)
    }

    companion object {
        private val supportedKinds = setOf(
            NotificationKinds.StaffAssignedToFlight,
            NotificationKinds.EmployeeInvitedToFlight,
            NotificationKinds.FlightScheduleUpdated,
            NotificationKinds.FlightReminder,
        )

        fun fromData(
            data: Map<String, String>,
            now: Instant = Instant.now(),
        ): NotificationPushPayload? {
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

            val flightId = value("flightId")
            val scheduledArrivalUtc = value("scheduledArrivalUtc")
            val leadTimeMinutes = value("leadTimeMinutes")?.toIntOrNull()
            if (kind != NotificationKinds.FlightScheduleUpdated && flightId == null) return null
            if (kind == NotificationKinds.FlightReminder) {
                val sta = scheduledArrivalUtc?.let(::parseNotificationInstant) ?: return null
                if (leadTimeMinutes == null || leadTimeMinutes <= 0 || !now.isBefore(sta)) return null
            }

            return NotificationPushPayload(
                id = id,
                kind = kind,
                recipientUserId = value("recipientUserId"),
                titleEn = value("titleEn").orEmpty(),
                bodyEn = value("bodyEn").orEmpty(),
                titleAr = value("titleAr").orEmpty(),
                bodyAr = value("bodyAr").orEmpty(),
                flightId = flightId,
                flightNumber = value("flightNumber"),
                inviterName = value("inviterName"),
                scheduledArrivalUtc = scheduledArrivalUtc,
                leadTimeMinutes = leadTimeMinutes,
                createdAtUtc = value("createdAtUtc") ?: value("createdAt")
                    ?: java.time.OffsetDateTime.now(java.time.ZoneOffset.UTC).toString(),
            )
        }
    }
}

fun NotificationDto.flightId(): String? = payload["flightId"]?.takeIf(String::isNotBlank)

fun NotificationDto.isExpiredReminder(now: Instant = Instant.now()): Boolean {
    if (kind != NotificationKinds.FlightReminder) return false
    val sta = payload["scheduledArrivalUtc"]?.let(::parseNotificationInstant) ?: return true
    return !now.isBefore(sta)
}

private fun parseNotificationInstant(value: String): Instant? =
    runCatching { OffsetDateTime.parse(value).toInstant() }
        .recoverCatching { Instant.parse(value) }
        .getOrNull()
