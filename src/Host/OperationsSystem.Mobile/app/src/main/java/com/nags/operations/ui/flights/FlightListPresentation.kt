package com.nags.operations.ui.flights

import com.nags.operations.data.FlightStatusKind
import com.nags.operations.data.MobileFlightDto
import com.nags.operations.data.MobileFlightWindowPhase
import com.nags.operations.data.evaluateMobileWindow
import com.nags.operations.data.isLocallyWithinMobileWindow
import java.time.Instant

/** The only statuses presented by the three mobile flight lists and their filter chips. */
internal val MobileFlightListStatusKinds: List<FlightStatusKind> = listOf(
    FlightStatusKind.Scheduled,
    FlightStatusKind.InProgress,
    FlightStatusKind.Completed,
)

/** Defensive list membership mirrors the three distinct mobile endpoints/caches. */
internal fun MobileFlightDto.belongsToMyFlightsList(): Boolean = !isPerLanding && !isAdHoc

internal fun MobileFlightDto.belongsToPerLandingFlightsList(): Boolean = isPerLanding && !isAdHoc

internal fun MobileFlightDto.belongsToAdHocFlightsList(): Boolean = isAdHoc

/**
 * Shared defensive list filter. Snapshot and realtime writes are already window-scoped, but
 * applying the same inclusive STA window here prevents an aging cached row from lingering on any
 * of the three tabs until the next network refresh.
 */
internal fun filterMobileFlightList(
    source: List<MobileFlightDto>,
    statusFilter: FlightStatusKind?,
    search: String,
    now: Instant = Instant.now(),
    includeFlight: (MobileFlightDto) -> Boolean = { true },
): List<MobileFlightDto> {
    if (source.isEmpty()) return source
    val query = search.trim()
    return source.asSequence()
        .filter(includeFlight)
        .filter { it.isLocallyWithinMobileWindow(now) }
        .filter { FlightStatusKind.fromWire(it.status) in MobileFlightListStatusKinds }
        .filter { statusFilter == null || FlightStatusKind.fromWire(it.status) == statusFilter }
        .filter { it.matchesSearch(query) }
        .toList()
}

/** Next instant at which a cached row can enter or leave the inclusive mobile window. */
internal fun nextMobileFlightWindowBoundary(
    source: List<MobileFlightDto>,
    now: Instant = Instant.now(),
): Instant? = source.asSequence()
    .mapNotNull { flight ->
        val window = flight.evaluateMobileWindow(now)
        when (window.phase) {
            MobileFlightWindowPhase.Before -> window.startsAt
            // The trailing boundary itself is included, so re-evaluate one millisecond later.
            MobileFlightWindowPhase.Within -> window.endsAt?.plusMillis(1)
            MobileFlightWindowPhase.After,
            MobileFlightWindowPhase.Unknown -> null
        }
    }
    .filter { it.isAfter(now) }
    .minOrNull()

internal fun MobileFlightDto.matchesSearch(query: String): Boolean {
    if (query.isBlank()) return true
    return listOf(
        flightNumber,
        customerName,
        customerIataCode.orEmpty(),
        stationIata,
        operationTypeName,
        aircraftTypeModel.orEmpty(),
    ).any { it.contains(query, ignoreCase = true) }
}
