package com.nags.operations.ui.flights

import androidx.compose.runtime.Composable
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.res.stringResource
import com.nags.operations.R

internal val LocalFlightUserId = staticCompositionLocalOf<String?> { null }

/** Account IDs take precedence; older actuals can identify their submitter by staff ID. */
internal fun attributionName(name: String, actorUserId: String?, currentUserId: String?, youLabel: String,
    actorStaffMemberId: String? = null, currentStaffMemberId: String? = null): String {
    val isCurrentUser = if (!actorUserId.isNullOrBlank()) {
        !currentUserId.isNullOrBlank() && actorUserId.equals(currentUserId, true)
    } else !actorStaffMemberId.isNullOrBlank() && !currentStaffMemberId.isNullOrBlank() &&
        actorStaffMemberId.equals(currentStaffMemberId, true)
    return if (isCurrentUser) youLabel else name
}

@Composable
internal fun flightAttributionName(name: String, actorUserId: String?, actorStaffMemberId: String? = null,
    currentStaffMemberId: String? = null): String = attributionName(name, actorUserId, LocalFlightUserId.current,
    stringResource(R.string.flight_actuals_you), actorStaffMemberId, currentStaffMemberId)
