package com.nags.operations.ui.flights

import com.nags.operations.data.MobileFlightDto
import com.nags.operations.data.notifications.NotificationOpenRequest

/** Destination selected only after a row notification resolves its authoritative flight DTO. */
internal enum class FlightNotificationTab {
    MyFlights,
    AdHoc,
}

/** Schedule-level notifications have no DTO and retain the existing My Flights destination. */
internal fun notificationFlightTab(flight: MobileFlightDto?): FlightNotificationTab =
    if (flight?.isAdHoc == true) FlightNotificationTab.AdHoc else FlightNotificationTab.MyFlights

/** In-memory payload retained by the shell until the Ad Hoc tab has opened its sheet. */
internal data class AdHocFlightNotificationHandoff(
    val request: NotificationOpenRequest,
    val flight: MobileFlightDto,
)
