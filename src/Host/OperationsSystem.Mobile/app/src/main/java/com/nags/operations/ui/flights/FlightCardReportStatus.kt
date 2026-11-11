package com.nags.operations.ui.flights

import com.nags.operations.data.FlightListItem
import com.nags.operations.data.db.entities.FlightCancellationPendingEntity
import com.nags.operations.data.db.entities.FlightArrivalPendingEntity
import com.nags.operations.data.db.entities.FlightDeparturePendingEntity
import com.nags.operations.ui.util.parseOffsetDateTime
import java.time.Duration
import java.time.Instant

/** A display summary of persisted reports and this account's durable local input, not approval. */
enum class FlightCardReportStatus { Scheduled, DueSoon, ArrivalNotRecorded, OnGround, ReturnedToRamp, Departed, Canceled, Conflict }

// Matches the existing 30-minute flight reminder milestone.
private val DueSoonWindow: Duration = Duration.ofMinutes(30)

internal fun FlightListItem.cardReportStatus(
    localArrival: Boolean = false,
    localDeparture: Boolean = false,
    localCancellation: Boolean = false,
    now: Instant = Instant.now(),
): FlightCardReportStatus {
    val arrival = hasArrivalSubmission == true || localArrival
    val departure = hasDepartureSubmission == true || localDeparture
    val cancellation = hasCancellationSubmission == true || localCancellation
    return when {
        cancellation && (arrival || departure || hasOpenReturnToRamp == true || latestReturnToRampSequence != null) -> FlightCardReportStatus.Conflict
        cancellation -> FlightCardReportStatus.Canceled
        hasOpenReturnToRamp == true -> FlightCardReportStatus.ReturnedToRamp
        departure -> FlightCardReportStatus.Departed
        arrival -> FlightCardReportStatus.OnGround
        // Legacy snapshots cannot establish that an arrival is missing until refreshed.
        hasArrivalSubmission == null || hasDepartureSubmission == null || hasCancellationSubmission == null -> FlightCardReportStatus.Scheduled
        else -> {
            val sta = runCatching { parseOffsetDateTime(scheduledArrivalUtc).toInstant() }.getOrNull()
            when {
                sta == null -> FlightCardReportStatus.Scheduled
                !sta.isAfter(now) -> FlightCardReportStatus.ArrivalNotRecorded
                Duration.between(now, sta) <= DueSoonWindow -> FlightCardReportStatus.DueSoon
                else -> FlightCardReportStatus.Scheduled
            }
        }
    }
}

/** Future minutes round up; elapsed minutes round down, keeping sub-minute values at STA now. */
internal fun FlightListItem.cardStaMinutes(now: Instant): Long? = runCatching {
    val remaining = Duration.between(now, parseOffsetDateTime(scheduledArrivalUtc).toInstant())
    if (remaining.isNegative) -remaining.negated().toMinutes()
    else remaining.toMinutes() + if (remaining.minusMinutes(remaining.toMinutes()).isZero) 0 else 1
}.getOrNull()

internal fun FlightListItem.cardPendingProgress(
    arrival: FlightArrivalPendingEntity? = null,
    departure: FlightDeparturePendingEntity? = null,
    cancellation: FlightCancellationPendingEntity? = null,
): FlightProgressStatus? {
    cancellation?.takeIf { it.flightId == id }?.let {
        return when {
            it.status != "pending" -> FlightProgressStatus.CancellationNeedsReview
            it.operation == "revert" -> FlightProgressStatus.CancellationRevertPending
            else -> FlightProgressStatus.CancellationPendingSync
        }
    }
    departure?.takeIf { it.flightId == id }?.let {
        return if (it.status == "pending") FlightProgressStatus.DeparturePendingSync else FlightProgressStatus.DepartureNeedsReview
    }
    arrival?.takeIf { it.flightId == id }?.let {
        return if (it.status == "pending") FlightProgressStatus.ArrivalPendingSync else FlightProgressStatus.ArrivalNeedsReview
    }
    return null
}

/** A possibly transmitted cancellation remains represented until its exact revert is acknowledged. */
internal fun FlightCancellationPendingEntity.keepsCancellationOnCard(): Boolean =
    operation == "save" || attemptOperation == "save" || submissionId != null
