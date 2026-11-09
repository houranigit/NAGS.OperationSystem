package com.nags.operations.data.db.entities

import androidx.room.Entity
import androidx.room.PrimaryKey
import com.nags.operations.data.notifications.NotificationCategory

/** Last known whole-inbox count; cached rows may cover only part of the inbox. */
@Entity(tableName = "notification_unread_state")
data class NotificationUnreadStateEntity(
    @PrimaryKey val recipientUserId: String, val count: Int,
    val invitations: Int? = null, val reminders: Int? = null, val flightUpdates: Int? = null,
) {
    fun forCategory(category: NotificationCategory): Int? = when (category) {
        NotificationCategory.Invitations -> invitations
        NotificationCategory.Reminders -> reminders
        NotificationCategory.FlightUpdates -> flightUpdates
    }

    fun adjustCategory(category: NotificationCategory?, delta: Int): NotificationUnreadStateEntity = when (category) {
        NotificationCategory.Invitations -> copy(invitations = invitations?.let { (it + delta).coerceAtLeast(0) })
        NotificationCategory.Reminders -> copy(reminders = reminders?.let { (it + delta).coerceAtLeast(0) })
        NotificationCategory.FlightUpdates -> copy(flightUpdates = flightUpdates?.let { (it + delta).coerceAtLeast(0) })
        null -> this
    }
}
