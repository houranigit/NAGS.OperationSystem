package com.nags.operations.data.repo

import com.nags.operations.data.FlightDetail

/** Partial write responses can prove work exists without returning newly restricted capabilities. */
internal fun FlightDetail.restrictCapabilitiesAfterKnownWork(): FlightDetail {
    val hasWork = arrivalSubmissions.isNotEmpty() || departureSubmissions.isNotEmpty() ||
        services.isNotEmpty() || tasks.isNotEmpty() || hasCancellationSubmission || cancellationSubmissions.isNotEmpty()
    if (!hasWork) return this
    return copy(canEditFlightDetails = if (isAdHocFlight) false else canEditFlightDetails,
        canAddServices = if (isUnassignedSearchFlight) false else canAddServices,
        canAddTasks = if (isUnassignedSearchFlight) false else canAddTasks)
}
