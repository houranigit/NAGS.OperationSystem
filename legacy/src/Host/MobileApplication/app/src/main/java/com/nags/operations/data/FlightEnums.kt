package com.nags.operations.data

/**
 * Mirrors backend flight / work-order status ints ([MobileFlightSummaryDto.status]).
 */
enum class FlightStatusKind(val value: Int) {
    Scheduled(0),
    InProgress(1),
    Completed(2),
    Canceled(3),
    ;

    companion object {
        fun fromValue(value: Int?): FlightStatusKind? =
            entries.firstOrNull { it.value == value }
    }
}

enum class WorkOrderStatusKind(val value: Int) {
    UnderReview(0),
    Approved(1),
    Rejected(2),
    Deleting(3),
    ;

    companion object {
        fun fromValue(value: Int?): WorkOrderStatusKind? =
            entries.firstOrNull { it.value == value }
    }
}

/**
 * Backend `TaskType` wire values — major/minor severity on work-order tasks.
 * See `Operations.Domain.Enumerations.TaskType`.
 */
object TaskTypeKind {
    const val Major: Int = 0
    const val Minor: Int = 1

    fun label(value: Int): String = if (value == Major) "Major" else "Minor"
}

/** Backend `TaskAttachmentKind`: Image / Voice / Document. */
object TaskAttachmentKindValue {
    const val Image: Int = 0
    const val Voice: Int = 1
    const val Document: Int = 2
}
