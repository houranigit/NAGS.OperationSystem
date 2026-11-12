package com.nags.operations.ui.flights

import com.nags.operations.data.StaffCatalogItem
import org.junit.Assert.*
import org.junit.Test

class FlightOperationsCatalogsTest {
    @Test fun performerChoicesContainOnlyTheFlightStationAndUnknownStationsHaveNoChoices() {
        val local = StaffCatalogItem("local", "Local employee", "E1", "STATION-A", "AAA", "Engineer")
        val foreign = StaffCatalogItem("foreign", "Foreign employee", "E2", "station-b", "BBB", "Engineer")
        val catalogs = FlightOperationsCatalogs(stationStaff = listOf(local, foreign))
        assertEquals(listOf(local), catalogs.forStation("station-a").stationStaff)
        assertTrue(catalogs.forStation("unknown-station").stationStaff.isEmpty())
        assertTrue(catalogs.forStation("").stationStaff.isEmpty())
        assertEquals(listOf(local, foreign), catalogs.stationStaff)
    }
}
