package com.nags.operations.ui.common

import androidx.compose.ui.graphics.Color
import com.nags.operations.data.FlightStatusKind
import com.nags.operations.data.WorkOrderStatusKind

object StatusColors {
    val FlightScheduled = Color(0xFF1976D2)
    val FlightInProgress = Color(0xFFFFA000)
    val FlightCompleted = Color(0xFF2E7D32)
    val FlightCanceled = Color(0xFFC62828)
    val FlightMerged = Color(0xFF6B7280)

    val WoSubmitted = Color(0xFF6B7280)
    val WoReturned = Color(0xFFEF6C00)
    val WoApproved = Color(0xFF2E7D32)
    val WoMerged = Color(0xFF6B7280)
}

fun FlightStatusKind?.color(): Color = when (this) {
    FlightStatusKind.Scheduled -> StatusColors.FlightScheduled
    FlightStatusKind.InProgress -> StatusColors.FlightInProgress
    FlightStatusKind.Completed -> StatusColors.FlightCompleted
    FlightStatusKind.Canceled -> StatusColors.FlightCanceled
    FlightStatusKind.Merged -> StatusColors.FlightMerged
    null -> StatusColors.FlightScheduled
}

fun FlightStatusKind?.label(): String = when (this) {
    FlightStatusKind.Scheduled -> "Scheduled"
    FlightStatusKind.InProgress -> "In progress"
    FlightStatusKind.Completed -> "Completed"
    FlightStatusKind.Canceled -> "Canceled"
    FlightStatusKind.Merged -> "Merged"
    null -> "—"
}

fun WorkOrderStatusKind?.color(): Color? = when (this) {
    WorkOrderStatusKind.Submitted -> StatusColors.WoSubmitted
    WorkOrderStatusKind.Returned -> StatusColors.WoReturned
    WorkOrderStatusKind.Approved -> StatusColors.WoApproved
    WorkOrderStatusKind.Merged -> StatusColors.WoMerged
    null -> null
}

fun WorkOrderStatusKind?.label(): String? = when (this) {
    WorkOrderStatusKind.Submitted -> "Submitted"
    WorkOrderStatusKind.Returned -> "Returned for changes"
    WorkOrderStatusKind.Approved -> "Approved"
    WorkOrderStatusKind.Merged -> "Merged"
    null -> null
}
