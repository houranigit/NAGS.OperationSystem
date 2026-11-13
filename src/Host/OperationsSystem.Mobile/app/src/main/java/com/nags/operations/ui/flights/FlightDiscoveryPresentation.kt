package com.nags.operations.ui.flights

import com.nags.operations.data.FlightListItem
import com.nags.operations.data.parseFlightInstant
import java.time.LocalDate
import java.time.ZoneId

internal sealed interface FlightDiscoveryFeedRow {
    val key: String
    data class Heading(val date: LocalDate?, val stationTimeZoneId: String) : FlightDiscoveryFeedRow {
        override val key = "date:$stationTimeZoneId:$date"
    }
    data class Flight(val flight: FlightListItem) : FlightDiscoveryFeedRow {
        override val key = flight.id
    }
}

/** Group in feed order by operational date, independent of the device's time zone. */
internal fun flightDiscoveryRows(items: List<FlightListItem>, groupDates: Boolean): List<FlightDiscoveryFeedRow> {
    val unique = items.distinctBy { it.id }
    if (!groupDates) return unique.map(FlightDiscoveryFeedRow::Flight)
    return buildList {
        unique.groupBy { flight ->
            val date = runCatching { parseFlightInstant(flight.scheduledArrivalUtc)
                .atZone(ZoneId.of(flight.stationTimeZoneId)).toLocalDate() }.getOrNull()
            FlightDiscoveryFeedRow.Heading(date, flight.stationTimeZoneId)
        }.forEach { (heading, flights) ->
            add(heading)
            flights.forEach { add(FlightDiscoveryFeedRow.Flight(it)) }
        }
    }
}
