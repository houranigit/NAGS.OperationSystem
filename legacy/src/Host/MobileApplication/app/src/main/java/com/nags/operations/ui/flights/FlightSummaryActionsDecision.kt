package com.nags.operations.ui.flights

import com.nags.operations.data.FlightStatusKind
import com.nags.operations.data.MobileFlightSummaryDto
import com.nags.operations.data.WorkOrderStatusKind

/** Same branching rules as FlightActionsViewModel.decide, using list-row fields only. */
enum class FlightSummaryActionsDecision {
    CreateOrCancel,
    UpdateOrReturnToRamp,
    CreateOnly,
    ReadOnly,
}

fun deriveFlightSummaryActions(summary: MobileFlightSummaryDto): FlightSummaryActionsDecision {
    val flightStatus = FlightStatusKind.fromValue(summary.status)
        ?: return FlightSummaryActionsDecision.ReadOnly
    val myWo = WorkOrderStatusKind.fromValue(summary.myWorkOrder?.status)
    return when (flightStatus) {
        FlightStatusKind.Completed, FlightStatusKind.Canceled -> FlightSummaryActionsDecision.ReadOnly
        FlightStatusKind.Scheduled -> FlightSummaryActionsDecision.CreateOrCancel
        FlightStatusKind.InProgress -> when {
            myWo == WorkOrderStatusKind.UnderReview ->
                FlightSummaryActionsDecision.UpdateOrReturnToRamp
            summary.otherWorkOrdersExist -> FlightSummaryActionsDecision.CreateOnly
            else -> FlightSummaryActionsDecision.CreateOrCancel
        }
    }
}
