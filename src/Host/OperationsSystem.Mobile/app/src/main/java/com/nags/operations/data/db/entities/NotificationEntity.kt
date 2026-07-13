package com.nags.operations.data.db.entities

import androidx.room.Entity
import androidx.room.PrimaryKey
import com.nags.operations.data.api.HttpClientFactory
import com.nags.operations.data.notifications.NotificationDto
import kotlinx.serialization.encodeToString

@Entity(tableName = "notifications")
data class NotificationEntity(
    @PrimaryKey val id: String,
    val recipientUserId: String?,
    val kind: String,
    val titleEn: String,
    val bodyEn: String,
    val titleAr: String,
    val bodyAr: String,
    val payloadJson: String,
    val isRead: Boolean,
    val isArchived: Boolean = false,
    val createdAtUtc: String,
    val readAtUtc: String?,
)

fun NotificationDto.toEntity(recipientUserId: String? = null): NotificationEntity = NotificationEntity(
    id = id,
    recipientUserId = recipientUserId,
    kind = kind,
    titleEn = titleEn,
    bodyEn = bodyEn,
    titleAr = titleAr,
    bodyAr = bodyAr,
    payloadJson = HttpClientFactory.json.encodeToString(payload),
    isRead = isRead,
    isArchived = false,
    createdAtUtc = createdAtUtc,
    readAtUtc = readAtUtc,
)

fun NotificationEntity.toDto(): NotificationDto = NotificationDto(
    id = id,
    kind = kind,
    titleEn = titleEn,
    bodyEn = bodyEn,
    titleAr = titleAr,
    bodyAr = bodyAr,
    payload = runCatching {
        HttpClientFactory.json.decodeFromString<Map<String, String>>(payloadJson)
    }.getOrDefault(emptyMap()),
    isRead = isRead,
    createdAtUtc = createdAtUtc,
    readAtUtc = readAtUtc,
)
