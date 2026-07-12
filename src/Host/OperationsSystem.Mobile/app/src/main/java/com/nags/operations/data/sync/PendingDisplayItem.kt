package com.nags.operations.data.sync

/**
 * Placeholder for optimistic-write chips — the NAGS mobile slice does not yet
 * expose an outbox on these lists; [FlightCard] keeps the same signature as
 * OperationsApplication for future parity.
 */
enum class OutboxOpStatus {
    Pending,
    Sending,
    Failed,
}

data class PendingDisplayItem(
    val id: String,
    val flightId: String,
    val status: OutboxOpStatus,
)
