package com.nags.operations.ui.flights

import com.nags.operations.data.FlightListItem
import com.nags.operations.data.db.entities.FlightCancellationPendingEntity

/** A display summary of persisted reports and this account's durable local input, not approval. */
enum class FlightCardReportStatus { Scheduled, Canceled, Arrived, Departed, Conflict }

internal fun FlightListItem.cardReportStatus(
    localArrival: Boolean = false,
    localDeparture: Boolean = false,
    localCancellation: Boolean = false,
): FlightCardReportStatus {
    val arrival = hasArrivalSubmission == true || localArrival
    val departure = hasDepartureSubmission == true || localDeparture
    val cancellation = hasCancellationSubmission == true || localCancellation
    return when {
        cancellation && (arrival || departure) -> FlightCardReportStatus.Conflict
        cancellation -> FlightCardReportStatus.Canceled
        departure -> FlightCardReportStatus.Departed
        arrival -> FlightCardReportStatus.Arrived
        else -> FlightCardReportStatus.Scheduled
    }
}

/** A possibly transmitted cancellation remains represented until its exact revert is acknowledged. */
internal fun FlightCancellationPendingEntity.keepsCancellationOnCard(): Boolean =
    operation == "save" || attemptOperation == "save" || submissionId != null
