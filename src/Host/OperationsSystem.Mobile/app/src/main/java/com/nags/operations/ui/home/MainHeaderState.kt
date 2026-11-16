package com.nags.operations.ui.home

import com.nags.operations.AppGraph
import com.nags.operations.data.AdHocFlightContext
import com.nags.operations.data.SharedCatalog
import com.nags.operations.data.StaffCatalogItem
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.map

internal enum class MainHeaderSyncStatus {
    Loading, Online, Offline, Syncing, Pending, NeedsReview, SyncError, Synced,
}

internal data class MainHeaderState(
    val stationCode: String? = null,
    val stationCity: String? = null,
    val stationTimeZoneId: String? = null,
    val displayName: String? = null,
    val employeeNumber: String? = null,
    val manpowerTypeName: String? = null,
    val isOnline: Boolean = false,
    val syncStatus: MainHeaderSyncStatus = MainHeaderSyncStatus.Loading,
    val pendingCount: Int = 0,
    val reviewCount: Int = 0,
)

/** Reuses owner-scoped snapshots; no network request or flight-list membership is needed. */
internal fun observeMainHeader(graph: AppGraph): Flow<MainHeaderState> {
    val identity = combine(graph.adHocFlights.context, graph.sharedCatalogsRepository.stationStaffFlow(),
        graph.tokenStore.displayNameFlow, ::mainHeaderIdentity)
    val savedChanges = combine(listOf(
        graph.flightArrivals.changes.map { rows -> rows.map { it.status } },
        graph.flightDepartures.changes.map { rows -> rows.map { it.status } },
        graph.flightCancellations.changes.map { rows -> rows.map { it.status } },
        graph.flightServices.changes.map { rows -> rows.map { it.status } },
        graph.flightTasks.changes.map { rows -> rows.map { it.status } },
        graph.adHocFlights.pending.map { rows -> rows.map { it.status } },
    )) { snapshots -> mainHeaderSavedChanges(snapshots.flatMap { it }) }
    val cache = combine(graph.sharedCatalogsRepository.observeStatus(), graph.flightsRepository.syncStatus,
        graph.discoveryFlights.offlineSearchStatus) { catalogs, flights, search ->
        val hasError = catalogs.any { it.error != null || it.permissionDenied } ||
            flights.error != null || flights.permissionDenied || search.error != null || search.permissionDenied
        val prepared = !hasError && flights.lastSuccessUtc != null && search.lastSuccessUtc != null &&
            SharedCatalog.entries.all { kind -> catalogs.any { it.kind == kind && it.lastSuccessUtc != null } }
        HeaderCacheState(hasError, prepared)
    }
    // combine waits for all queue reads; empty initial placeholders cannot claim a successful sync.
    return combine(identity, savedChanges, cache, graph.networkMonitor.isOnline, graph.isSyncing) {
            header, saved, cached, online, syncing ->
        header.copy(isOnline = online, pendingCount = saved.pendingCount, reviewCount = saved.reviewCount,
            syncStatus = mainHeaderSyncStatus(online, syncing, saved.pendingCount, saved.reviewCount,
                cached.hasError, cached.prepared))
    }.distinctUntilChanged()
}

internal fun mainHeaderIdentity(context: AdHocFlightContext?, staff: List<StaffCatalogItem>,
    displayName: String?): MainHeaderState {
    val employee = context?.let { current -> staff.firstOrNull {
        it.id.equals(current.currentStaffMemberId, ignoreCase = true) &&
            it.stationId.equals(current.stationId, ignoreCase = true)
    } }
    return MainHeaderState(
        stationCode = context?.stationIata?.takeIf { it.isNotBlank() },
        stationCity = context?.stationCity?.takeIf { it.isNotBlank() },
        stationTimeZoneId = context?.stationTimeZoneId,
        displayName = employee?.fullName?.takeIf { it.isNotBlank() } ?: displayName,
        employeeNumber = employee?.employeeId?.trim()?.takeIf { it.isNotEmpty() },
        manpowerTypeName = employee?.manpowerTypeName?.takeIf { it.isNotBlank() },
    )
}

internal data class HeaderSavedChanges(val pendingCount: Int = 0, val reviewCount: Int = 0)

internal fun mainHeaderSavedChanges(statuses: List<String>): HeaderSavedChanges = HeaderSavedChanges(
    pendingCount = statuses.count { it == "pending" },
    reviewCount = statuses.count { it != "pending" && it != "acknowledged" },
)

internal fun mainHeaderSyncStatus(online: Boolean, syncing: Boolean, pendingCount: Int, reviewCount: Int,
    hasSyncError: Boolean, hasSynchronizedData: Boolean): MainHeaderSyncStatus = when {
    reviewCount > 0 -> MainHeaderSyncStatus.NeedsReview
    !online -> MainHeaderSyncStatus.Offline
    syncing -> MainHeaderSyncStatus.Syncing
    pendingCount > 0 -> MainHeaderSyncStatus.Pending
    hasSyncError -> MainHeaderSyncStatus.SyncError
    hasSynchronizedData -> MainHeaderSyncStatus.Synced
    else -> MainHeaderSyncStatus.Online
}

private data class HeaderCacheState(val hasError: Boolean, val prepared: Boolean)
