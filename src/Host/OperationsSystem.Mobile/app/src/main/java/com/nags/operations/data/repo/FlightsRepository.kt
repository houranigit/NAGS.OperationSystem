package com.nags.operations.data.repo

import com.nags.operations.data.db.AppDatabase
import com.nags.operations.data.db.entities.AdHocFlightEntity
import com.nags.operations.data.db.entities.FlightEntity
import com.nags.operations.data.db.entities.PerLandingFlightEntity
import kotlinx.coroutines.flow.Flow

/**
 * Reactive reads for the flight lists. The sync coordinator owns the writes so this class is
 * genuinely read-only — there's no `refresh()` here on purpose; the UI triggers refreshes
 * through the sync coordinator's public surface so every refresh shows up in the Sync Center.
 */
class FlightsRepository(private val db: AppDatabase) {
    /** Non-Per-Landing flights the signed-in user is rostered on, sorted by STA ascending. */
    fun myFlightsFlow(): Flow<List<FlightEntity>> = db.flightDao().observeAll()

    /** Reactive single My-Flights row — drives the invite screen's assigned/roster split. */
    fun myFlightFlow(id: String): Flow<FlightEntity?> = db.flightDao().observeById(id)

    /** Per-Landing flights at the signed-in user's home station, sorted by STA ascending. */
    fun perLandingFlightsFlow(): Flow<List<PerLandingFlightEntity>> = db.perLandingFlightDao().observeAll()

    /** Ad Hoc flights at the signed-in user's home station, sorted by STA ascending. */
    fun adHocFlightsFlow(): Flow<List<AdHocFlightEntity>> = db.adHocFlightDao().observeAll()

    /**
     * Resolves a flight from whichever local cache currently holds it (My flights,
     * Per-Landing, or Ad Hoc). Used when navigating into create-work-order from a list row.
     */
    suspend fun findWorkOrderFlight(id: String): WorkOrderFlightRow? =
        db.flightDao().getById(id)?.toWorkOrderFlightRow()
            ?: db.perLandingFlightDao().getById(id)?.toWorkOrderFlightRow()
            ?: db.adHocFlightDao().getById(id)?.toWorkOrderFlightRow()
}
