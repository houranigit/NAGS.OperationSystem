package com.nags.operations.data

/**
 * Backend `FlightStatus` — serialized as enum names on the wire
 * (`Scheduled`, `InProgress`, `Completed`, `Canceled`, `Merged`).
 */
enum class FlightStatusKind(val wire: String) {
    Scheduled("Scheduled"),
    InProgress("InProgress"),
    Completed("Completed"),
    Canceled("Canceled"),
    Merged("Merged"),
    ;

    companion object {
        fun fromWire(value: String?): FlightStatusKind? =
            entries.firstOrNull { it.wire.equals(value, ignoreCase = true) }
    }
}

/**
 * Backend `WorkOrderStatus` — `Submitted` and `Returned` are editable; `Approved` locks the
 * work order and settles the flight; `Merged` is a soft-archived merge source. There is no
 * Draft status server-side (drafts are local-only) and no Rejected status (returns reopen).
 */
enum class WorkOrderStatusKind(val wire: String) {
    Submitted("Submitted"),
    Returned("Returned"),
    Approved("Approved"),
    Merged("Merged"),
    ;

    val isEditable: Boolean get() = this == Submitted || this == Returned

    companion object {
        fun fromWire(value: String?): WorkOrderStatusKind? =
            entries.firstOrNull { it.wire.equals(value, ignoreCase = true) }
    }
}

/** Backend `WorkOrderType`: a work order either completes or cancels its flight. */
enum class WorkOrderTypeKind(val wire: String) {
    Completion("Completion"),
    Cancellation("Cancellation"),
    ;

    companion object {
        fun fromWire(value: String?): WorkOrderTypeKind? =
            entries.firstOrNull { it.wire.equals(value, ignoreCase = true) }
    }
}

/** Backend `TaskType` wire values (enum names). */
object TaskTypeKind {
    const val Major: String = "Major"
    const val Minor: String = "Minor"

    fun label(value: String): String = if (value.equals(Major, ignoreCase = true)) "Major" else "Minor"
}

/** Backend `TaskAttachmentKind` wire values (enum names). */
object TaskAttachmentKindValue {
    const val Image: String = "Image"
    const val Voice: String = "Voice"
    const val Document: String = "Document"
}
