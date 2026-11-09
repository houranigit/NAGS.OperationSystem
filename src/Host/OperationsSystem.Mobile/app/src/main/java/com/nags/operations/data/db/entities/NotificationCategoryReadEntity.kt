package com.nags.operations.data.db.entities

import androidx.room.Entity

/** A category's local read watermark and its durable acknowledgement, independent of the screen. */
@Entity(tableName = "notification_category_reads", primaryKeys = ["recipientUserId", "category"])
data class NotificationCategoryReadEntity(
    val recipientUserId: String,
    val category: String,
    val throughCreatedAtUtc: String,
    val localReadAtUtc: String,
    val pending: Boolean,
    val unreadDelta: Int,
    val serverReadAtUtc: String? = null,
)
