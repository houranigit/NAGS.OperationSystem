package com.nags.operations.ui.flights

import com.nags.operations.data.FlightDetail
import com.nags.operations.data.FlightListItem

internal data class FlightFormWorkTarget(val key: String, val completed: Boolean)

/** Only started entries have an active/done destination; planned placeholders remain drafts. */
internal fun FlightWorkDrafts.navigationTargets(): List<FlightFormWorkTarget> =
    services.filter { it.period.start.isNotBlank() }.map { FlightFormWorkTarget("service:${it.id}", it.period.end.isNotBlank()) } +
        tasks.filter { it.period.start.isNotBlank() }.map { FlightFormWorkTarget("task:${it.id}", it.period.end.isNotBlank()) }

/** Reuse list metadata for lifecycle/RTR, while the form's work counts navigate its current entries. */
internal fun FlightDetail.formCardSummary(listItem: FlightListItem?, targets: List<FlightFormWorkTarget>): FlightListItem =
    FlightListItem(
        id = id, flightNumber = flightNumber, customerIataCode = customerIataCode, customerName = customerName,
        stationIataCode = stationIata, operationTypeName = operationTypeName,
        scheduledArrivalUtc = scheduledArrivalUtc, scheduledDepartureUtc = scheduledDepartureUtc,
        isPerLanding = isPerLanding, aircraftTypeModel = aircraftTypeModel, customerId = customerId,
        customerLogoVersion = listItem?.customerLogoVersion, stationId = stationId,
        isMyFlight = listItem?.isMyFlight ?: false, isMyAdHocFlight = listItem?.isMyAdHocFlight ?: false,
        stationTimeZoneId = stationTimeZoneId,
        hasArrivalSubmission = if (listItem != null) listItem.hasArrivalSubmission else arrivalSubmissions.isNotEmpty(),
        hasDepartureSubmission = if (listItem != null) listItem.hasDepartureSubmission else departureSubmissions.isNotEmpty(),
        hasCancellationSubmission = if (listItem != null) listItem.hasCancellationSubmission else hasCancellationSubmission,
        hasOpenReturnToRamp = listItem?.hasOpenReturnToRamp,
        latestReturnToRampSequence = listItem?.latestReturnToRampSequence,
        activeWorkCount = targets.count { !it.completed }, completedWorkCount = targets.count { it.completed },
        latestCancellationAtUtc = listItem?.latestCancellationAtUtc,
    )
