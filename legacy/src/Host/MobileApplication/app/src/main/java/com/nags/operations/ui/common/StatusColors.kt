package com.nags.operations.ui.common

import androidx.compose.ui.graphics.Color
import com.nags.operations.data.FlightStatusKind
import com.nags.operations.data.WorkOrderStatusKind

object StatusColors {
    val FlightScheduled = Color(0xFF1976D2)
    val FlightInProgress = Color(0xFFFFA000)
    val FlightCompleted = Color(0xFF2E7D32)
    val FlightCanceled = Color(0xFFC62828)

    val WoUnderReview = Color(0xFF6B7280)
    val WoApproved = Color(0xFF2E7D32)
    val WoRejected = Color(0xFFC62828)
    val WoDeleting = Color(0xFFEF6C00)
}

fun FlightStatusKind?.color(): Color = when (this) {
    FlightStatusKind.Scheduled -> StatusColors.FlightScheduled
    FlightStatusKind.InProgress -> StatusColors.FlightInProgress
    FlightStatusKind.Completed -> StatusColors.FlightCompleted
    FlightStatusKind.Canceled -> StatusColors.FlightCanceled
    null -> StatusColors.FlightScheduled
}

fun FlightStatusKind?.label(): String = when (this) {
    FlightStatusKind.Scheduled -> "Scheduled"
    FlightStatusKind.InProgress -> "In progress"
    FlightStatusKind.Completed -> "Completed"
    FlightStatusKind.Canceled -> "Canceled"
    null -> "—"
}

fun WorkOrderStatusKind?.color(): Color? = when (this) {
    WorkOrderStatusKind.UnderReview -> StatusColors.WoUnderReview
    WorkOrderStatusKind.Approved -> StatusColors.WoApproved
    WorkOrderStatusKind.Rejected -> StatusColors.WoRejected
    WorkOrderStatusKind.Deleting -> StatusColors.WoDeleting
    null -> null
}

fun WorkOrderStatusKind?.label(): String? = when (this) {
    WorkOrderStatusKind.UnderReview -> "Under review"
    WorkOrderStatusKind.Approved -> "Approved"
    WorkOrderStatusKind.Rejected -> "Rejected"
    WorkOrderStatusKind.Deleting -> "Removing"
    null -> null
}
