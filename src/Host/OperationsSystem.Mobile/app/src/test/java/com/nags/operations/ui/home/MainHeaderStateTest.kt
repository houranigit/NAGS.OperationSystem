package com.nags.operations.ui.home

import com.nags.operations.data.AdHocFlightContext
import com.nags.operations.data.StaffCatalogItem
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class MainHeaderStateTest {
    private val context = AdHocFlightContext("station", "JED", "King Abdulaziz International Airport",
        "Asia/Riyadh", "staff", true, true, true, true, true, emptyList(),
        "2026-11-15T12:00:00Z", "2026-11-15T00:00:00Z", "2026-11-16T00:00:00Z", stationCity = "Jeddah")
    private val employee = StaffCatalogItem("staff", "Hamza", "42", "station", "JED", "Technician")

    @Test fun stationAndEmployeeComeFromScopedContextWithoutAnyFlights() {
        val state = mainHeaderIdentity(context, listOf(employee), "Account name")
        assertEquals("JED", state.stationCode)
        assertEquals("Jeddah", state.stationCity)
        assertEquals("Asia/Riyadh", state.stationTimeZoneId)
        assertEquals("Hamza", state.displayName)
        assertEquals("42", state.employeeNumber)
        assertEquals("Technician", state.manpowerTypeName)
    }

    @Test fun employeeFromAnotherStationCannotPopulateTheHeader() {
        val state = mainHeaderIdentity(context, listOf(employee.copy(stationId = "old-station")), "Account name")
        assertEquals("Account name", state.displayName)
        assertNull(state.employeeNumber)
        assertNull(state.manpowerTypeName)
    }

    @Test fun anotherEmployeeAtTheSameStationCannotSupplyTheEmployeeNumber() {
        val state = mainHeaderIdentity(context, listOf(employee.copy(id = "another-staff")), "Account name")
        assertEquals("Account name", state.displayName)
        assertNull(state.employeeNumber)
        assertNull(state.manpowerTypeName)
    }

    @Test fun oldCachedContextDoesNotInventCityFromAirportNameOrTimezone() {
        val state = mainHeaderIdentity(context.copy(stationCity = null), listOf(employee), null)
        assertNull(state.stationCity)
        assertEquals("JED", state.stationCode)
    }

    @Test fun revokedContextDoesNotRetainStationOrStaffDetails() {
        val state = mainHeaderIdentity(null, listOf(employee), "Account name")
        assertNull(state.stationCode)
        assertNull(state.stationCity)
        assertNull(state.stationTimeZoneId)
        assertNull(state.employeeNumber)
        assertNull(state.manpowerTypeName)
        assertEquals("Account name", state.displayName)
    }

    @Test fun reviewAndPendingCountsExcludeAcknowledgedPlanningRows() {
        assertEquals(HeaderSavedChanges(2, 3),
            mainHeaderSavedChanges(listOf("pending", "conflict", "acknowledged", "failed", "pending", "unknown")))
    }

    @Test fun reviewRemainsVisibleOfflineAndDuringSync() {
        assertEquals(MainHeaderSyncStatus.NeedsReview, mainHeaderSyncStatus(false, true, 2, 1, true, true))
        assertEquals(MainHeaderSyncStatus.Offline, mainHeaderSyncStatus(false, true, 2, 0, true, true))
    }

    @Test fun pendingErrorsAndUnpreparedCachesCannotClaimSuccessfulSync() {
        assertEquals(MainHeaderSyncStatus.Pending, mainHeaderSyncStatus(true, false, 1, 0, false, true))
        assertEquals(MainHeaderSyncStatus.SyncError, mainHeaderSyncStatus(true, false, 0, 0, true, true))
        assertEquals(MainHeaderSyncStatus.Online, mainHeaderSyncStatus(true, false, 0, 0, false, false))
        assertEquals(MainHeaderSyncStatus.Synced, mainHeaderSyncStatus(true, false, 0, 0, false, true))
        assertEquals(MainHeaderSyncStatus.Syncing, mainHeaderSyncStatus(true, true, 0, 0, false, true))
    }
}
