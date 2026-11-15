package com.nags.operations.ui.flights

import com.nags.operations.R
import com.nags.operations.data.OperationalWorkPeriod
import com.nags.operations.data.conflictingWorkPeriodIds
import java.time.ZoneId

/** Compare the final visible graph; unchanged historical pairs do not prevent unrelated saves. */
internal fun serviceOverlapValidation(
    services: List<FlightServiceDraft>,
    zone: ZoneId,
    changedIds: Set<String>,
): Map<String, Map<String, Int>> {
    val periods = services.filter { it.period.start.isNotBlank() }.map { draft ->
        val input = draft.serviceInput(zone)
        OperationalWorkPeriod(draft.id, draft.serviceId, input.startUtc, input.endUtc)
    }
    return conflictingWorkPeriodIds(periods).intersect(changedIds)
        .associateWith { mapOf("startUtc" to R.string.flight_service_overlap) }
}
