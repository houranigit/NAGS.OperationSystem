package com.nags.operations.ui.workorder

import com.nags.operations.data.db.entities.FlightServiceSummary
import com.nags.operations.data.repo.WorkOrderFlightRow
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class WorkOrderServiceLineDefaultsTest {
    @Test
    fun per_landing_work_order_starts_without_service_lines() {
        var allocatedKeys = 0

        val result = serviceLinesToPrefill(
            flight(isPerLanding = true),
            nextKey = { (++allocatedKeys).toLong() },
        )

        assertTrue(result.isEmpty())
        assertEquals(0, allocatedKeys)
    }

    @Test
    fun normal_work_order_keeps_real_planned_service_prefill() {
        val result = serviceLinesToPrefill(
            flight(isPerLanding = false),
            nextKey = { 7L },
        )

        val line = result.single()
        assertEquals(7L, line.localKey)
        assertEquals("service-1", line.serviceId)
        assertEquals("2026-07-18T10:00:00Z", line.fromIso)
        assertEquals("2026-07-18T12:00:00Z", line.toIso)
    }

    private fun flight(isPerLanding: Boolean) = WorkOrderFlightRow(
        id = "flight-1",
        flightNumber = "100",
        operationTypeName = "Transit",
        sta = "2026-07-18T10:00:00Z",
        std = "2026-07-18T12:00:00Z",
        aircraftTypeId = null,
        aircraftTypeModel = null,
        customerName = "Customer",
        customerIataCode = "NA",
        stationIata = "ORD",
        isPerLanding = isPerLanding,
        isAdHoc = false,
        plannedServices = listOf(
            FlightServiceSummary("per-landing", "Aircraft Per Landing", isAircraftPerLanding = true),
            FlightServiceSummary("service-1", "Baggage"),
        ),
    )
}
