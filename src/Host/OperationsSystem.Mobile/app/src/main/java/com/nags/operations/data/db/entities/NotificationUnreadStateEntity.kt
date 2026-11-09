package com.nags.operations.data.db.entities

import androidx.room.Entity
import androidx.room.PrimaryKey

/** Last known whole-inbox count; cached rows may cover only part of the inbox. */
@Entity(tableName = "notification_unread_state")
data class NotificationUnreadStateEntity(@PrimaryKey val recipientUserId: String, val count: Int)
