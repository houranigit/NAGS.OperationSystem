package com.nags.operations.ui.flights

import com.nags.operations.data.FlightStatusKind
import com.nags.operations.data.MobileFlightDto
import com.nags.operations.data.WorkOrderStatusKind

/** Branching rules for the flight actions sheet, using list-row fields only. */
enum class FlightSummaryActionsDecision {
    CreateOrCancel,
    UpdateOrReturnToRamp,
    CreateOnly,
    /** Completed flights expose only the future Return-to-ramp entry point. */
    CompletedReturnToRamp,
    ReadOnly,
}

fun deriveFlightSummaryActions(summary: MobileFlightDto): FlightSummaryActionsDecision {
    val flightStatus = FlightStatusKind.fromWire(summary.status)
        ?: return FlightSummaryActionsDecision.ReadOnly
    val myWo = WorkOrderStatusKind.fromWire(summary.myWorkOrder?.status)
    return when (flightStatus) {
        FlightStatusKind.Completed -> FlightSummaryActionsDecision.CompletedReturnToRamp
        FlightStatusKind.Canceled,
        FlightStatusKind.Merged -> FlightSummaryActionsDecision.ReadOnly
        FlightStatusKind.Scheduled -> FlightSummaryActionsDecision.CreateOrCancel
        FlightStatusKind.InProgress -> when {
            // Submitted or Returned = still editable by the author.
            myWo?.isEditable == true -> FlightSummaryActionsDecision.UpdateOrReturnToRamp
            summary.otherWorkOrdersExist -> FlightSummaryActionsDecision.CreateOnly
            else -> FlightSummaryActionsDecision.CreateOrCancel
        }
    }
}
