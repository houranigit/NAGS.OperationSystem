package com.nags.operations.ui.invite

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Checkbox
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import com.nags.operations.R
import com.nags.operations.ui.flights.FlightsUiState
import com.nags.operations.ui.flights.FlightsViewModel

@Composable
fun InviteEmployeesDialog(state: FlightsUiState, viewModel: FlightsViewModel) {
    val busy = state.isInviting || state.isLoadingInvitations
    val canSend = !busy && state.invitationOptionsLoaded && state.canInviteEmployees &&
        state.detail?.isPerLanding == false && state.selectedInvitationIds.isNotEmpty() &&
        state.availableInvitationIds.containsAll(state.selectedInvitationIds)
    AlertDialog(
        onDismissRequest = viewModel::closeInvitations,
        title = { Text(stringResource(R.string.flights_invite_employees)) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
                Text(stringResource(R.string.flights_invite_hint))
                if (state.invitationStale) {
                    Text(stringResource(R.string.flights_invite_stale), color = MaterialTheme.colorScheme.error)
                }
                state.invitationError?.let { error ->
                    if (!state.invitationStale || !state.invitationOptionsLoaded) {
                        Text(error, color = MaterialTheme.colorScheme.error)
                    }
                }
                if (state.isLoadingInvitations) {
                    CircularProgressIndicator(Modifier.size(24.dp), strokeWidth = 2.dp)
                } else if (state.invitationOptions.isEmpty() && state.invitationOptionsLoaded) {
                    Text(stringResource(R.string.flights_invite_empty))
                }
                LazyColumn(Modifier.fillMaxWidth().heightIn(max = 320.dp)) {
                    items(state.invitationOptions, key = { it.staffMemberId }) { employee ->
                        val selected = employee.staffMemberId in state.selectedInvitationIds
                        val available = employee.staffMemberId in state.availableInvitationIds
                        val enabled = !busy && (available || selected)
                        Row(modifier = Modifier.fillMaxWidth().clickable(enabled = enabled) {
                            viewModel.selectInvitation(employee.staffMemberId, !selected)
                        }.padding(vertical = 4.dp), verticalAlignment = Alignment.CenterVertically) {
                            Checkbox(checked = selected, enabled = enabled, onCheckedChange = {
                                viewModel.selectInvitation(employee.staffMemberId, it)
                            })
                            Column {
                                Text(employee.fullName)
                                Text(employee.employeeId, style = MaterialTheme.typography.bodySmall)
                                if (!available) Text(stringResource(R.string.flights_invite_unavailable),
                                    style = MaterialTheme.typography.labelSmall,
                                    color = MaterialTheme.colorScheme.error)
                            }
                        }
                    }
                }
                TextButton(onClick = viewModel::refreshInvitations, enabled = !busy) {
                    Text(stringResource(R.string.flights_invite_refresh))
                }
            }
        },
        confirmButton = {
            TextButton(onClick = viewModel::inviteSelectedEmployees, enabled = canSend) {
                if (state.isInviting) CircularProgressIndicator(Modifier.size(18.dp), strokeWidth = 2.dp)
                else Text(stringResource(R.string.flights_invite_send))
            }
        },
        dismissButton = {
            TextButton(onClick = viewModel::closeInvitations, enabled = !state.isInviting) {
                Text(stringResource(R.string.flights_cancel))
            }
        },
    )
}
